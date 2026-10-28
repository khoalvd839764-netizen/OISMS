import { AlertCircle, Plus, RefreshCw, ShoppingBag } from "lucide-react";
import { useEffect, useState, useCallback, useRef } from "react";
import { useSearchParams } from "react-router-dom";
import { Button, Input, Modal, Select } from "../components/ui";
import { apiAdapter, generateUuid, getPendingOrderStorageKey } from "../services/apiAdapter";
import { useAuth } from "../context/AuthContext";
import type { Sku } from "../types/domain";
import type { OrderFilter, OrderSummary } from "../types/ordersCenter";
import { orderService } from "../services/orderService";
import { OrdersTable } from "../components/orders/OrdersTable";
import { OrdersFilter } from "../components/orders/OrdersFilter";
import { OrderDetailDrawer } from "../components/orders/OrderDetailDrawer";

export interface PendingOrderSnapshot {
  ownerTenantId: string;
  ownerUserId: string;
  idempotencyKey: string;
  requestPayload: {
    branchId: string;
    channel: string;
    customerName: string;
    requestKey: string;
    items: Array<{
      skuId: string;
      skuCode: string;
      productName: string;
      variantName?: string;
      quantity: number;
      unitPrice: number;
    }>;
  };
  warehouseId?: string;
  isUnknownState: boolean;
  lastError?: string;
  createdAt: number;
}

