import { AlertCircle, ChevronRight, Filter, Plus, Printer, RefreshCw, Search } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { Badge, Button, Input, Modal, PageTitle, Select } from "../components/ui";
import { apiAdapter, generateUuid, getPendingOrderStorageKey } from "../services/apiAdapter";
import { realtimeAdapter, type RealtimeEvent } from "../services/realtimeAdapter";
import { useAuth } from "../context/AuthContext";
import { OrderBadge } from "./DashboardPage";
import type { Order, Sku } from "../types/domain";

interface PendingOrderState {
  idempotencyKey: string;
  branchId: string;
  warehouseId: string;
  skuId: string;
  quantity: number;
  channel: Order["channel"];
  customer: string;
}

function loadSavedPendingOrder(tenantId?: string): PendingOrderState | null {
  try {
    const key = getPendingOrderStorageKey(tenantId);
    const raw = sessionStorage.getItem(key);
    if (!raw) return null;
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

function savePendingOrder(data: PendingOrderState, tenantId?: string) {
  try {
    const key = getPendingOrderStorageKey(tenantId);
    sessionStorage.setItem(key, JSON.stringify(data));
  } catch {}
}

function clearSavedPendingOrder(tenantId?: string) {
  try {
    const key = getPendingOrderStorageKey(tenantId);
    sessionStorage.removeItem(key);
  } catch {}
}

export function OrdersPage() {
  const { account } = useAuth();
  const [ordersList, setOrdersList] = useState<Order[]>([]);
  const [skusList, setSkusList] = useState<Sku[]>([]);
  const [branches, setBranches] = useState<Array<{ id: string; name: string }>>([]);
  const [warehouses, setWarehouses] = useState<Array<{ id: string; name: string; branchId: string }>>([]);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [selected, setSelected] = useState<Order | null>(null);
  const [status, setStatus] = useState<string>("Draft");
  const [cancelReason, setCancelReason] = useState("");
  const [inventoryReloaded, setInventoryReloaded] = useState(false);

  // Bộ lọc
  const [searchQuery, setSearchQuery] = useState("");
  const [channelFilter, setChannelFilter] = useState("all");

  // [A04 & A08] Modal tạo đơn & giữ chỗ tồn kho theo đúng Chi nhánh / Kho cụ thể (POST /api/inventory/reserve)
  const [createModal, setCreateModal] = useState(false);
  const [newCustomer, setNewCustomer] = useState("Khách hàng Admin");
  const [newChannel, setNewChannel] = useState<Order["channel"]>("Shopee");
  const [newBranchId, setNewBranchId] = useState("");
  const [newWarehouseId, setNewWarehouseId] = useState("");
  const [newSkuId, setNewSkuId] = useState("");
  const [newQuantity, setNewQuantity] = useState(1);
  const [createError, setCreateError] = useState<string | null>(null);
  const [warehouseStockList, setWarehouseStockList] = useState<Sku[]>([]);
  const [loadingWhStock, setLoadingWhStock] = useState(false);
  const [whStockError, setWhStockError] = useState<string | null>(null);
  const isCreatingOrderRef = useRef(false);
  const whStockReqSeqRef = useRef(0);

  // [F2] Duy trì Idempotency-Key UUID cho luồng tạo đơn & giữ chỗ
  const [orderIdempotencyKey, setOrderIdempotencyKey] = useState<string | null>(null);
  const [isRetryingOrder, setIsRetryingOrder] = useState(false);

  const loadOrders = async () => {
    setLoading(true);
    setLoadError(null);
    const [res, skuRes, brRes, whRes] = await Promise.all([
      apiAdapter.getOrders(account.tenantId),
      apiAdapter.getSkus(),
      apiAdapter.getBranches(),
      apiAdapter.getWarehouses(),
    ]);

    if (skuRes.error) {
      setLoadError(skuRes.error.message);
    }

    if (skuRes.data) {
      setSkusList(skuRes.data);
      const firstActive = skuRes.data.find((s) => s.isActive !== false) || skuRes.data[0];
      if (firstActive?.id && !newSkuId) setNewSkuId(firstActive.id);
    }

    const brData = brRes.data || [];
    const whData = whRes.data || [];
    setBranches(brData);
    setWarehouses(whData);

    const initBranch = newBranchId || brData[0]?.id || "";
    if (initBranch) {
      setNewBranchId(initBranch);
      const branchWhs = whData.filter((w: any) => w.branchId === initBranch);
      if (!newWarehouseId || !branchWhs.some((w: any) => w.id === newWarehouseId)) {
        setNewWarehouseId(branchWhs[0]?.id || "");
      }
    }

    if (res.data) {
      setOrdersList(res.data);
      if (res.data.length > 0 && !selected) {
        setSelected(res.data[0]);
        setStatus(res.data[0].status);
      } else if (res.data.length === 0) {
        setSelected(null);
      }
    }
    setLoading(false);
  };

  useEffect(() => {
    loadOrders();
  }, [account.tenantId]);

  // [A08 & F3] Khi mở Modal Tạo đơn và đổi Chi nhánh / Kho, tải số dư thực tế tại đúng Kho đó (chống stale data)
  useEffect(() => {
    async function loadSelectedWarehouseStock() {
      if (!createModal || !newBranchId || !newWarehouseId) {
        setWarehouseStockList([]);
        setWhStockError(null);
        return;
      }
      const seq = ++whStockReqSeqRef.current;
      setWarehouseStockList([]); // Reset ngay lập tức để không hiển thị số dư của kho cũ
      setWhStockError(null);
      setLoadingWhStock(true);
      const res = await apiAdapter.getBalances({
        branchId: newBranchId,
        warehouseId: newWarehouseId,
      });
      if (seq !== whStockReqSeqRef.current) return;
      setLoadingWhStock(false);
      if (res.error) {
        setWarehouseStockList([]);
        setWhStockError(res.error.message || "Không thể tải số dư kho");
      } else {
        setWarehouseStockList(res.data || []);
        setWhStockError(null);
      }
    }
    loadSelectedWarehouseStock();
  }, [createModal, newBranchId, newWarehouseId]);

  const modalBranchWarehouses = useMemo(
    () => warehouses.filter((w) => w.branchId === newBranchId),
    [warehouses, newBranchId]
  );

  useEffect(() => {
    if (!selected) return;
    const applyEvent = (event: RealtimeEvent) => {
      if (event.type !== "reservation.expired" || event.orderCode !== selected.code) return;
      setStatus("Cancelled");
      setCancelReason(event.reason);
      setInventoryReloaded(true);
    };
    const latest = realtimeAdapter.getLatestEvent();
    if (latest) applyEvent(latest);
    return realtimeAdapter.subscribe(applyEvent);
  }, [selected?.code]);

  const selectOrder = (order: Order) => {
    setSelected(order);
    setStatus(order.status);
    setCancelReason("");
    setInventoryReloaded(false);
  };

  // [A01 & A11] Lưu trạng thái đơn hàng theo đúng khóa oism_orders_${tenantId}
  const updateOrderStatus = (nextStatus: Order["status"], reason = "") => {
    setStatus(nextStatus);
    if (reason) setCancelReason(reason);
    if (!selected) return;
    const nextList = ordersList.map((o) => (o.id === selected.id ? { ...o, status: nextStatus } : o));
    setOrdersList(nextList);
    setSelected({ ...selected, status: nextStatus });
    apiAdapter.saveSessionOrders(nextList, account.tenantId);
  };

  // [F2] Mở modal tạo đơn & khôi phục thao tác dở dang nếu có
  const openCreateModal = () => {
    setCreateError(null);
    const pending = loadSavedPendingOrder(account.tenantId);
    if (pending && pending.idempotencyKey) {
      setOrderIdempotencyKey(pending.idempotencyKey);
      if (pending.branchId) setNewBranchId(pending.branchId);
      if (pending.warehouseId) setNewWarehouseId(pending.warehouseId);
      if (pending.skuId) setNewSkuId(pending.skuId);
      if (pending.quantity) setNewQuantity(pending.quantity);
      if (pending.channel) setNewChannel(pending.channel);
      if (pending.customer) setNewCustomer(pending.customer);
      setIsRetryingOrder(true);
    } else {
      const freshKey = generateUuid();
      setOrderIdempotencyKey(freshKey);
      setIsRetryingOrder(false);
    }
    setCreateModal(true);
  };

  // [F2] Hủy thao tác dở dang và sinh key mới để tạo đơn mới
  const handleDiscardPendingOrder = () => {
    if (isRetryingOrder) {
      const confirmDiscard = window.confirm(
        "Thao tác tạo đơn / giữ chỗ trước đó có thể đã được tiếp nhận một phần trên máy chủ. " +
          "Bỏ thao tác dở dang sẽ dừng theo dõi và sinh định danh mới cho đơn tiếp theo. Bạn có chắc chắn muốn bỏ qua?"
      );
      if (!confirmDiscard) return;
    }
    clearSavedPendingOrder(account.tenantId);
    const freshKey = generateUuid();
    setOrderIdempotencyKey(freshKey);
    setIsRetryingOrder(false);
    setCreateError(null);
  };

  // [F2] Khi người dùng thay đổi dữ liệu đặt hàng, sinh UUID mới cho ý định mới
  const handleFormChange = () => {
    if (isRetryingOrder) {
      clearSavedPendingOrder(account.tenantId);
      setIsRetryingOrder(false);
    }
    setOrderIdempotencyKey(generateUuid());
    setCreateError(null);
  };

  const handleCreateOrder = async () => {
    if (isCreatingOrderRef.current) return;
    isCreatingOrderRef.current = true;
    try {
      setCreateError(null);
      const chosenSku = skusList.find((s) => s.id === newSkuId);
      if (!chosenSku) {
        setCreateError("Vui lòng chọn một sản phẩm / SKU hợp lệ.");
        return;
      }
      // [A10] Chặn chọn SKU đã ngừng hoạt động
      if (chosenSku.isActive === false) {
        setCreateError(`Mã SKU ${chosenSku.code} đang ở trạng thái [Ngừng hoạt động], không thể tạo đơn bán.`);
        return;
      }
      if (!newBranchId || !newWarehouseId) {
        setCreateError("Vui lòng chọn Chi nhánh và Kho xuất hàng để giữ chỗ tồn kho.");
        return;
      }
      if (newQuantity <= 0) {
        setCreateError("Số lượng đặt hàng phải lớn hơn 0.");
        return;
      }

      const selectedBranchObj = branches.find((b) => b.id === newBranchId);
      const selectedWarehouseObj = warehouses.find((w) => w.id === newWarehouseId);

      // [F2] Giữ UUID ổn định theo ý định tạo đơn để tránh giữ lặp khi retry
      const key = orderIdempotencyKey || generateUuid();
      setOrderIdempotencyKey(key);

      const pendingPayload: PendingOrderState = {
        idempotencyKey: key,
        branchId: newBranchId,
        warehouseId: newWarehouseId,
        skuId: chosenSku.id,
        quantity: newQuantity,
        channel: newChannel,
        customer: newCustomer.trim() || "Khách lẻ",
      };
      savePendingOrder(pendingPayload, account.tenantId);

      setLoading(true);
      const res = await apiAdapter.checkout(
        chosenSku,
        newQuantity,
        newChannel,
        newCustomer.trim() || "Khách lẻ",
        {
          branchId: newBranchId,
          branchName: selectedBranchObj?.name,
          warehouseId: newWarehouseId,
          warehouseName: selectedWarehouseObj?.name,
          tenantId: account.tenantId,
          idempotencyKey: key,
          isRetry: isRetryingOrder,
        }
      );
      setLoading(false);

      if (res.error) {
        setCreateError(`${res.error.message} (Định danh: ${key.slice(0, 8)}...)`);
        setIsRetryingOrder(true);
        return;
      }

      clearSavedPendingOrder(account.tenantId);
      setOrderIdempotencyKey(null);
      setIsRetryingOrder(false);
      setCreateModal(false);
      await loadOrders();
    } finally {
      isCreatingOrderRef.current = false;
    }
  };

  const filteredOrders = ordersList.filter((o) => {
    const matchesSearch = `${o.code} ${o.customer}`.toLowerCase().includes(searchQuery.toLowerCase());
    const matchesChannel = channelFilter === "all" || o.channel === channelFilter;
    return matchesSearch && matchesChannel;
  });

  const isReservedState = status === "Reserved" || status === "Pending";

  return (
    <>
      <PageTitle
        eyebrow="FR-ORD · FR-RSE"
        title="Đơn hàng đa kênh"
        description="Một luồng xử lý chung cho POS, Admin và sàn thương mại điện tử (Shopee, TikTok, Lazada) kèm cơ chế khóa giữ chỗ chống bán âm."
        actions={
          <>
            <Button variant="secondary" onClick={loadOrders} disabled={loading}>
              <RefreshCw size={17} />{loading ? "Đang đồng bộ..." : "Đồng bộ"}
            </Button>
            <Button onClick={openCreateModal}>
              <Plus size={17} />Tạo đơn & Giữ chỗ kho
            </Button>
          </>
        }
      />

      {/* [A09] Hiển thị rõ lỗi kết nối API */}
      {loadError && (
        <div
          style={{
            background: "#fef2f2",
            border: "1px solid #ef4444",
            color: "#b91c1c",
            padding: "14px 18px",
            borderRadius: "10px",
            marginBottom: "18px",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            gap: "12px",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
            <AlertCircle size={20} />
            <div>
              <strong>Không thể tải dữ liệu từ máy chủ:</strong>
              <div style={{ fontSize: "13px" }}>{loadError}</div>
            </div>
          </div>
          <Button variant="secondary" onClick={loadOrders}>
            <RefreshCw size={15} /> Tải lại trang
          </Button>
        </div>
      )}

      <div className="order-layout">
        <section className="card order-list-card">
          <div className="toolbar">
            <label className="search-field">
              <Search size={18} />
              <Input
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Tìm mã đơn, khách hàng..."
              />
            </label>
            <Select aria-label="Lọc kênh" value={channelFilter} onChange={(e) => setChannelFilter(e.target.value)}>
              <option value="all">Tất cả kênh</option>
              <option value="POS">POS</option>
              <option value="Shopee">Shopee</option>
              <option value="TikTok">TikTok</option>
              <option value="Lazada">Lazada</option>
            </Select>
            <Button variant="secondary" onClick={loadOrders}><Filter size={17} /></Button>
          </div>
          <div className="order-list">
            {filteredOrders.length === 0 ? (
              <div style={{ textAlign: "center", padding: "36px 12px", color: "#64748b" }}>
                Chưa có đơn hàng nào trong phiên của doanh nghiệp <strong>{account.tenantName}</strong>. Bấm{" "}
                <strong>"Tạo đơn & Giữ chỗ kho"</strong> để khởi tạo đơn và khóa tồn kho (
                <code>POST /api/inventory/reserve</code>).
              </div>
            ) : (
              filteredOrders.map((order) => (
                <button
                  key={order.id}
                  className={selected?.id === order.id ? "active" : ""}
                  onClick={() => selectOrder(order)}
                >
                  <div>
                    <span className={`channel channel--${order.channel.toLowerCase()}`}>{order.channel}</span>
                    <strong>{order.code}</strong>
                    <small>
                      {order.branch ? `${order.branch} · ` : ""}
                      {new Date(order.createdAt).toLocaleString("vi-VN", {
                        day: "2-digit",
                        month: "2-digit",
                        hour: "2-digit",
                        minute: "2-digit",
                        timeZone: "Asia/Ho_Chi_Minh",
                      })}
                    </small>
                  </div>
                  <div>
                    <strong>{order.amount.toLocaleString("vi-VN")} ₫</strong>
                    <OrderBadge status={selected?.id === order.id ? status : order.status} />
                  </div>
                  <ChevronRight size={18} />
                </button>
              ))
            )}
          </div>
        </section>

        {selected ? (
          <section className="card order-detail">
            <div className="card-head detail-head">
              <div>
                <div className="eyebrow">CHI TIẾT ĐƠN HÀNG</div>
                <h2>{selected.code}</h2>
                <p>
                  Kênh bán: {selected.channel} {selected.branch ? `· Kho giữ chỗ: ${selected.branch}` : ""}
                </p>
              </div>
              <OrderBadge status={status} />
            </div>

            {isReservedState && (
              <div className="reservation-banner">
                <span>
                  <strong>Đang giữ chỗ tồn kho (Anti-oversell · Advisory Lock)</strong>
                  <small>Hệ thống tự động giải phóng tồn kho nếu quá hạn thanh toán.</small>
                </span>
                <b>{selected.expiresIn || "15 phút"}</b>
              </div>
            )}

            {status === "Cancelled" && cancelReason && (
              <div className="expired-banner">
                <div>
                  <strong>Lý do hủy: {cancelReason}</strong>
                  <span>Hệ thống đã tự động hoàn trả số dư khả dụng (Available).</span>
                </div>
                {inventoryReloaded && <Badge tone="success">Đã hoàn tồn kho</Badge>}
              </div>
            )}

            <div className="detail-grid">
              <div><span>Khách hàng</span><strong>{selected.customer}</strong></div>
              <div><span>Kênh bán</span><strong>{selected.channel}</strong></div>
              <div className="full">
                <span>Chi nhánh / Kho giữ chỗ</span>
                <strong>{selected.branch || "Chi nhánh chính"}</strong>
              </div>
              <div className="full"><span>Trạng thái chứng từ</span><strong>{status}</strong></div>
            </div>

            {selected.items && selected.items.length > 0 && (
              <div className="table-wrap" style={{ marginTop: "12px" }}>
                <table>
                  <thead>
                    <tr>
                      <th>SKU / Sản phẩm</th>
                      <th>SL</th>
                      <th>Đơn giá</th>
                      <th>Giá vốn (WAC chi nhánh)</th>
                      <th>Thành tiền</th>
                    </tr>
                  </thead>
                  <tbody>
                    {selected.items.map((item, i) => (
                      <tr key={i}>
                        <td><strong>{item.skuCode}</strong> · {item.name}</td>
                        <td>{item.quantity}</td>
                        <td>{item.price.toLocaleString("vi-VN")} ₫</td>
                        <td>{item.costSnapshot.toLocaleString("vi-VN")} ₫</td>
                        <td><strong>{(item.price * item.quantity).toLocaleString("vi-VN")} ₫</strong></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <div className="order-total">
              <span className="grand">Tổng giá trị đơn<strong>{selected.amount.toLocaleString("vi-VN")} ₫</strong></span>
            </div>

            <div className="state-flow">
              <div className="done"><i />Nháp</div>
              <div className={["Pending", "Reserved", "Confirmed", "Completed"].includes(status) ? "done" : ""}><i />Giữ chỗ (Sprint 2)</div>
              <div className={["Confirmed", "Completed"].includes(status) ? "done" : ""}><i />Xác nhận (Sprint 3)</div>
              <div className={status === "Completed" ? "done" : ""}><i />Hoàn tất (Sprint 3)</div>
            </div>

            {/* [A11] Gắn nhãn [Sprint 3 Preview] rõ ràng cho các thao tác trạng thái đơn hàng thuộc phạm vi Sprint 3 */}
            <div className="detail-actions" style={{ flexWrap: "wrap", gap: "8px" }}>
              <Button variant="secondary" onClick={() => window.print()}>
                <Printer size={17} />In phiếu giao <Badge tone="neutral">[Sprint 3 Preview]</Badge>
              </Button>
              {isReservedState && (
                <>
                  <Button variant="danger" onClick={() => updateOrderStatus("Cancelled", "Khách yêu cầu hủy đơn")}>
                    Hủy giữ chỗ <Badge tone="neutral">[Sprint 3 Preview]</Badge>
                  </Button>
                  <Button onClick={() => updateOrderStatus("Confirmed")}>
                    Xác nhận đơn <Badge tone="neutral">[Sprint 3 Preview]</Badge>
                  </Button>
                </>
              )}
              {status === "Confirmed" && (
                <Button onClick={() => updateOrderStatus("Completed")}>
                  Hoàn tất đơn <Badge tone="neutral">[Sprint 3 Preview]</Badge>
                </Button>
              )}
              {status === "Completed" && <Badge tone="success">Quy trình đã hoàn tất [Sprint 3 Preview]</Badge>}
              {status === "Cancelled" && <Badge tone="danger">Đã hủy đơn hàng [Sprint 3 Preview]</Badge>}
            </div>
          </section>
        ) : (
          <section className="card order-detail" style={{ display: "grid", placeItems: "center", minHeight: "300px", color: "#64748b" }}>
            Chọn một đơn hàng từ danh sách bên trái hoặc bấm "Tạo đơn & Giữ chỗ kho" để khởi tạo đơn mới.
          </section>
        )}
      </div>

      {/* [A04, A08, A10 & F2] Modal Tạo đơn & Giữ chỗ tồn kho hiển thị rõ tồn khả dụng theo đúng Chi nhánh / Kho cụ thể */}
      {createModal && (
        <Modal title="Tạo đơn hàng đa kênh & Giữ chỗ tồn kho (Advisory Lock)" onClose={() => setCreateModal(false)}>
          {createError && (
            <div style={{ background: "#fee2e2", border: "1px solid #ef4444", color: "#b91c1c", padding: "10px", borderRadius: "8px", fontSize: "12px", marginBottom: "12px", display: "flex", alignItems: "center", gap: "8px" }}>
              <AlertCircle size={16} />
              <span>{createError}</span>
            </div>
          )}

          {orderIdempotencyKey && (
            <div
              style={{
                background: "#f8fafc",
                border: "1px solid #94a3b8",
                color: "#334155",
                padding: "8px 12px",
                borderRadius: "6px",
                fontSize: "12px",
                marginBottom: "12px",
              }}
            >
              <strong>Định danh giữ chỗ (F2 - Idempotency Key):</strong> <code>{orderIdempotencyKey}</code>
              {isRetryingOrder && (
                <div style={{ marginTop: "4px", color: "#b45309" }}>
                  Thao tác trước đó đang chờ kết quả hoặc gặp lỗi mạng. Khi bấm <strong>"Thử lại Tạo đơn & Giữ chỗ"</strong>, hệ thống sẽ gửi cùng định danh và payload để tránh tạo đơn lặp hoặc giữ chỗ hai lần.
                </div>
              )}
            </div>
          )}

          {whStockError && (
            <div
              style={{
                background: "#fef2f2",
                border: "1px solid #f87171",
                color: "#991b1b",
                padding: "8px 12px",
                borderRadius: "6px",
                fontSize: "12px",
                marginBottom: "12px",
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
              }}
            >
              <span>
                <strong>Lỗi nạp tồn kho kho xuất hàng:</strong> Không thể tải số dư của kho đang chọn ({whStockError}).
              </span>
              <button
                type="button"
                onClick={() => {
                  if (newBranchId && newWarehouseId) {
                    const seq = ++whStockReqSeqRef.current;
                    setLoadingWhStock(true);
                    setWhStockError(null);
                    setWarehouseStockList([]);
                    apiAdapter.getBalances({ branchId: newBranchId, warehouseId: newWarehouseId }).then((res) => {
                      if (seq !== whStockReqSeqRef.current) return;
                      setLoadingWhStock(false);
                      if (res.error) {
                        setWhStockError(res.error.message || "Không thể tải số dư kho");
                      } else {
                        setWarehouseStockList(res.data || []);
                      }
                    });
                  }
                }}
                style={{
                  background: "#dc2626",
                  color: "#fff",
                  border: "none",
                  padding: "4px 8px",
                  borderRadius: "4px",
                  cursor: "pointer",
                  fontSize: "11px",
                }}
              >
                Thử lại
              </button>
            </div>
          )}

          <div className="form-grid">
            <label>
              <span>Tên khách hàng *</span>
              <Input
                value={newCustomer}
                onChange={(e) => {
                  setNewCustomer(e.target.value);
                  handleFormChange();
                }}
                required
              />
            </label>
            <label>
              <span>Kênh bán hàng *</span>
              <Select
                value={newChannel}
                onChange={(e) => {
                  setNewChannel(e.target.value as Order["channel"]);
                  handleFormChange();
                }}
              >
                <option value="Shopee">Shopee</option>
                <option value="TikTok">TikTok Shop</option>
                <option value="Lazada">Lazada</option>
                <option value="POS">POS tại quầy</option>
              </Select>
            </label>
            {/* [A04] Chọn Chi nhánh lọc đúng Kho thuộc chi nhánh đó */}
            <label>
              <span>Chi nhánh xuất hàng *</span>
              <Select
                value={newBranchId}
                onChange={(e) => {
                  const nextBr = e.target.value;
                  setNewBranchId(nextBr);
                  const matchingWhs = warehouses.filter((w) => w.branchId === nextBr);
                  setNewWarehouseId(matchingWhs[0]?.id || "");
                  handleFormChange();
                }}
              >
                {branches.map((b) => (
                  <option key={b.id} value={b.id}>{b.name}</option>
                ))}
              </Select>
            </label>
            <label>
              <span>Kho giữ chỗ (Thuộc chi nhánh) *</span>
              <Select
                value={newWarehouseId}
                onChange={(e) => {
                  setNewWarehouseId(e.target.value);
                  handleFormChange();
                }}
                disabled={modalBranchWarehouses.length === 0}
              >
                {modalBranchWarehouses.length === 0 ? (
                  <option value="">-- Chưa có kho thuộc chi nhánh này --</option>
                ) : (
                  modalBranchWarehouses.map((w) => (
                    <option key={w.id} value={w.id}>{w.name}</option>
                  ))
                )}
              </Select>
            </label>
            <label>
              <span>Chọn SKU đặt hàng *</span>
              <Select
                value={newSkuId}
                onChange={(e) => {
                  setNewSkuId(e.target.value);
                  handleFormChange();
                }}
              >
                {skusList.map((s) => {
                  const whBal = warehouseStockList.find((wb) => wb.id === s.id);
                  const whAvail = whBal ? Math.max(0, whBal.onHand - whBal.reserved) : 0;
                  return (
                    <option key={s.id} value={s.id} disabled={s.isActive === false}>
                      {s.code} - {s.name} {s.isActive === false ? "[Ngừng hoạt động]" : `(Khả dụng tại kho chọn: ${whAvail} / Tổng hệ thống: ${s.onHand - s.reserved})`}
                    </option>
                  );
                })}
              </Select>
            </label>
            <label>
              <span>Số lượng đặt *</span>
              <Input
                type="number"
                min="1"
                value={newQuantity}
                onChange={(e) => {
                  setNewQuantity(Math.max(1, Number(e.target.value)));
                  handleFormChange();
                }}
              />
            </label>
          </div>

          {/* [A08] Hiển thị rõ số tồn khả dụng theo từng Chi nhánh / Kho cụ thể trước khi thực hiện giữ chỗ */}
          {(() => {
            const chosenSku = skusList.find((s) => s.id === newSkuId);
            const whBal = warehouseStockList.find((wb) => wb.id === newSkuId);
            const whOnHand = whBal?.onHand ?? 0;
            const whReserved = whBal?.reserved ?? 0;
            const whAvailable = Math.max(0, whOnHand - whReserved);
            const branchWac = whBal?.averageCost ?? chosenSku?.averageCost ?? 0;
            return (
              <div
                style={{
                  marginTop: "12px",
                  padding: "12px",
                  background: "#f8fafc",
                  border: "1px solid #cbd5e1",
                  borderRadius: "8px",
                  fontSize: "12px",
                }}
              >
                <strong style={{ display: "block", marginBottom: "6px", color: "#0f172a" }}>
                  Kiểm tra tồn kho tại Kho giữ chỗ đang chọn ({loadingWhStock ? "Đang tải..." : "Trực tiếp từ Backend"}):
                </strong>
                <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: "8px" }}>
                  <div>
                    <span style={{ color: "#64748b", display: "block" }}>Tồn thực tế (OnHand)</span>
                    <strong>{whOnHand}</strong>
                  </div>
                  <div>
                    <span style={{ color: "#64748b", display: "block" }}>Đang giữ chỗ (Reserved)</span>
                    <strong style={{ color: "#d97706" }}>{whReserved}</strong>
                  </div>
                  <div>
                    <span style={{ color: "#64748b", display: "block" }}>Khả dụng tại kho này</span>
                    <strong style={{ color: whAvailable >= newQuantity ? "#16a34a" : "#dc2626" }}>
                      {whAvailable}
                    </strong>
                  </div>
                  <div>
                    <span style={{ color: "#64748b", display: "block" }}>Giá vốn WAC CN</span>
                    <strong>{branchWac.toLocaleString("vi-VN")} ₫</strong>
                  </div>
                </div>
              </div>
            );
          })()}

          <div className="hint-box" style={{ marginTop: "12px" }}>
            Khi bấm tạo đơn, hệ thống gọi trực tiếp API <code>POST /api/inventory/reserve</code> tại đúng{" "}
            <strong>Chi nhánh & Kho</strong> đã chọn ở trên để khóa số dư khả dụng (<code>Reserved += SL</code>) trong 15 phút chống bán vượt tồn (Overselling).
          </div>
          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setCreateModal(false)}>Đóng</Button>
            {isRetryingOrder && (
              <Button
                variant="secondary"
                onClick={handleDiscardPendingOrder}
                disabled={loading || loadingWhStock}
                style={{ color: "#dc2626", borderColor: "#fca5a5" }}
              >
                Bỏ thao tác dở dang / Tạo mới
              </Button>
            )}
            <Button onClick={handleCreateOrder} disabled={loading || loadingWhStock || Boolean(whStockError)}>
              {loading
                ? "Đang giữ chỗ..."
                : isRetryingOrder
                ? "Thử lại Tạo đơn & Giữ chỗ (Cùng định danh)"
                : "Tạo đơn & Giữ chỗ kho"}
            </Button>
          </div>
        </Modal>
      )}
    </>
  );
}
