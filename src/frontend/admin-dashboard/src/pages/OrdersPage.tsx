import { ChevronRight, Filter, Plus, Printer, RefreshCw, Search, AlertCircle } from "lucide-react";
import { useEffect, useState } from "react";
import { Badge, Button, Input, Modal, PageTitle, Select } from "../components/ui";
import { apiAdapter } from "../services/apiAdapter";
import { realtimeAdapter, type RealtimeEvent } from "../services/realtimeAdapter";
import { OrderBadge } from "./DashboardPage";
import type { Order, Sku } from "../types/domain";

export function OrdersPage() {
  const [ordersList, setOrdersList] = useState<Order[]>([]);
  const [skusList, setSkusList] = useState<Sku[]>([]);
  const [loading, setLoading] = useState(false);
  const [selected, setSelected] = useState<Order | null>(null);
  const [status, setStatus] = useState<string>("Draft");
  const [cancelReason, setCancelReason] = useState("");
  const [inventoryReloaded, setInventoryReloaded] = useState(false);

  // Bộ lọc
  const [searchQuery, setSearchQuery] = useState("");
  const [channelFilter, setChannelFilter] = useState("all");

  // Modal tạo đơn & giữ chỗ tồn kho (POST /api/inventory/reserve)
  const [createModal, setCreateModal] = useState(false);
  const [newCustomer, setNewCustomer] = useState("Khách hàng Admin");
  const [newChannel, setNewChannel] = useState<Order["channel"]>("Shopee");
  const [newSkuId, setNewSkuId] = useState("");
  const [newQuantity, setNewQuantity] = useState(1);
  const [createError, setCreateError] = useState<string | null>(null);

  const loadOrders = async () => {
    setLoading(true);
    const [res, skuRes] = await Promise.all([
      apiAdapter.getOrders(),
      apiAdapter.getSkus(),
    ]);
    if (skuRes.data) {
      setSkusList(skuRes.data);
      if (skuRes.data[0]?.id && !newSkuId) setNewSkuId(skuRes.data[0].id);
    }
    if (res.data) {
      setOrdersList(res.data);
      if (res.data.length > 0 && !selected) {
        setSelected(res.data[0]);
        setStatus(res.data[0].status);
      }
    }
    setLoading(false);
  };

  useEffect(() => {
    loadOrders();
  }, []);

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

  const updateOrderStatus = (nextStatus: Order["status"], reason = "") => {
    setStatus(nextStatus);
    if (reason) setCancelReason(reason);
    if (!selected) return;
    const nextList = ordersList.map((o) => (o.id === selected.id ? { ...o, status: nextStatus } : o));
    setOrdersList(nextList);
    setSelected({ ...selected, status: nextStatus });
    sessionStorage.setItem("oism_session_orders", JSON.stringify(nextList));
  };

  const handleCreateOrder = async () => {
    setCreateError(null);
    const chosenSku = skusList.find((s) => s.id === newSkuId);
    if (!chosenSku) {
      setCreateError("Vui lòng chọn một sản phẩm / SKU hợp lệ.");
      return;
    }
    if (newQuantity <= 0) {
      setCreateError("Số lượng đặt hàng phải lớn hơn 0.");
      return;
    }

    setLoading(true);
    const res = await apiAdapter.checkout(chosenSku, newQuantity, newChannel, newCustomer.trim() || "Khách lẻ");
    setLoading(false);

    if (res.error) {
      setCreateError(res.error.message);
      return;
    }

    setCreateModal(false);
    await loadOrders();
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
            <Button onClick={() => { setCreateError(null); setCreateModal(true); }}>
              <Plus size={17} />Tạo đơn Admin
            </Button>
          </>
        }
      />
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
            <Button variant="secondary"><Filter size={17} /></Button>
          </div>
          <div className="order-list">
            {filteredOrders.length === 0 ? (
              <div style={{ textAlign: "center", padding: "36px 12px", color: "#64748b" }}>
                Chưa có đơn hàng nào khớp bộ lọc. Bấm <strong>"Tạo đơn Admin"</strong> để khởi tạo đơn và giữ chỗ tồn kho (`POST /api/inventory/reserve`).
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
                <p>Kênh bán: {selected.channel}</p>
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
                      <th>Giá vốn (WAC)</th>
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
              <div className={["Pending", "Reserved", "Confirmed", "Completed"].includes(status) ? "done" : ""}><i />Giữ chỗ</div>
              <div className={["Confirmed", "Completed"].includes(status) ? "done" : ""}><i />Xác nhận</div>
              <div className={status === "Completed" ? "done" : ""}><i />Hoàn tất</div>
            </div>

            <div className="detail-actions">
              <Button variant="secondary"><Printer size={17} />In phiếu giao</Button>
              {isReservedState && (
                <>
                  <Button variant="danger" onClick={() => updateOrderStatus("Cancelled", "Khách yêu cầu hủy đơn")}>
                    Hủy giữ chỗ
                  </Button>
                  <Button onClick={() => updateOrderStatus("Confirmed")}>Xác nhận đơn</Button>
                </>
              )}
              {status === "Confirmed" && (
                <Button onClick={() => updateOrderStatus("Completed")}>Hoàn tất đơn</Button>
              )}
              {status === "Completed" && <Badge tone="success">Quy trình đã hoàn tất</Badge>}
              {status === "Cancelled" && <Badge tone="danger">Đã hủy đơn hàng</Badge>}
            </div>
          </section>
        ) : (
          <section className="card order-detail" style={{ display: "grid", placeItems: "center", minHeight: "300px", color: "#64748b" }}>
            Chọn một đơn hàng từ danh sách bên trái hoặc bấm "Tạo đơn Admin" để khởi tạo đơn mới.
          </section>
        )}
      </div>

      {createModal && (
        <Modal title="Tạo đơn hàng đa kênh & Giữ chỗ tồn kho (Advisory Lock)" onClose={() => setCreateModal(false)}>
          {createError && (
            <div style={{ background: "#fee2e2", border: "1px solid #ef4444", color: "#b91c1c", padding: "10px", borderRadius: "8px", fontSize: "12px", marginBottom: "12px", display: "flex", alignItems: "center", gap: "8px" }}>
              <AlertCircle size={16} />
              <span>{createError}</span>
            </div>
          )}
          <div className="form-grid">
            <label>
              <span>Tên khách hàng *</span>
              <Input value={newCustomer} onChange={(e) => setNewCustomer(e.target.value)} required />
            </label>
            <label>
              <span>Kênh bán hàng *</span>
              <Select value={newChannel} onChange={(e) => setNewChannel(e.target.value as Order["channel"])}>
                <option value="Shopee">Shopee</option>
                <option value="TikTok">TikTok Shop</option>
                <option value="Lazada">Lazada</option>
                <option value="POS">POS tại quầy</option>
              </Select>
            </label>
            <label>
              <span>Chọn SKU đặt hàng *</span>
              <Select value={newSkuId} onChange={(e) => setNewSkuId(e.target.value)}>
                {skusList.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.code} - {s.name} (Khả dụng: {s.onHand - s.reserved})
                  </option>
                ))}
              </Select>
            </label>
            <label>
              <span>Số lượng đặt *</span>
              <Input
                type="number"
                min="1"
                value={newQuantity}
                onChange={(e) => setNewQuantity(Math.max(1, Number(e.target.value)))}
              />
            </label>
          </div>
          <div className="hint-box" style={{ marginTop: "12px" }}>
            Khi bấm tạo đơn, hệ thống gọi trực tiếp API <code>POST /api/inventory/reserve</code> để khóa số dư khả dụng (<code>Reserved += SL</code>) trong 15 phút chống bán vượt tồn (Overselling).
          </div>
          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setCreateModal(false)}>Hủy</Button>
            <Button onClick={handleCreateOrder} disabled={loading}>
              {loading ? "Đang giữ chỗ..." : "Tạo đơn & Giữ chỗ kho"}
            </Button>
          </div>
        </Modal>
      )}
    </>
  );
}