function loadSavedPendingOrder(tenantId?: string, userId?: string): PendingOrderSnapshot | null {
  try {
    const key = getPendingOrderStorageKey(tenantId, userId);
    const raw = sessionStorage.getItem(key);
    if (!raw) return null;
    const parsed: PendingOrderSnapshot = JSON.parse(raw);
    // B01-F02: Chặn replay chéo người dùng: tenantId VÀ userId phải khớp
    if (parsed.ownerTenantId !== tenantId || parsed.ownerUserId !== userId) {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

function savePendingOrder(data: PendingOrderSnapshot, tenantId?: string, userId?: string) {
  try {
    const key = getPendingOrderStorageKey(tenantId, userId);
    sessionStorage.setItem(key, JSON.stringify(data));
  } catch {}
}

function clearSavedPendingOrder(tenantId?: string, userId?: string) {
  try {
    const key = getPendingOrderStorageKey(tenantId, userId);
    sessionStorage.removeItem(key);
  } catch {}
}

export function OrdersPage() {
  const { account } = useAuth();
  const [searchParams] = useSearchParams();
  const urlOrderId = searchParams.get("orderId");
  const orderReqSeqRef = useRef(0);

  useEffect(() => {
    if (urlOrderId) {
      setSelectedOrderId(urlOrderId);
    }
  }, [urlOrderId]);

  // State Trung tâm Đơn hàng Đa kênh (Sprint 3: SCR-ORD-01..03)
  const [orders, setOrders] = useState<OrderSummary[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loadingOrders, setLoadingOrders] = useState(false);
  const [ordersError, setOrdersError] = useState<string | null>(null);
  const [selectedOrderId, setSelectedOrderId] = useState<string | null>(null);

  const [filter, setFilter] = useState<OrderFilter>({
    pageIndex: 1,
    pageSize: 10,
  });

  // Master data cho Modal Tạo đơn & Giữ chỗ (Sprint 2)
  const [skusList, setSkusList] = useState<Sku[]>([]);
  const [branches, setBranches] = useState<Array<{ id: string; name: string }>>([]);
  const [warehouses, setWarehouses] = useState<Array<{ id: string; name: string; branchId: string }>>([]);
  const [warehouseStockList, setWarehouseStockList] = useState<any[]>([]);
  const [loadingWhStock, setLoadingWhStock] = useState(false);
  const [whStockError, setWhStockError] = useState<string | null>(null);

  // Modal tạo đơn & giữ chỗ
  const [createModal, setCreateModal] = useState(false);
  const [newCustomer, setNewCustomer] = useState("Khách hàng Admin");
  const [newBranchId, setNewBranchId] = useState("");
  const [newWarehouseId, setNewWarehouseId] = useState("");
  const [newSkuId, setNewSkuId] = useState("");
  const [newQuantity, setNewQuantity] = useState(1);
  const [createError, setCreateError] = useState<string | null>(null);
  const [loadingCreate, setLoadingCreate] = useState(false);

  const isCreatingOrderRef = useRef(false);
  const whStockReqSeqRef = useRef(0);

  // Idempotency-Key & Snapshot bảo toàn (B01-F02)
  const [orderIdempotencyKey, setOrderIdempotencyKey] = useState<string | null>(null);
  const [pendingSnapshot, setPendingSnapshot] = useState<PendingOrderSnapshot | null>(null);
  const [isRetryingOrder, setIsRetryingOrder] = useState(false);
  const [isUnknownState, setIsUnknownState] = useState(false);

  // Nạp danh sách đơn hàng từ backend (/api/orders) - Chống out-of-order responses (A02-AC-01)
  const loadOrders = useCallback(async () => {
    const seq = ++orderReqSeqRef.current;
    setLoadingOrders(true);
    setOrdersError(null);
    try {
      const paged = await orderService.getOrders(filter);
      if (seq !== orderReqSeqRef.current) return;
      setOrders(paged.items || []);
      setTotalCount(paged.totalCount || 0);
    } catch (err: any) {
      if (seq !== orderReqSeqRef.current) return;
      console.warn("Lỗi nạp danh sách đơn hàng:", err);
      setOrdersError(err?.response?.data?.detail || err?.message || "Không thể tải danh sách đơn hàng.");
    } finally {
      if (seq === orderReqSeqRef.current) {
        setLoadingOrders(false);
      }
    }
  }, [filter]);

  useEffect(() => {
    loadOrders();
  }, [loadOrders]);

  // Lắng nghe sự kiện SignalR OrderCreated để tự động nạp lại bảng
  useEffect(() => {
    const handleOrderCreated = () => {
      loadOrders();
    };
    window.addEventListener("oism:order-created", handleOrderCreated);
    return () => {
      window.removeEventListener("oism:order-created", handleOrderCreated);
    };
  }, [loadOrders]);

  // Nạp Master data cho Modal Tạo đơn
  useEffect(() => {
    async function loadMasterData() {
      try {
        const [sRes, brRes, whRes] = await Promise.all([
          apiAdapter.getSkus(),
          apiAdapter.getBranches(),
          apiAdapter.getWarehouses(),
        ]);
        if (sRes.data) {
          setSkusList(sRes.data);
          if (sRes.data.length > 0) setNewSkuId(sRes.data[0].id);
        }
        if (brRes.data) {
          setBranches(brRes.data);
          if (brRes.data.length > 0) setNewBranchId(brRes.data[0].id);
        }
        if (whRes.data) {
          setWarehouses(whRes.data);
          const firstMatch = whRes.data.find(
            (w: any) => !brRes.data[0]?.id || w.branchId === brRes.data[0]?.id
          ) || whRes.data[0];
          if (firstMatch) setNewWarehouseId(firstMatch.id);
        }
      } catch (err) {
        console.warn("Lỗi nạp master data:", err);
      }
    }
    loadMasterData();
  }, []);

  // Tải tồn kho chi nhánh được chọn trong Modal
  useEffect(() => {
    async function loadSelectedWarehouseStock() {
      if (!createModal || !newBranchId || !newWarehouseId) {
        setWarehouseStockList([]);
        return;
      }
      const seq = ++whStockReqSeqRef.current;
      setWarehouseStockList([]);
      setWhStockError(null);
      setLoadingWhStock(true);
      const res = await apiAdapter.getBalances({
        branchId: newBranchId,
        warehouseId: newWarehouseId,
              });
      if (seq !== whStockReqSeqRef.current) return;
      setLoadingWhStock(false);
      if (res.data) setWarehouseStockList(res.data);
      if (res.error) setWhStockError(res.error.message);
    }
    loadSelectedWarehouseStock();
  }, [createModal, newBranchId, newWarehouseId, account.tenantId]);

  // Mở Modal tạo đơn
  const openCreateModal = () => {
    setCreateError(null);
    const pending = loadSavedPendingOrder(account.tenantId, account.id);
    if (pending) {
      setPendingSnapshot(pending);
      setOrderIdempotencyKey(pending.idempotencyKey);
      setNewBranchId(pending.requestPayload.branchId);
      if (pending.warehouseId) setNewWarehouseId(pending.warehouseId);
      const firstItem = pending.requestPayload.items[0];
      if (firstItem) {
        setNewSkuId(firstItem.skuId);
        setNewQuantity(firstItem.quantity);
      }
      setNewCustomer(pending.requestPayload.customerName);
      setIsRetryingOrder(true);
      // B01-F02: Mọi pending chưa giải phóng dứt khoát khi nạp lại đều ở trạng thái unknown
      setIsUnknownState(true);
      if (pending.lastError) {
        setCreateError(pending.lastError);
      } else {
        setCreateError("Đang có một yêu cầu tạo đơn chưa rõ kết quả từ phiên trước. Vui lòng thử lại để kiểm tra với máy chủ, hoặc hủy bỏ để tạo yêu cầu mới.");
      }
    } else {
      setPendingSnapshot(null);
      setOrderIdempotencyKey(generateUuid());
      setIsRetryingOrder(false);
      setIsUnknownState(false);
    }
    setCreateModal(true);
  };

  const handleDiscardPendingOrder = () => {
    if (isRetryingOrder || isUnknownState || pendingSnapshot) {
      const confirmDiscard = window.confirm(
        "Bạn có chắc muốn hủy bỏ yêu cầu tạo đơn đang chờ? Nếu máy chủ đã xử lý thành công yêu cầu trước đó, việc tạo mới có thể sinh thêm đơn khác."
      );
      if (!confirmDiscard) return;
    }
    clearSavedPendingOrder(account.tenantId, account.id);
    setPendingSnapshot(null);
    setOrderIdempotencyKey(generateUuid());
    setIsRetryingOrder(false);
    setIsUnknownState(false);
    setCreateError(null);
  };

  const handleFormChange = () => {
    // B01-F02: Tuyệt đối không cho phép gõ phím làm mất pending hoặc sinh key mới khi đang có pending snapshot
    if (isRetryingOrder || isUnknownState || pendingSnapshot) {
      return;
    }
  };

  // Tạo đơn hàng & giữ chỗ
  const createOrder = async () => {
    // 1. Kiểm tra khóa chống double-submit
    if (isCreatingOrderRef.current) return;

    // 2. B01-F03: Validation thực hiện TRƯỚC KHI lấy khóa submit, giải phóng ngay lập tức nếu lỗi
    setCreateError(null);

    let payloadToSend: any;
    let keyToSend: string;

    if ((isRetryingOrder || pendingSnapshot) && pendingSnapshot) {
      // B01-F02: Retry giữ nguyên snapshot payload & requestKey, KHÔNG dựng lại từ catalog
      keyToSend = pendingSnapshot.idempotencyKey;
      payloadToSend = pendingSnapshot.requestPayload;
    } else {
      const chosenSku = skusList.find((s) => s.id === newSkuId);
      if (!chosenSku) {
        setCreateError("Vui lòng chọn một sản phẩm / SKU hợp lệ.");
        return;
      }
      if (chosenSku.isActive === false) {
        setCreateError(`Mã SKU ${chosenSku.code} đang ở trạng thái [Ngừng hoạt động], không thể tạo đơn bán.`);
        return;
      }
      if (!newBranchId) {
        setCreateError("Vui lòng chọn Chi nhánh xuất hàng để giữ chỗ tồn kho.");
        return;
      }
      if (newQuantity <= 0) {
        setCreateError("Số lượng đặt hàng phải lớn hơn 0.");
        return;
      }

      keyToSend = orderIdempotencyKey || generateUuid();
      setOrderIdempotencyKey(keyToSend);

      payloadToSend = {
        branchId: newBranchId,
        channel: "Admin", // B01-F01: Khóa chặt contract channel Admin cho đơn tạo thủ công
        customerName: newCustomer.trim() || "Khách lẻ",
        requestKey: keyToSend,
        items: [
          {
            skuId: chosenSku.id,
            skuCode: chosenSku.code,
            productName: chosenSku.name,
            variantName: chosenSku.variant,
            quantity: newQuantity,
            unitPrice: chosenSku.sellingPrice || 0, // Lưu snapshot giá tại thời điểm tạo
          },
        ],
      };
    }

    // 3. Lấy khóa submit và chuyển trạng thái loading
    isCreatingOrderRef.current = true;
    setLoadingCreate(true);

    // B01-F02: Lưu snapshot với isUnknownState = true NGAY TRƯỚC KHI GỬI POST.
    // Nếu trang bị reload hoặc ngắt kết nối giữa chừng, snapshot được bảo toàn ở trạng thái unknown
    const snapshotToSave: PendingOrderSnapshot = {
      ownerTenantId: account.tenantId,
      ownerUserId: account.id,
      idempotencyKey: keyToSend,
      requestPayload: payloadToSend,
      warehouseId: newWarehouseId,
      isUnknownState: true, // In-flight = unknown until definite server response
      createdAt: Date.now(),
    };
    savePendingOrder(snapshotToSave, account.tenantId, account.id);
    setPendingSnapshot(snapshotToSave);
    setIsUnknownState(true);
    setIsRetryingOrder(true);

    try {
      await orderService.createOrder(payloadToSend);

      // Thành công dứt khoát: giải phóng pending
      clearSavedPendingOrder(account.tenantId, account.id);
      setPendingSnapshot(null);
      setOrderIdempotencyKey(null);
      setIsRetryingOrder(false);
      setIsUnknownState(false);
      setCreateModal(false);
      setNewQuantity(1);
      await loadOrders();
    } catch (err: any) {
      const status = err?.response?.status;
      const data = err?.response?.data;
      const errorDetail = data?.detail || data?.message || err?.message || "Lỗi xử lý tạo đơn hàng.";
      const errorCode = data?.errorCode || data?.extensions?.errorCode;

      // Phân định Definite Failure vs Indeterminate / Unknown State (B01-F02)
      if (status === 400) {
        // Lỗi xác thực dứt khoát: cho phép sửa lại form
        const msg = `Lỗi xác thực dữ liệu (400): ${errorDetail}`;
        setCreateError(msg);
        clearSavedPendingOrder(account.tenantId, account.id);
        setPendingSnapshot(null);
        setIsRetryingOrder(false);
        setIsUnknownState(false);
      } else if (status === 401 || status === 403) {
        // Lỗi quyền truy cập dứt khoát
        const msg = `Bạn không có quyền thực hiện thao tác tạo đơn (${status}).`;
        setCreateError(msg);
        clearSavedPendingOrder(account.tenantId, account.id);
        setPendingSnapshot(null);
        setIsRetryingOrder(false);
        setIsUnknownState(false);
      } else if (status === 409) {
        // Phân biệt thiếu tồn kho vs xung đột Idempotency (B01-F02)
        if (errorCode === "INSUFFICIENT_STOCK" || errorDetail.toLowerCase().includes("tồn kho") || errorDetail.toLowerCase().includes("không đủ")) {
          const msg = `Tồn kho không đủ để giữ chỗ: ${errorDetail}`;
          setCreateError(msg);
          clearSavedPendingOrder(account.tenantId, account.id);
          setPendingSnapshot(null);
          setIsRetryingOrder(false);
          setIsUnknownState(false);
        } else if (errorCode === "IDEMPOTENCY_CONFLICT" || errorCode === "IDEMPOTENCY_PAYLOAD_MISMATCH") {
          const msg = `Xung đột định danh (409 ${errorCode}): Dữ liệu yêu cầu không khớp với yêu cầu trước đó. Vui lòng bấm 'Hủy và tạo yêu cầu mới'.`;
          setCreateError(msg);
          setIsRetryingOrder(true);
          setIsUnknownState(true);
        } else {
          const msg = `Xung đột trạng thái (409): ${errorDetail}`;
          setCreateError(msg);
          setIsRetryingOrder(true);
          setIsUnknownState(true);
        }
      } else {
        // Timeout mạng, rớt kết nối, lỗi 5xx -> Trạng thái Unknown (B01-F02)
        const msg = `Mất kết nối hoặc phản hồi chưa rõ từ máy chủ (${status || "Mạng"}). Đang bảo lưu nguyên vẹn yêu cầu để thử lại an toàn: ${errorDetail}`;
        setCreateError(`${msg} (Định danh: ${keyToSend.slice(0, 8)}...)`);
        setIsRetryingOrder(true);
        setIsUnknownState(true);

        const updatedSnapshot: PendingOrderSnapshot = {
          ...snapshotToSave,
          isUnknownState: true,
          lastError: msg,
        };
        savePendingOrder(updatedSnapshot, account.tenantId, account.id);
        setPendingSnapshot(updatedSnapshot);
      }
    } finally {
      // B01-F03: Luôn giải phóng khóa trên mọi đường thoát
      isCreatingOrderRef.current = false;
      setLoadingCreate(false);
    }
  };

  return (
    <div className="p-6 max-w-7xl mx-auto space-y-4">
      {/* Header */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-3">
        <div>
          <div className="flex items-center gap-2">
            <ShoppingBag className="w-7 h-7 text-indigo-600" />
            <h1 className="text-2xl font-bold text-gray-900">Trung Tâm Đơn Hàng Đa Kênh</h1>
          </div>
          <p className="text-xs text-gray-500 mt-1">
            Quản trị vòng đời đơn hàng: Shopee, TikTok, Lazada, POS, Web. Duyệt đơn (trừ on-hand), Hủy đơn (hoàn giữ chỗ), Xử lý và In phiếu.
          </p>
        </div>

        <div className="flex gap-2">
          <button
            onClick={loadOrders}
            className="px-3 py-2 text-xs font-semibold text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 shadow-sm flex items-center gap-1.5"
          >
            <RefreshCw className="w-3.5 h-3.5" />
            Đồng bộ
          </button>
          <button
            onClick={openCreateModal}
            className="px-4 py-2 text-xs font-semibold text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg shadow-sm flex items-center gap-1.5"
          >
            <Plus className="w-4 h-4" />
            Tạo đơn & Giữ chỗ kho
          </button>
        </div>
      </div>

      {ordersError && (
        <div className="p-3.5 bg-rose-50 border border-rose-200 rounded-lg text-rose-700 text-xs flex items-center gap-2">
          <AlertCircle className="w-4 h-4 flex-shrink-0" />
          <span>{ordersError}</span>
        </div>
      )}

      {/* Bộ lọc đa tiêu chí */}
      <OrdersFilter
        filter={filter}
        onChange={(newFilter) => setFilter(newFilter)}
        onReset={() => setFilter({ pageIndex: 1, pageSize: 10 })}
      />

      {/* Bảng danh sách đơn hàng có phân trang server-side */}
      <OrdersTable
        orders={orders}
        loading={loadingOrders}
        totalCount={totalCount}
        pageIndex={filter.pageIndex}
        pageSize={filter.pageSize}
        onPageChange={(nextPage) => setFilter({ ...filter, pageIndex: nextPage })}
        onSelectOrder={(id) => setSelectedOrderId(id)}
      />

      {/* Drawer Chi tiết đơn hàng & Thao tác nghiệp vụ */}
      <OrderDetailDrawer
        orderId={selectedOrderId}
        onClose={() => setSelectedOrderId(null)}
        onRefresh={loadOrders}
      />

      {/* Modal Tạo đơn & Giữ chỗ tồn kho (Sprint 2) */}
      {createModal && (
        <Modal title="Tạo đơn hàng đa kênh & Giữ chỗ tồn kho (Advisory Lock)" onClose={() => setCreateModal(false)}>
          {createError && (
            <div
              style={{
                background: "#fef2f2",
                border: "1px solid #ef4444",
                color: "#b91c1c",
                padding: "10px 14px",
                borderRadius: "8px",
                marginBottom: "14px",
                fontSize: "12px",
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
              }}
            >
              <span>{createError}</span>
              {/* B01-F03: Retry luôn gọi createOrder(), tuyệt đối không quay lại apiAdapter.checkout cũ */}
              <button
                type="button"
                onClick={() => {
                  setIsRetryingOrder(true);
                  createOrder();
                }}
                style={{
                  background: "#dc2626",
                  color: "#fff",
                  border: "none",
                  padding: "4px 8px",
                  borderRadius: "4px",
                  cursor: "pointer",
                  fontSize: "11px",
                  fontWeight: 600,
                }}
              >
                Thử lại
              </button>
            </div>
          )}

          {/* B01-F02: Tính toán isFormLocked bao quát toàn bộ trường hợp pending chưa hoàn tất */}
          {(() => {
            const isFormLocked = isRetryingOrder || isUnknownState || !!pendingSnapshot;
            return (
              <>
                {isFormLocked && (
                  <div className="mb-3 p-3 bg-amber-50 border border-amber-300 rounded-lg text-xs text-amber-800">
                    ⚠️ <b>Yêu cầu đang chờ xác nhận:</b> Hệ thống đang bảo lưu chính xác dữ liệu giao dịch (SKU, giá, số lượng, chi nhánh, định danh). Biểu mẫu đang được khóa để bảo đảm nhất quán với dữ liệu gửi tới máy chủ. Hãy bấm <b>Thử lại giữ chỗ</b> để đối soát lại với máy chủ, hoặc bấm <b>Hủy và tạo yêu cầu mới</b> nếu muốn thay đổi thông tin.
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
                      disabled={isFormLocked}
                      required
                    />
                  </label>
                  <label>
                    <span>Kênh bán hàng *</span>
                    {/* B01-F01: Đơn tạo thủ công qua Admin bắt buộc channel Admin, hiển thị rõ ràng */}
                    <Input
                      value="Admin (Đơn tạo thủ công)"
                      disabled
                      readOnly
                    />
                  </label>
                  <label>
                    <span>Chi nhánh xuất hàng *</span>
                    <Select
                      value={newBranchId}
                      disabled={isFormLocked}
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
                    <span>Kho xuất hàng (Tra cứu khả dụng)</span>
                    <Select
                      value={newWarehouseId}
                      disabled={isFormLocked}
                      onChange={(e) => {
                        setNewWarehouseId(e.target.value);
                        handleFormChange();
                      }}
                    >
                      {warehouses
                        .filter((w) => !newBranchId || w.branchId === newBranchId)
                        .map((w) => (
                          <option key={w.id} value={w.id}>{w.name}</option>
                        ))}
                    </Select>
                  </label>
                  <label style={{ gridColumn: "1 / -1" }}>
                    <span>Sản phẩm / Biến thể (SKU) *</span>
                    <Select
                      value={newSkuId}
                      disabled={isFormLocked}
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
                            {s.code} · {s.name} {s.isActive === false ? "[Ngừng hoạt động]" : `(Khả dụng tại kho chọn: ${whAvail} / Tổng hệ thống: ${s.onHand - s.reserved})`}
                          </option>
                        );
                      })}
                    </Select>
                  </label>
                  <label>
                    <span>Số lượng bán *</span>
                    <Input
                      type="number"
                      min="1"
                      value={newQuantity}
                      disabled={isFormLocked}
                      onChange={(e) => {
                        setNewQuantity(Math.max(1, Number(e.target.value) || 1));
                        handleFormChange();
                      }}
                      required
                    />
                  </label>
                </div>
              </>
            );
          })()}

          {/* Hiển thị tồn khả dụng */}
          {(() => {
            const chosenSku = skusList.find((s) => s.id === newSkuId);
            const whBal = warehouseStockList.find((wb) => wb.id === newSkuId);
            const whOnHand = whBal ? whBal.onHand : 0;
            const whReserved = whBal ? whBal.reserved : 0;
            const whAvail = Math.max(0, whOnHand - whReserved);
            const sysAvail = chosenSku ? chosenSku.onHand - chosenSku.reserved : 0;
            const isNotEnough = chosenSku && newQuantity > whAvail;

            return (
              <div
                style={{
                  background: isNotEnough ? "#fff1f2" : "#f8fafc",
                  border: `1px solid ${isNotEnough ? "#f43f5e" : "#e2e8f0"}`,
                  padding: "12px 14px",
                  borderRadius: "8px",
                  marginTop: "12px",
                  fontSize: "12px",
                  color: isNotEnough ? "#9f1239" : "#334155",
                }}
              >
                <div style={{ fontWeight: 600, marginBottom: "4px" }}>
                  Kiểm tra tồn kho trước khi Giữ chỗ:
                </div>
                {loadingWhStock ? (
                  <div style={{ color: "#64748b" }}>Đang tải số dư tồn kho thực tế...</div>
                ) : whStockError ? (
                  <div style={{ color: "#b91c1c" }}>{whStockError}</div>
                ) : (
                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                    <div>
                      • Khả dụng tại kho đang chọn: <strong>{whAvail}</strong> (OnHand: {whOnHand}, Reserved: {whReserved})
                    </div>
                    <div>
                      • Khả dụng chi nhánh (phạm vi giữ chỗ thực tế): <strong>{sysAvail}</strong>
                    </div>
                  </div>
                )}
                {isNotEnough && (
                  <div style={{ color: "#e11d48", fontWeight: 600, marginTop: "6px" }}>
                    ⚠️ Cảnh báo: Số lượng bán ({newQuantity}) vượt quá Tồn khả dụng ({whAvail}) tại kho này.
                  </div>
                )}
              </div>
            );
          })()}

          <div
            style={{
              marginTop: "10px",
              padding: "8px 12px",
              borderRadius: "6px",
              background: isRetryingOrder ? "#fef3c7" : "#f1f5f9",
              fontSize: "11px",
              color: isRetryingOrder ? "#92400e" : "#475569",
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <span>
              {isRetryingOrder
                ? `Đang giữ nguyên định danh để thử lại an toàn: ${orderIdempotencyKey?.slice(0, 8)}...`
                : `Định danh thao tác mới: ${orderIdempotencyKey?.slice(0, 8)}...`}
            </span>
            {isRetryingOrder && (
              <button
                type="button"
                onClick={handleDiscardPendingOrder}
                style={{
                  border: "none",
                  background: "transparent",
                  color: "#b45309",
                  fontWeight: 600,
                  cursor: "pointer",
                  textDecoration: "underline",
                  fontSize: "11px",
                }}
              >
                Hủy và tạo yêu cầu mới
              </button>
            )}
          </div>

          <div className="modal-actions" style={{ marginTop: "16px", display: "flex", justifyContent: "flex-end", gap: "8px" }}>
            <Button variant="secondary" onClick={() => setCreateModal(false)}>Hủy</Button>
            <Button variant="primary" onClick={createOrder} disabled={loadingCreate}>
              {loadingCreate ? "Đang khóa tồn kho..." : isRetryingOrder ? "Thử lại giữ chỗ" : "Tạo đơn & Giữ chỗ ngay"}
            </Button>
          </div>
        </Modal>
      )}
    </div>
  );
}

export default OrdersPage;
