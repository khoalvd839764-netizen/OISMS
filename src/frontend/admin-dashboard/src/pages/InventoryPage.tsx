import {
  AlertCircle,
  ArrowDownToLine,
  ArrowRightLeft,
  BookOpen,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  ClipboardCheck,
  ClipboardList,
  Lock,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
  Settings2,
  Trash2,
  TriangleAlert,
} from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Badge, Button, ComingSoonBadge, Input, Modal, PageTitle, Select } from "../components/ui";
import { apiAdapter, generateUuid } from "../services/apiAdapter";
import { realtimeAdapter } from "../services/realtimeAdapter";
import { useAuth } from "../context/AuthContext";
import type { LedgerEntry, Sku } from "../types/domain";
import { GoodsReceiptList } from "../components/GoodsReceiptList";
import { purchaseOrderService } from "../services/purchaseOrderService";
import type { PurchaseOrderResponse } from "../types/purchaseOrder";

// [F1] Cấu trúc lưu tạm phiếu nhập nháp đang chờ xác nhận qua các lần đóng/mở form hoặc refresh
interface PendingReceiptState {
  receiptId: string;
  purchaseOrderId?: string;
  purchaseOrderNumber?: string;
  branchId: string;
  warehouseId: string;
  supplierId: string;
  notes: string;
  items: Array<{
    purchaseOrderItemId?: string;
    skuId: string;
    skuCode: string;
    skuName: string;
    unitName?: string;
    factorSnapshot?: number;
    orderedQuantity?: number;
    remainingQuantity?: number;
    quantity: number;
    unitPrice: number;
  }>;
}

function loadSavedPendingReceipt(tenantId?: string): PendingReceiptState | null {
  try {
    const key = apiAdapter.getPendingReceiptStorageKey(tenantId);
    const raw = sessionStorage.getItem(key);
    if (!raw) return null;
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

function savePendingReceipt(data: PendingReceiptState, tenantId?: string) {
  try {
    const key = apiAdapter.getPendingReceiptStorageKey(tenantId);
    sessionStorage.setItem(key, JSON.stringify(data));
  } catch {}
}

function clearSavedPendingReceipt(tenantId?: string) {
  try {
    const key = apiAdapter.getPendingReceiptStorageKey(tenantId);
    sessionStorage.removeItem(key);
  } catch {}
}

// [F2] Cấu trúc lưu tạm thao tác giữ chỗ đang chờ xử lý qua các lần đóng/mở form hoặc reload
interface PendingReservationState {
  idempotencyKey: string;
  branchId: string;
  warehouseId: string;
  skuId: string;
  quantity: number;
}

function loadSavedPendingReservation(tenantId?: string): PendingReservationState | null {
  try {
    const key = apiAdapter.getPendingReservationStorageKey(tenantId);
    const raw = sessionStorage.getItem(key);
    if (!raw) return null;
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

function savePendingReservation(data: PendingReservationState, tenantId?: string) {
  try {
    const key = apiAdapter.getPendingReservationStorageKey(tenantId);
    sessionStorage.setItem(key, JSON.stringify(data));
  } catch {}
}

function clearSavedPendingReservation(tenantId?: string) {
  try {
    const key = apiAdapter.getPendingReservationStorageKey(tenantId);
    sessionStorage.removeItem(key);
  } catch {}
}

export function InventoryPage() {
  const { account } = useAuth();
  const currentTenantId = account?.tenantId;

  const [tab, setTab] = useState<"balance" | "ledger">("balance");
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  // [F4] Tách biệt trạng thái tải lỗi sổ kho và trạng thái dữ liệu rỗng
  const [ledgerError, setLedgerError] = useState<string | null>(null);
  const [loadingLedger, setLoadingLedger] = useState(false);

  // Dữ liệu Tồn kho & Sổ kho
  const [skus, setSkus] = useState<Sku[]>([]);
  const [warehouseBalances, setWarehouseBalances] = useState<Sku[]>([]);
  const [availableSkus, setAvailableSkus] = useState<Sku[]>([]);
  const [ledger, setLedger] = useState<LedgerEntry[]>([]);
  const [suppliers, setSuppliers] = useState<Array<{ id: string; name: string }>>([]);
  const [branches, setBranches] = useState<Array<{ id: string; name: string }>>([]);
  const [warehouses, setWarehouses] = useState<Array<{ id: string; name: string; branchId: string }>>([]);

  // Modals
  const [receiptModal, setReceiptModal] = useState(false);
  const [receiptListOpen, setReceiptListOpen] = useState(false);
  const [receiptReady, setReceiptReady] = useState(false);
  const [receiptNumber, setReceiptNumber] = useState("");
  const receiptReadSeq = useRef(0);
  const receiptActor = `${account?.tenantId}:${account?.id}`;
  const receiptActorRef = useRef(receiptActor);
  receiptActorRef.current = receiptActor;
  const [confirmedSuccess, setConfirmedSuccess] = useState(false);
  const [selectedLedger, setSelectedLedger] = useState<LedgerEntry | null>(null);

  // [A08 & F2] Modal Giữ chỗ tồn kho (Reserve) hỗ trợ Idempotency-Key chống giữ lặp
  const [reserveModalOpen, setReserveModalOpen] = useState(false);
  const [reserveBranchId, setReserveBranchId] = useState("");
  const [reserveWarehouseId, setReserveWarehouseId] = useState("");
  const [reserveSkuId, setReserveSkuId] = useState("");
  const [reserveQty, setReserveQty] = useState(1);
  const [reserveError, setReserveError] = useState<string | null>(null);
  const [reserveSuccess, setReserveSuccess] = useState<string | null>(null);
  const [reserveWhBalances, setReserveWhBalances] = useState<Sku[]>([]);
  const [loadingReserveWh, setLoadingReserveWh] = useState(false);
  const [reserveWhError, setReserveWhError] = useState<string | null>(null);
  const [reserveIdempotencyKey, setReserveIdempotencyKey] = useState<string | null>(null);
  const [isRetryingReserve, setIsRetryingReserve] = useState(false);
  const reserveWhReqSeqRef = useRef(0);
  const isReservingRef = useRef(false);

  // [F3] Trạng thái lỗi tải số dư của riêng kho đang chọn trên Toolbar
  const [warehouseBalanceError, setWarehouseBalanceError] = useState<string | null>(null);

  // [F3] Modal Nhập kho: thứ tự request và lỗi tải số dư kho phục vụ WAC
  const modalWhReqSeqRef = useRef(0);
  const [loadingModalWhStock, setLoadingModalWhStock] = useState(false);
  const [modalWhError, setModalWhError] = useState<string | null>(null);
  const isConfirmingReceiptRef = useRef(false);

  // Bộ lọc Bảng Tồn kho & Sổ kho
  const [searchQuery, setSearchQuery] = useState("");
  const [branchFilter, setBranchFilter] = useState("all");
  const [balanceWarehouseFilter, setBalanceWarehouseFilter] = useState("all");
  const [stockStatusFilter, setStockStatusFilter] = useState("all");
  const [warehouseFilter, setWarehouseFilter] = useState("all");
  const [operationFilter, setOperationFilter] = useState("all");
  const [directionFilter, setDirectionFilter] = useState("all");

  // [F3] Biến ref kiểm soát thứ tự request tồn kho để tránh phản hồi cũ ghi đè dữ liệu mới
  const balanceReqSeqRef = useRef(0);
  const ledgerReqSeqRef = useRef(0);

  // [A05] Phân trang (Pagination) cho Bảng Tồn kho & Sổ kho
  const [balancePage, setBalancePage] = useState(1);
  const [balancePageSize, setBalancePageSize] = useState(20);
  const [ledgerPage, setLedgerPage] = useState(1);
  const [ledgerPageSize, setLedgerPageSize] = useState(20);

  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();

  // Form Phiếu nhập kho nhiều dòng
  const [selectedSupplier, setSelectedSupplier] = useState("");
  const [selectedBranch, setSelectedBranch] = useState("");
  const [selectedWarehouse, setSelectedWarehouse] = useState("");
  const [receiptNotes, setReceiptNotes] = useState("");
  const [receiptItems, setReceiptItems] = useState<
    Array<{
      purchaseOrderItemId?: string;
      skuId: string;
      skuCode: string;
      skuName: string;
      unitName?: string;
      factorSnapshot?: number;
      orderedQuantity?: number;
      remainingQuantity?: number;
      quantity: number;
      unitPrice: number;
    }>
  >([]);
  const [receiptError, setReceiptError] = useState<string | null>(null);
  // [A07 & F1] Lưu ID phiếu nhập nháp để retry an toàn, đối soát trạng thái với backend
  const [createdReceiptId, setCreatedReceiptId] = useState<string | null>(null);
  const [verifyingPendingReceipt, setVerifyingPendingReceipt] = useState(false);
  const [modalWarehouseBalances, setModalWarehouseBalances] = useState<Sku[]>([]);

  // [U01-A] Quản lý liên kết Đơn mua hàng đã duyệt
  const [receivablePos, setReceivablePos] = useState<PurchaseOrderResponse[]>([]);
  const [selectedPurchaseOrderId, setSelectedPurchaseOrderId] = useState<string>("");
  const [selectedPoDetail, setSelectedPoDetail] = useState<PurchaseOrderResponse | null>(null);
  const [, setLoadingPos] = useState(false);

  // Cấu hình ngưỡng cảnh báo
  const [thresholds] = useState<Record<string, number>>({});

  // Modal Cấu hình Ngưỡng cảnh báo ReorderPoint (CS-03)
  const [reorderModalSku, setReorderModalSku] = useState<Sku | null>(null);
  const [reorderInputVal, setReorderInputVal] = useState<number>(5);
  const [reorderBranchId, setReorderBranchId] = useState<string>("");
  const [savingReorder, setSavingReorder] = useState(false);
  const [reorderError, setReorderError] = useState<string | null>(null);

  const openReorderModal = (sku: Sku) => {
    setReorderModalSku(sku);
    setReorderInputVal(thresholds[sku.id] ?? sku.threshold ?? 5);
    setReorderBranchId(sku.branchId || branches[0]?.id || "");
    setReorderError(null);
  };

  const handleSaveReorderPoint = async () => {
    if (!reorderModalSku) return;
    if (reorderInputVal < 0) {
      setReorderError("Ngưỡng cảnh báo không được là số âm.");
      return;
    }
    const branchId = reorderBranchId || reorderModalSku.branchId || branches[0]?.id;
    if (!branchId) {
      setReorderError("Vui lòng chọn chi nhánh áp dụng ngưỡng cảnh báo.");
      return;
    }

    setSavingReorder(true);
    setReorderError(null);
    const res = await apiAdapter.updateReorderPoint({
      branchId,
      skuId: reorderModalSku.id,
      reorderPoint: Number(reorderInputVal),
    });
    setSavingReorder(false);

    if (res.error) {
      setReorderError(res.error.message || "Lưu ngưỡng cảnh báo thất bại.");
    } else {
      setReorderModalSku(null);
      await loadBalances();
    }
  };

  // [F4] Tải dữ liệu Sổ cái kho và lưu riêng lỗi sổ kho
  const loadLedger = async () => {
    const seq = ++ledgerReqSeqRef.current;
    setLoadingLedger(true);
    setLedgerError(null);
    const lRes = await apiAdapter.getLedger();
    if (seq !== ledgerReqSeqRef.current) return;
    if (lRes.error) {
      setLedgerError(lRes.error.message || "Không thể tải sổ cái kho (Ledger) từ máy chủ.");
    } else if (lRes.data) {
      setLedger(lRes.data);
    }
    setLoadingLedger(false);
  };

  // [F3] Tải số dư tổng và số dư kho đang chọn, chống stale responses bằng sequence counter
  const loadBalances = async (
    targetBranch = branchFilter,
    targetWarehouse = balanceWarehouseFilter
  ) => {
    const seq = ++balanceReqSeqRef.current;
    const promises: Promise<any>[] = [
      apiAdapter.getBalances(),
      apiAdapter.getSkus(),
    ];
    if (targetWarehouse !== "all") {
      promises.push(
        apiAdapter.getBalances({
          branchId: targetBranch !== "all" ? targetBranch : undefined,
          warehouseId: targetWarehouse,
        })
      );
    }

    const [bRes, pRes, whRes] = await Promise.all(promises);

    // Bỏ qua nếu có request khác được kích hoạt sau đó (Race Condition Protection)
    if (seq !== balanceReqSeqRef.current) return;

    if (bRes.error || pRes.error) {
      setLoadError(
        bRes.error?.message || pRes.error?.message || "Không thể tải dữ liệu kho từ máy chủ."
      );
    } else {
      setLoadError(null);
    }

    if (bRes.data) {
      setSkus(bRes.data);
    }
    if (pRes.data) {
      setAvailableSkus(pRes.data);
    }
    if (targetWarehouse !== "all") {
      if (whRes?.error) {
        // [F3] Khi API kho đang chọn bị lỗi, xóa dữ liệu kho cũ ngay để không hiển thị sai lệch dưới nhãn kho mới
        setWarehouseBalances([]);
        setWarehouseBalanceError(whRes.error?.message || "Không thể tải số dư tồn kho của kho đã chọn.");
      } else if (whRes?.data) {
        setWarehouseBalances(whRes.data);
        setWarehouseBalanceError(null);
      }
    } else {
      setWarehouseBalances([]);
      setWarehouseBalanceError(null);
    }
  };

  const loadData = async () => {
    setLoading(true);
    setLoadError(null);
    const [sRes, brRes, wRes] = await Promise.all([
      apiAdapter.getSuppliers(),
      apiAdapter.getBranches(),
      apiAdapter.getWarehouses(),
    ]);

    if (sRes.data) {
      setSuppliers(sRes.data);
      if (sRes.data[0]?.id && !selectedSupplier) setSelectedSupplier(sRes.data[0].id);
    }
    const loadedBranches = brRes.data || [];
    const loadedWarehouses = wRes.data || [];
    setBranches(loadedBranches);
    setWarehouses(loadedWarehouses);

    // [A04] Gán mặc định Chi nhánh đầu tiên và Kho đầu tiên thuộc đúng Chi nhánh đó
    const defaultBranchId = selectedBranch || loadedBranches[0]?.id || "";
    if (defaultBranchId) {
      setSelectedBranch(defaultBranchId);
      const branchWhs = loadedWarehouses.filter((w: any) => w.branchId === defaultBranchId);
      if (!selectedWarehouse || !branchWhs.some((w: any) => w.id === selectedWarehouse)) {
        setSelectedWarehouse(branchWhs[0]?.id || "");
      }
    }

    if (!reserveBranchId && loadedBranches[0]?.id) {
      const initBrId = loadedBranches[0].id;
      setReserveBranchId(initBrId);
      const initWh = loadedWarehouses.find((w: any) => w.branchId === initBrId);
      if (initWh) setReserveWarehouseId(initWh.id);
    }

    await Promise.all([
      loadBalances(branchFilter, balanceWarehouseFilter),
      loadLedger(),
    ]);

    setLoading(false);
  };

  useEffect(() => {
    loadData();
  }, []);

  // [F3] Khi lọc theo Kho hoặc Chi nhánh, nạp lại số dư kho an toàn tránh race condition
  useEffect(() => {
    loadBalances(branchFilter, balanceWarehouseFilter);
  }, [branchFilter, balanceWarehouseFilter]);

  // Returning from a stock operation in another tab must read the authoritative balances and ledger.
  // [BF-016] Tham gia nhóm chi nhánh SignalR (cụ thể hoặc toàn bộ chi nhánh đang hoạt động) và lắng nghe biến động tồn kho realtime
  useEffect(() => {
    const targetBranchIds =
      branchFilter && branchFilter !== "all"
        ? [branchFilter]
        : branches.map((b) => b.id).filter(Boolean);

    targetBranchIds.forEach((bId) => {
      realtimeAdapter.joinBranch(bId);
    });

    const refreshStock = () => {
      void loadBalances(branchFilter, balanceWarehouseFilter);
      void loadLedger();
    };
    window.addEventListener("focus", refreshStock);
    window.addEventListener("oism:stock-changed", refreshStock);
    window.addEventListener("oism:reservation-expired", refreshStock);
    window.addEventListener("oism:realtime-reconnected", refreshStock);
    return () => {
      window.removeEventListener("focus", refreshStock);
      window.removeEventListener("oism:stock-changed", refreshStock);
      window.removeEventListener("oism:reservation-expired", refreshStock);
      window.removeEventListener("oism:realtime-reconnected", refreshStock);
      targetBranchIds.forEach((bId) => {
        realtimeAdapter.leaveBranch(bId);
      });
      balanceReqSeqRef.current++;
      ledgerReqSeqRef.current++;
    };
  }, [branchFilter, balanceWarehouseFilter, branches]);

  // [A03 & F3] Khi mở Modal Nhập kho và chọn Chi nhánh / Kho, tải số dư thực tế tại đúng Kho đó để mô phỏng WAC chuẩn xác
  const loadModalWhStock = async (branch = selectedBranch, warehouse = selectedWarehouse) => {
    if (!receiptModal || !branch || !warehouse) {
      setModalWarehouseBalances([]);
      setModalWhError(null);
      return;
    }
    const seq = ++modalWhReqSeqRef.current;
    setLoadingModalWhStock(true);
    setModalWarehouseBalances([]); // Ngay lập tức làm rỗng để không lấy số dư kho cũ tính WAC
    setModalWhError(null);

    const res = await apiAdapter.getBalances({
      branchId: branch,
      warehouseId: warehouse,
    });

    if (seq !== modalWhReqSeqRef.current) return;

    if (res.error) {
      setModalWarehouseBalances([]);
      setModalWhError(res.error.message || "Không thể tải số dư tồn kho tiếp nhận để tính WAC.");
    } else if (res.data) {
      setModalWarehouseBalances(res.data);
      setModalWhError(null);
    }
    setLoadingModalWhStock(false);
  };

  useEffect(() => {
    loadModalWhStock();
  }, [receiptModal, selectedBranch, selectedWarehouse]);

  // [A08 & F3] Khi mở Modal Giữ chỗ (Reserve), tải số dư khả dụng của đúng Chi nhánh & Kho được chọn
  const loadReserveWhStock = async (branch = reserveBranchId, warehouse = reserveWarehouseId) => {
    if (!reserveModalOpen || !branch || !warehouse) {
      setReserveWhBalances([]);
      setReserveWhError(null);
      return;
    }
    const seq = ++reserveWhReqSeqRef.current;
    setLoadingReserveWh(true);
    setReserveWhBalances([]); // Ngay lập tức làm rỗng để không hiển thị số dư kho cũ
    setReserveWhError(null);

    const res = await apiAdapter.getBalances({
      branchId: branch,
      warehouseId: warehouse,
    });

    if (seq !== reserveWhReqSeqRef.current) return;

    if (res.error) {
      setReserveWhBalances([]);
      setReserveWhError(res.error.message || "Không thể tải số dư tồn kho giữ chỗ.");
    } else if (res.data) {
      setReserveWhBalances(res.data);
      setReserveWhError(null);
    }
    setLoadingReserveWh(false);
  };

  useEffect(() => {
    loadReserveWhStock();
  }, [reserveModalOpen, reserveBranchId, reserveWarehouseId]);

  // [A04] Danh sách kho đã lọc chặt theo Chi nhánh trên Toolbar, Modal Nhập kho và Modal Giữ chỗ
  const toolbarWarehouses = useMemo(
    () => warehouses.filter((w) => branchFilter === "all" || w.branchId === branchFilter),
    [warehouses, branchFilter]
  );

  const receiptBranchWarehouses = useMemo(
    () => warehouses.filter((w) => w.branchId === selectedBranch),
    [warehouses, selectedBranch]
  );

  const reserveBranchWarehouses = useMemo(
    () => warehouses.filter((w) => w.branchId === reserveBranchId),
    [warehouses, reserveBranchId]
  );

  // [F5] Lọc chính xác theo warehouseId, tránh trường hợp chọn "Kho" dính cả "Kho 2"
  const filteredLedger = useMemo(
    () =>
      ledger.filter(
        (item) =>
          (warehouseFilter === "all" || item.warehouseId === warehouseFilter) &&
          (operationFilter === "all" || item.transaction === operationFilter) &&
          (directionFilter === "all" || item.direction === directionFilter) &&
          `${item.sku} ${item.reference}`.toLowerCase().includes(searchQuery.toLowerCase())
      ),
    [ledger, warehouseFilter, operationFilter, directionFilter, searchQuery]
  );

  const activeBalanceSource = balanceWarehouseFilter !== "all" ? warehouseBalances : skus;

  const filteredSkus = useMemo(
    () =>
      activeBalanceSource.filter((sku) => {
        const matchesSearch = `${sku.name} ${sku.code} ${sku.variant || ""}`
          .toLowerCase()
          .includes(searchQuery.toLowerCase());
        const matchesBranch =
          branchFilter === "all" || sku.branchId === branchFilter || sku.branchName === branchFilter;
        const available = sku.onHand - sku.reserved;
        const thresh = thresholds[sku.id] ?? sku.threshold;
        const matchesStatus =
          stockStatusFilter === "all" || (stockStatusFilter === "low" && available <= thresh);
        return matchesSearch && matchesBranch && matchesStatus;
      }),
    [activeBalanceSource, searchQuery, branchFilter, stockStatusFilter, thresholds]
  );

  // [A05] Tính toán danh sách hiển thị theo phân trang
  const totalBalancePages = Math.max(1, Math.ceil(filteredSkus.length / balancePageSize));
  const pagedSkus = useMemo(() => {
    const safePage = Math.min(balancePage, totalBalancePages);
    const start = (safePage - 1) * balancePageSize;
    return filteredSkus.slice(start, start + balancePageSize);
  }, [filteredSkus, balancePage, balancePageSize, totalBalancePages]);

  const totalLedgerPages = Math.max(1, Math.ceil(filteredLedger.length / ledgerPageSize));
  const pagedLedger = useMemo(() => {
    const safePage = Math.min(ledgerPage, totalLedgerPages);
    const start = (safePage - 1) * ledgerPageSize;
    return filteredLedger.slice(start, start + ledgerPageSize);
  }, [filteredLedger, ledgerPage, ledgerPageSize, totalLedgerPages]);

  const lowStock = skus.filter(
    (sku) => sku.isActive !== false && sku.onHand - sku.reserved <= (thresholds[sku.id] ?? sku.threshold)
  );

  // [U01-A] Tải danh sách đơn mua có thể nhận hàng (Approved hoặc PartiallyReceived)
  const loadReceivablePurchaseOrders = async () => {
    setLoadingPos(true);
    try {
      const res = await purchaseOrderService.getPurchaseOrders({ pageSize: 100 });
      const filtered = (res.items || []).filter(
        (po) => po.status === "Approved" || po.status === "PartiallyReceived"
      );
      setReceivablePos(filtered);
      return filtered;
    } catch (err: any) {
      console.error("Không thể tải danh sách đơn mua:", err);
      return [];
    } finally {
      setLoadingPos(false);
    }
  };

  // [U01-A] Chọn đơn mua hàng đã duyệt để đổ mặt hàng và khóa giá
  const handleSelectPurchaseOrder = async (poId: string) => {
    setSelectedPurchaseOrderId(poId);
    if (!poId) {
      setSelectedPoDetail(null);
      setReceiptItems([]);
      return;
    }
    setLoading(true);
    setReceiptError(null);
    try {
      const po = await purchaseOrderService.getPurchaseOrderById(poId);
      setSelectedPoDetail(po);
      setSelectedBranch(po.branchId);
      setSelectedSupplier(po.supplierId);
      const branchWhs = warehouses.filter((w) => w.branchId === po.branchId);
      if (!selectedWarehouse || !branchWhs.some((w) => w.id === selectedWarehouse)) {
        setSelectedWarehouse(branchWhs[0]?.id || "");
      }
      // Khởi tạo các mặt hàng từ đơn mua (chỉ những dòng còn lượng nhận)
      const lines = po.items
        .filter((it) => it.remainingQuantity > 0)
        .map((it) => ({
          purchaseOrderItemId: it.id,
          skuId: it.skuId,
          skuCode: it.skuCode,
          skuName: it.productName,
          unitName: it.unitName,
          factorSnapshot: it.factorSnapshot,
          orderedQuantity: it.quantity,
          remainingQuantity: it.remainingQuantity,
          quantity: it.remainingQuantity,
          unitPrice: it.unitPrice, // KHÓA ĐƠN GIÁ THEO ĐƠN MUA
        }));
      setReceiptItems(lines);
    } catch (err: any) {
      setReceiptError(
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        "Không thể tải thông tin đơn mua hàng."
      );
    } finally {
      setLoading(false);
    }
  };

  // [F1] Mở Modal Phiếu nhập và tự động khôi phục / đối soát phiếu nháp đang chờ xác nhận
  const closeReceiptModal = () => {
    if (isConfirmingReceiptRef.current) return;
    receiptReadSeq.current++;
    setVerifyingPendingReceipt(false);
    setReceiptModal(false);
  };

  // Always review the server snapshot, including drafts created in another session.
  const openSavedReceipt = async (id: string) => {
    const seq = ++receiptReadSeq.current;
    setReceiptListOpen(false);
    setReceiptModal(true);
    setConfirmedSuccess(false);
    setCreatedReceiptId(id);
    setReceiptReady(false);
    setReceiptError(null);
    setVerifyingPendingReceipt(true);
    try {
      const response = await apiAdapter.getGoodsReceiptById(id);
      if (seq !== receiptReadSeq.current || receiptActorRef.current !== receiptActor) return;
      const receipt = response.data;
      if (response.error || !receipt || receipt.id !== id) {
        throw new Error(response.error?.message || "Không thể đọc phiếu nhập từ máy chủ.");
      }
      if (receipt.status !== "Draft" && receipt.status !== "Confirmed") {
        throw new Error("Phiếu không còn ở trạng thái cho phép xác nhận.");
      }
      const items = receipt.items.map((item: any) => ({
        purchaseOrderItemId: item.purchaseOrderItemId,
        skuId: item.skuId,
        skuCode: item.skuCode,
        skuName: item.productName,
        unitName: item.unitName,
        factorSnapshot: item.factorSnapshot,
        quantity: item.quantity,
        unitPrice: item.unitPrice,
      }));
      setSelectedBranch(receipt.branchId);
      setSelectedWarehouse(receipt.warehouseId);
      setSelectedSupplier(receipt.supplierId);
      setSelectedPurchaseOrderId(receipt.purchaseOrderId || "");
      setReceiptNotes(receipt.note || "");
      setReceiptItems(items);
      setReceiptNumber(receipt.number);
      if (receipt.status === "Confirmed") {
        if (loadSavedPendingReceipt(currentTenantId)?.receiptId === id) clearSavedPendingReceipt(currentTenantId);
        setCreatedReceiptId(null);
        setConfirmedSuccess(true);
      } else {
        savePendingReceipt({
          receiptId: id,
          purchaseOrderId: receipt.purchaseOrderId,
          purchaseOrderNumber: receipt.purchaseOrderNumber,
          branchId: receipt.branchId,
          warehouseId: receipt.warehouseId,
          supplierId: receipt.supplierId,
          notes: receipt.note || "",
          items,
        }, currentTenantId);
        setReceiptReady(true);
      }
    } catch (error) {
      if (seq === receiptReadSeq.current && receiptActorRef.current === receiptActor) {
        setReceiptError(error instanceof Error ? error.message : "Không thể tải phiếu nhập.");
      }
    } finally {
      if (seq === receiptReadSeq.current && receiptActorRef.current === receiptActor) setVerifyingPendingReceipt(false);
    }
  };

  const openReceiptModal = async () => {
    setReceiptModal(true);
    setConfirmedSuccess(false);
    setReceiptError(null);

    const pending = loadSavedPendingReceipt(currentTenantId);
    if (pending?.receiptId) {
      await openSavedReceipt(pending.receiptId);
      return;
    }
    setReceiptReady(false);
    setReceiptNumber("");
    setCreatedReceiptId(null);
    setSelectedPurchaseOrderId("");
    setSelectedPoDetail(null);
    setReceiptItems([]);

    loadReceivablePurchaseOrders();
  };

  const openReceiptModalForPo = async (poId: string) => {
    setReceiptModal(true);
    setConfirmedSuccess(false);
    setReceiptError(null);
    setReceiptReady(false);
    setReceiptNumber("");
    setCreatedReceiptId(null);
    await loadReceivablePurchaseOrders();
    await handleSelectPurchaseOrder(poId);
  };

  useEffect(() => {
    const poParam = searchParams.get("purchaseOrderId");
    if (poParam) {
      openReceiptModalForPo(poParam);
      searchParams.delete("purchaseOrderId");
      setSearchParams(searchParams, { replace: true });
    }
  }, [searchParams]);

  // [F1] Người dùng chủ động bỏ theo dõi phiếu nháp đang chờ để tạo mới từ đầu (chứng từ cũ vẫn lưu Draft trên server)
  const handleDiscardPendingReceipt = () => {
    if (createdReceiptId) {
      const confirmDiscard = window.confirm(
        `Phiếu nhập nháp (Mã: ${createdReceiptId.slice(
          0,
          8
        )}...) đã được tạo và lưu ở trạng thái Draft trên máy chủ. ` +
          "Thao tác này chỉ dừng theo dõi trên trình duyệt để lập phiếu mới, không xóa phiếu trên server. Bạn có chắc chắn muốn bỏ theo dõi phiếu này?"
      );
      if (!confirmDiscard) return;
    }
    clearSavedPendingReceipt(currentTenantId);
    setCreatedReceiptId(null);
    setReceiptReady(false);
    setReceiptNumber("");
    setReceiptError(null);
    setSelectedPurchaseOrderId("");
    setSelectedPoDetail(null);
    setReceiptItems([]);
    loadReceivablePurchaseOrders();
  };

  // [U01-A] Hủy phiếu nhập kho nháp trên server
  const handleCancelDraftReceipt = async () => {
    if (!createdReceiptId) return;
    const ok = window.confirm(
      `Bạn có chắc chắn muốn hủy phiếu nhập nháp ${receiptNumber || createdReceiptId.slice(0, 8)} trên máy chủ không?`
    );
    if (!ok) return;

    setLoading(true);
    setReceiptError(null);
    try {
      const res = await apiAdapter.cancelGoodsReceipt(createdReceiptId);
      if (res.error) {
        setReceiptError(res.error.message);
        return;
      }
      clearSavedPendingReceipt(currentTenantId);
      setCreatedReceiptId(null);
      setReceiptReady(false);
      setReceiptModal(false);
      await Promise.all([loadBalances(), loadLedger()]);
    } catch (err: any) {
      setReceiptError(err?.message || "Không thể hủy phiếu nhập.");
    } finally {
      setLoading(false);
    }
  };

  const removeReceiptLine = (idx: number) => {
    if (receiptItems.length <= 1 || createdReceiptId) return;
    setReceiptItems(receiptItems.filter((_, i) => i !== idx));
  };

  const totalReceiptQuantity = receiptItems.reduce((acc, cur) => acc + (Number(cur.quantity) || 0), 0);
  const totalReceiptAmount = receiptItems.reduce(
    (acc, cur) => acc + (Number(cur.quantity) || 0) * (Number(cur.unitPrice) || 0),
    0
  );

  // [A07 & F1 & U01-A] Xử lý tạo và xác nhận phiếu nhập kho an toàn, idempotent retry
  const handleSaveReceipt = async () => {
    if (isConfirmingReceiptRef.current || createdReceiptId) return;
    const actor = receiptActor;
    setReceiptError(null);

    if (!selectedPurchaseOrderId) {
      setReceiptError("Bắt buộc chọn Đơn mua hàng đã duyệt để lập phiếu nhập kho (U01-A).");
      return;
    }

    if (!selectedBranch || !selectedWarehouse) {
      setReceiptError("Vui lòng chọn Kho hàng tiếp nhận.");
      return;
    }
    // Kiểm tra Kho phải thuộc đúng Chi nhánh đã chọn [A04]
    const validWh = warehouses.find((w) => w.id === selectedWarehouse && w.branchId === selectedBranch);
    if (!validWh) {
      setReceiptError("Kho tiếp nhận không thuộc Chi nhánh của đơn mua. Vui lòng chọn lại kho hợp lệ.");
      return;
    }

    const activeLines = receiptItems.filter((item) => Number(item.quantity) > 0);
    if (activeLines.length === 0) {
      setReceiptError("Vui lòng nhập số lượng thực nhận (> 0) cho ít nhất một mặt hàng.");
      return;
    }

    // Kiểm tra không vượt quá số lượng còn lại của đơn mua
    for (const line of activeLines) {
      if (line.remainingQuantity !== undefined && line.quantity > line.remainingQuantity) {
        setReceiptError(
          `Mặt hàng ${line.skuCode} nhập vượt số lượng còn lại của đơn mua (Tối đa: ${line.remainingQuantity} ${line.unitName || ""}).`
        );
        return;
      }
    }

    isConfirmingReceiptRef.current = true;
    setLoading(true);
    let receiptIdToConfirm = createdReceiptId;

    try {
      // Bước 1: Chỉ gọi API tạo mới nếu chưa có createdReceiptId (tránh tạo trùng phiếu khi Retry [A07])
      if (!receiptIdToConfirm) {
        const payload = {
          purchaseOrderId: selectedPurchaseOrderId,
          warehouseId: selectedWarehouse,
          note: receiptNotes.trim() || undefined,
          items: activeLines.map((item) => ({
            purchaseOrderItemId: item.purchaseOrderItemId!,
            quantity: Number(item.quantity) || 1,
          })),
        };

        const res = await apiAdapter.createGoodsReceipt(payload);
        if (receiptActorRef.current !== actor) return;

        if (res.error || !res.data?.id) {
          setLoading(false);
          setReceiptError(res.error?.message || "Không thể tạo phiếu nhập kho.");
          return;
        }

        receiptIdToConfirm = res.data.id;
        setCreatedReceiptId(receiptIdToConfirm);
        setReceiptNumber(res.data.number);
        setReceiptReady(true);

        // [F1] Lưu thông tin phiếu nháp vào sessionStorage theo tenant để không bị mất khi đóng/mở form
        savePendingReceipt(
          {
            receiptId: receiptIdToConfirm,
            purchaseOrderId: selectedPurchaseOrderId,
            purchaseOrderNumber: res.data.purchaseOrderNumber || selectedPoDetail?.number,
            branchId: selectedBranch,
            warehouseId: selectedWarehouse,
            supplierId: selectedSupplier,
            notes: receiptNotes,
            items: activeLines,
          },
          currentTenantId
        );
      }

      // Saving a draft ends here: inventory is only posted by a separate user action.
    } catch (err: any) {
      if (receiptActorRef.current === actor) setReceiptError(err?.message || "Không thể lưu phiếu nháp.");
    } finally {
      if (receiptActorRef.current === actor) setLoading(false);
      isConfirmingReceiptRef.current = false;
    }
  };

  const handleConfirmReceipt = async () => {
    if (isConfirmingReceiptRef.current || !createdReceiptId || !receiptReady) return;
    const id = createdReceiptId;
    const actor = receiptActor;
    isConfirmingReceiptRef.current = true;
    setLoading(true);
    setReceiptError(null);
    try {
      const before = await apiAdapter.getGoodsReceiptById(id);
      if (receiptActorRef.current !== actor) return;
      if (before.error || !before.data || before.data.id !== id) {
        throw new Error(before.error?.message || "Không thể kiểm tra trạng thái phiếu nhập.");
      }
      if (before.data.status !== "Confirmed") {
        if (before.data.status !== "Draft") throw new Error("Chỉ có thể xác nhận phiếu nháp.");
        const result = await apiAdapter.confirmGoodsReceipt(id);
        if (receiptActorRef.current !== actor) return;
        if (result.error || result.data?.status !== "Confirmed") {
          // Lost response / duplicate confirm: trust the authoritative status, not error text.
          const after = await apiAdapter.getGoodsReceiptById(id);
          if (receiptActorRef.current !== actor) return;
          if (after.data?.id !== id || after.data.status !== "Confirmed") {
            throw new Error(result.error?.message || "Chưa xác minh được kết quả. Phiếu được giữ lại để kiểm tra và thử xác nhận lại.");
          }
        }
      }
      clearSavedPendingReceipt(currentTenantId);
      setCreatedReceiptId(null);
      setReceiptReady(false);
      setConfirmedSuccess(true);
      await Promise.all([loadBalances(), loadLedger()]);
    } catch (error) {
      if (receiptActorRef.current === actor) setReceiptError(error instanceof Error ? error.message : "Không thể xác nhận phiếu nhập.");
    } finally {
      isConfirmingReceiptRef.current = false;
      if (receiptActorRef.current === actor) setLoading(false);
    }
  };

  // [A08 & F2] Mở modal Giữ chỗ tồn kho (Reserve) cho 1 SKU tại đúng Chi nhánh / Kho
  const openReserveModal = (sku?: Sku) => {
    setReserveError(null);
    setReserveSuccess(null);

    // [F2] Khôi phục thao tác giữ chỗ dở dang nếu có lưu trong sessionStorage
    const pending = loadSavedPendingReservation(currentTenantId);
    if (pending && pending.idempotencyKey) {
      // Nếu người dùng bấm Giữ chỗ trên một SKU KHÁC với SKU đang dở dang
      if (sku && sku.id !== pending.skuId) {
        const pendingSkuName =
          availableSkus.find((s) => s.id === pending.skuId)?.name ||
          skus.find((s) => s.id === pending.skuId)?.name ||
          pending.skuId;
        const confirmDiscard = window.confirm(
          `Bạn đang có một thao tác giữ chỗ dở dang chưa hoàn tất cho sản phẩm "${pendingSkuName}" (Định danh: ${pending.idempotencyKey.slice(
            0,
            8
          )}...). ` +
            `Bạn có muốn hủy theo dõi thao tác cũ để tạo giữ chỗ mới cho sản phẩm "${sku.name}" không?`
        );
        if (confirmDiscard) {
          clearSavedPendingReservation(currentTenantId);
          const freshKey = generateUuid();
          setReserveIdempotencyKey(freshKey);
          setIsRetryingReserve(false);
          const targetBranchId = sku.branchId || (branchFilter !== "all" ? branchFilter : branches[0]?.id || "");
          setReserveBranchId(targetBranchId);
          const branchWhs = warehouses.filter((w) => w.branchId === targetBranchId);
          const targetWhId =
            sku.warehouseId && branchWhs.some((w) => w.id === sku.warehouseId)
              ? sku.warehouseId
              : branchWhs[0]?.id || "";
          setReserveWarehouseId(targetWhId);
          setReserveSkuId(sku.id);
          setReserveQty(1);
          setReserveModalOpen(true);
          return;
        }
      }
      setReserveIdempotencyKey(pending.idempotencyKey);
      if (pending.branchId) setReserveBranchId(pending.branchId);
      if (pending.warehouseId) setReserveWarehouseId(pending.warehouseId);
      if (pending.skuId) setReserveSkuId(pending.skuId);
      if (pending.quantity) setReserveQty(pending.quantity);
      setIsRetryingReserve(true);
    } else {
      const freshKey = generateUuid();
      setReserveIdempotencyKey(freshKey);
      setIsRetryingReserve(false);

      const targetBranchId = sku?.branchId || (branchFilter !== "all" ? branchFilter : branches[0]?.id || "");
      setReserveBranchId(targetBranchId);
      const branchWhs = warehouses.filter((w) => w.branchId === targetBranchId);
      const targetWhId =
        sku?.warehouseId && branchWhs.some((w) => w.id === sku.warehouseId)
          ? sku.warehouseId
          : branchWhs[0]?.id || "";
      setReserveWarehouseId(targetWhId);
      const defaultSku =
        sku?.id ||
        availableSkus.find((s) => s.isActive !== false)?.id ||
        skus.find((s) => s.isActive !== false)?.id ||
        "";
      setReserveSkuId(defaultSku);
      setReserveQty(1);
    }
    setReserveModalOpen(true);
  };

  // [F2] Bỏ thao tác giữ chỗ dở dang và sinh UUID mới cho ý định mới
  const handleDiscardPendingReservation = () => {
    if (isRetryingReserve && reserveIdempotencyKey) {
      const confirmDiscard = window.confirm(
        `Thao tác giữ chỗ này (Định danh: ${reserveIdempotencyKey.slice(
          0,
          8
        )}...) có thể đã được giữ chỗ trên máy chủ. Bạn có chắc chắn muốn bỏ theo dõi để tạo giữ chỗ mới?`
      );
      if (!confirmDiscard) return;
    }
    clearSavedPendingReservation(currentTenantId);
    const freshKey = generateUuid();
    setReserveIdempotencyKey(freshKey);
    setIsRetryingReserve(false);
    setReserveError(null);
    setReserveSuccess(null);
  };

  // [F2, BF-013] Khi người dùng thay đổi chi nhánh/kho/SKU/số lượng, yêu cầu xác nhận nếu đang có thao tác retry/unknown
  const handleReserveFormChange = () => {
    if (isRetryingReserve) {
      const confirmDiscard = window.confirm(
        `Thao tác giữ chỗ trước đó (mã ${reserveIdempotencyKey.slice(
          0,
          8
        )}...) chưa rõ kết quả và có thể đã thành công trên máy chủ. Bạn có chắc chắn muốn bỏ theo dõi để tạo giữ chỗ mới?`
      );
      if (!confirmDiscard) return false;
      clearSavedPendingReservation(currentTenantId);
      setIsRetryingReserve(false);
    }
    setReserveIdempotencyKey(generateUuid());
    setReserveError(null);
    setReserveSuccess(null);
    return true;
  };

  const handleReserveStock = async () => {
    if (isReservingRef.current) return;
    setReserveError(null);
    setReserveSuccess(null);
    if (!reserveBranchId || !reserveWarehouseId || !reserveSkuId) {
      setReserveError("Vui lòng chọn đầy đủ Chi nhánh, Kho hàng và Mã SKU cần giữ chỗ.");
      return;
    }
    const chosenSku =
      availableSkus.find((s) => s.id === reserveSkuId) || skus.find((s) => s.id === reserveSkuId);
    if (chosenSku?.isActive === false) {
      setReserveError("Mã SKU này đang ở trạng thái [Ngừng hoạt động], không thể giữ chỗ.");
      return;
    }
    const whSkuBal = reserveWhBalances.find((b) => b.id === reserveSkuId);
    const whAvailable = whSkuBal ? whSkuBal.onHand - whSkuBal.reserved : 0;
    if (reserveQty <= 0) {
      setReserveError("Số lượng giữ chỗ phải lớn hơn 0.");
      return;
    }

    // [F2] Nếu KHÔNG PHẢI là thao tác retry (lần gọi mới), kiểm tra tồn khả dụng phía client
    // Nếu là RETRY của key đã gửi trước đó, không dùng whAvailable để chặn vì backend có thể đã trừ tồn rồi!
    if (!isRetryingReserve && reserveQty > whAvailable) {
      setReserveError(
        `Kho đang chọn chỉ có ${whAvailable} sản phẩm khả dụng (Yêu cầu: ${reserveQty}). Vui lòng chọn kho khác hoặc giảm số lượng.`
      );
      return;
    }

    // [F2] Duy trì Idempotency-Key UUID RFC4122 chuẩn, không dùng fallback chuỗi tự do
    const key = reserveIdempotencyKey || generateUuid();
    setReserveIdempotencyKey(key);

    // Lưu thao tác pending vào sessionStorage trước khi gọi API
    savePendingReservation(
      {
        idempotencyKey: key,
        branchId: reserveBranchId,
        warehouseId: reserveWarehouseId,
        skuId: reserveSkuId,
        quantity: reserveQty,
      },
      currentTenantId
    );

    isReservingRef.current = true;
    setLoading(true);
    try {
      const res = await apiAdapter.reserveStock({
        branchId: reserveBranchId,
        warehouseId: reserveWarehouseId,
        skuId: reserveSkuId,
        quantity: reserveQty,
        durationMinutes: 15,
        idempotencyKey: key,
      });

      if (res.error) {
        // Giữ nguyên reserveIdempotencyKey để khi người dùng bấm Thử lại sẽ gửi cùng định danh
        setReserveError(`${res.error.message} (Định danh: ${key.slice(0, 8)}...)`);
        setIsRetryingReserve(true);
        return;
      }

      // Thành công: xóa Idempotency Key khỏi state và sessionStorage
      clearSavedPendingReservation(currentTenantId);
      setReserveIdempotencyKey(null);
      setIsRetryingReserve(false);
      setReserveSuccess(
        `Đã khóa giữ chỗ thành công ${reserveQty} SP tại kho đã chọn (Hiệu lực 15 phút - Advisory Lock).`
      );

      // [F3] Tải lại số dư tổng và số dư kho đang chọn trên bảng chính
      await Promise.all([loadBalances(), loadLedger()]);

      // Nạp lại tồn kho của kho vừa giữ chỗ trong modal
      await loadReserveWhStock(reserveBranchId, reserveWarehouseId);
    } finally {
      isReservingRef.current = false;
      setLoading(false);
    }
  };

  return (
    <>
      <PageTitle
        eyebrow="FR-INV · FR-COST"
        title="Kho & sổ kho"
        description="Theo dõi tồn thực tế, tồn giữ chỗ, thực bán khả dụng theo từng Chi nhánh / Kho và bút toán sổ cái bất biến."
        actions={
          <>
            <Button variant="secondary" disabled title="Chưa có luồng nhận trả hàng và ghi sổ kho.">
              <RotateCcw size={17} />Ghi nhận trả hàng <ComingSoonBadge />
            </Button>
            <Button variant="secondary" onClick={() => navigate("/purchase-orders")}>
              <ClipboardList size={17} />Đơn mua hàng
            </Button>
            <Button variant="secondary" onClick={() => openReserveModal()}>
              <Lock size={17} />Giữ chỗ theo kho
            </Button>
            <Button variant="secondary" onClick={() => setReceiptListOpen(true)}>Phiếu nhập đã lưu</Button>
            <Button onClick={openReceiptModal}>
              <Plus size={17} />Lập phiếu nhập
            </Button>
          </>
        }
      />

      {/* [A09] Hiển thị rõ thông báo lỗi kết nối và nút Tải lại trang thay vì hiển thị tồn = 0 */}
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
              <strong>Lỗi tải dữ liệu tồn kho / sổ kho từ máy chủ:</strong>
              <div style={{ fontSize: "13px" }}>{loadError}</div>
            </div>
          </div>
          <Button variant="secondary" onClick={loadData}>
            <RefreshCw size={15} /> Tải lại trang
          </Button>
        </div>
      )}

      <div className="process-strip">
        <div>
          <span><ArrowDownToLine size={18} /></span>
          <strong>Phiếu nhập</strong>
          <small>Sẵn sàng ghi sổ WAC</small>
        </div>
        <div>
          <span><ArrowRightLeft size={18} /></span>
          <strong>Chuyển kho</strong>
          <small>2 bước nội bộ</small>
        </div>
        <div>
          <span><ClipboardCheck size={18} /></span>
          <strong>Kiểm kê</strong>
          <small>Cân kho tự động</small>
        </div>
        <div>
          <span><TriangleAlert size={18} /></span>
          <strong>Cảnh báo tồn</strong>
          <small>{lowStock.length} SKU dưới ngưỡng</small>
        </div>
      </div>

      {lowStock.length > 0 && (
        <section className="low-stock-panel">
          <div>
            <TriangleAlert size={19} />
            <span>
              <strong>Cảnh báo tồn khả dụng (Available ≤ Threshold)</strong>
              <small>Tồn khả dụng = OnHand - Reserved. Cần lập phiếu nhập để tránh mất đơn bán.</small>
            </span>
          </div>
          <div className="low-stock-links">
            {lowStock.slice(0, 4).map((sku) => (
              <button key={`${sku.id}-${sku.branchId || "b"}`} onClick={() => setTab("balance")}>
                <strong>{sku.code} ({sku.branchName || "CN"})</strong>
                <span>
                  Khả dụng {sku.onHand - sku.reserved} / Ngưỡng {thresholds[sku.id] ?? sku.threshold}
                </span>
              </button>
            ))}
          </div>
          <Button variant="secondary" onClick={() => { if (skus.length > 0) openReorderModal(skus[0]); }}>
            <Settings2 size={16} />Cấu hình ngưỡng
          </Button>
        </section>
      )}

      <section className="card">
        <div className="tabs">
          <button className={tab === "balance" ? "active" : ""} onClick={() => setTab("balance")}>
            Bảng Tồn kho ({filteredSkus.length})
          </button>
          <button className={tab === "ledger" ? "active" : ""} onClick={() => { setTab("ledger"); void loadLedger(); }}>
            Sổ kho bất biến — Append-Only ({filteredLedger.length})
          </button>
          <button type="button" onClick={loadData} disabled={loading}
            style={{ marginLeft: "auto", display: "inline-flex", alignItems: "center", gap: 6 }}>
            <RefreshCw size={15} />Làm mới tồn & sổ kho
          </button>
        </div>

        {tab === "balance" ? (
          <>
            <div className="toolbar">
              <label className="search-field">
                <Search size={18} />
                <Input
                  value={searchQuery}
                  onChange={(e) => {
                    setSearchQuery(e.target.value);
                    setBalancePage(1);
                  }}
                  placeholder="Tìm SKU hoặc tên sản phẩm..."
                />
              </label>
              {/* [A04 & A08] Lọc Chi nhánh tự động lọc danh sách Kho tương ứng */}
              <Select
                aria-label="Chi nhánh"
                value={branchFilter}
                onChange={(e) => {
                  const nextBr = e.target.value;
                  setBranchFilter(nextBr);
                  setBalanceWarehouseFilter("all");
                  setBalancePage(1);
                }}
              >
                <option value="all">Tất cả chi nhánh</option>
                {branches.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </Select>
              <Select
                aria-label="Kho hàng"
                value={balanceWarehouseFilter}
                onChange={(e) => {
                  setBalanceWarehouseFilter(e.target.value);
                  setBalancePage(1);
                }}
              >
                <option value="all">Tất cả kho ({toolbarWarehouses.length})</option>
                {toolbarWarehouses.map((w) => (
                  <option key={w.id} value={w.id}>
                    {w.name}
                  </option>
                ))}
              </Select>
              <Select
                aria-label="Trạng thái tồn"
                value={stockStatusFilter}
                onChange={(e) => {
                  setStockStatusFilter(e.target.value);
                  setBalancePage(1);
                }}
              >
                <option value="all">Tất cả trạng thái</option>
                <option value="low">Chỉ hiển thị dưới ngưỡng ({lowStock.length})</option>
              </Select>
            </div>

            {/* [F3] Hiển thị rõ lỗi khi tải số dư của riêng kho đang lọc kèm nút Thử lại */}
            {warehouseBalanceError && balanceWarehouseFilter !== "all" && (
              <div
                style={{
                  background: "#fef2f2",
                  border: "1px solid #fca5a5",
                  color: "#b91c1c",
                  padding: "12px 16px",
                  borderRadius: "8px",
                  marginBottom: "16px",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  gap: "12px",
                }}
              >
                <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                  <AlertCircle size={18} />
                  <span>
                    <strong>Lỗi tải dữ liệu kho đã chọn:</strong> {warehouseBalanceError}
                  </span>
                </div>
                <Button
                  variant="secondary"
                  onClick={() => loadBalances(branchFilter, balanceWarehouseFilter)}
                >
                  <RefreshCw size={14} /> Thử lại tải kho
                </Button>
              </div>
            )}

            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>SKU / Sản phẩm</th>
                    <th>Chi nhánh / Kho</th>
                    <th>Tồn thực tế (OnHand)</th>
                    <th>Giữ chỗ (Reserved)</th>
                    <th>Khả dụng (Available)</th>
                    <th>Giá vốn BQ (WAC)</th>
                    <th>Giá trị tồn kho</th>
                    <th>Thao tác / Ngưỡng</th>
                  </tr>
                </thead>
                <tbody>
                  {loading && skus.length === 0 ? (
                    <tr>
                      <td colSpan={8} style={{ textAlign: "center", padding: "32px 0", color: "#64748b" }}>
                        <div
                          style={{
                            display: "inline-block",
                            width: "20px",
                            height: "20px",
                            border: "2px solid #cbd5e1",
                            borderTopColor: "#1769e0",
                            borderRadius: "50%",
                            animation: "spin 0.8s linear infinite",
                            verticalAlign: "middle",
                            marginRight: "8px",
                          }}
                        />
                        Đang nạp dữ liệu tồn kho từ máy chủ OISM...
                      </td>
                    </tr>
                  ) : loadError && skus.length === 0 ? (
                    <tr>
                      <td colSpan={8} style={{ textAlign: "center", padding: "36px 0", color: "#b91c1c" }}>
                        Không thể hiển thị bảng tồn kho do lỗi kết nối máy chủ.{" "}
                        <Button variant="secondary" onClick={loadData}>
                          Tải lại dữ liệu
                        </Button>
                      </td>
                    </tr>
                  ) : pagedSkus.length === 0 ? (
                    <tr>
                      <td colSpan={8} style={{ textAlign: "center", padding: "36px 0", color: "#64748b" }}>
                        Chưa có dữ liệu tồn kho khớp bộ lọc. Bấm <strong>"Tạo phiếu nhập"</strong> phía trên để nhập hàng và ghi nhận số dư ban đầu.
                      </td>
                    </tr>
                  ) : (
                    pagedSkus.map((sku) => {
                      const available = sku.onHand - sku.reserved;
                      const thresh = thresholds[sku.id] ?? sku.threshold;
                      const isUnder = available <= thresh;
                      const isInactive = sku.isActive === false;
                      return (
                        <tr key={`${sku.id}-${sku.branchId || "br"}-${sku.warehouseId || "wh"}`}>
                          <td>
                            <div className="product-cell">
                              <span className="product-thumb">{sku.image || "📦"}</span>
                              <div>
                                <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
                                  <strong>{sku.code}</strong>
                                  {isInactive && <Badge tone="neutral">[Ngừng hoạt động]</Badge>}
                                </div>
                                <span>
                                  {sku.name} {sku.variant ? `· ${sku.variant}` : ""}
                                </span>
                              </div>
                            </div>
                          </td>
                          <td>
                            <strong>{sku.branchName || "Chi nhánh chính"}</strong>
                            <div style={{ fontSize: "12px", color: "#64748b" }}>
                              {sku.warehouseName || "Tất cả các kho thuộc CN"}
                            </div>
                          </td>
                          <td>
                            <strong>{sku.onHand}</strong>
                          </td>
                          <td>
                            <span style={{ color: "#d97706", fontWeight: 600 }}>{sku.reserved}</span>
                          </td>
                          <td>
                            <strong className={isUnder ? "text-warning" : ""}>{available}</strong>
                          </td>
                          <td>{sku.averageCost.toLocaleString("vi-VN")} ₫</td>
                          <td>
                            <strong>{(sku.averageCost * sku.onHand).toLocaleString("vi-VN")} ₫</strong>
                          </td>
                          <td>
                            <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
                              <Button
                                variant="secondary"
                                disabled={isInactive || available <= 0}
                                onClick={() => openReserveModal(sku)}
                                title="Kiểm tra tồn từng kho & Giữ chỗ"
                              >
                                Giữ chỗ
                              </Button>
                              <Button
                                variant="ghost"
                                onClick={() => openReorderModal(sku)}
                                title="Cấu hình ngưỡng cảnh báo tồn kho an toàn"
                              >
                                ≤{thresh}
                              </Button>
                            </div>
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>

            {/* [A05] Thanh phân trang Bảng Tồn kho */}
            <div className="pagination">
              <span>
                Hiển thị{" "}
                {filteredSkus.length > 0 ? (balancePage - 1) * balancePageSize + 1 : 0}–
                {Math.min(balancePage * balancePageSize, filteredSkus.length)} trong tổng số{" "}
                <strong>{filteredSkus.length}</strong> bản ghi tồn kho
              </span>
              <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                <Select
                  aria-label="Số dòng mỗi trang"
                  value={String(balancePageSize)}
                  onChange={(e) => {
                    setBalancePageSize(Number(e.target.value));
                    setBalancePage(1);
                  }}
                >
                  <option value="10">10 dòng/trang</option>
                  <option value="20">20 dòng/trang</option>
                  <option value="50">50 dòng/trang</option>
                  <option value="100">100 dòng/trang</option>
                </Select>
                <Button
                  variant="secondary"
                  disabled={balancePage <= 1}
                  onClick={() => setBalancePage((p) => Math.max(1, p - 1))}
                >
                  <ChevronLeft size={16} /> Trang trước
                </Button>
                <span style={{ fontSize: "13px", fontWeight: 600, padding: "0 6px" }}>
                  Trang {balancePage} / {totalBalancePages}
                </span>
                <Button
                  variant="secondary"
                  disabled={balancePage >= totalBalancePages}
                  onClick={() => setBalancePage((p) => Math.min(totalBalancePages, p + 1))}
                >
                  Trang sau <ChevronRight size={16} />
                </Button>
              </div>
            </div>
          </>
        ) : (
          <>
            <div className="toolbar ledger-filters">
              <label className="search-field">
                <Search size={18} />
                <Input
                  value={searchQuery}
                  onChange={(e) => {
                    setSearchQuery(e.target.value);
                    setLedgerPage(1);
                  }}
                  placeholder="Tìm SKU hoặc mã chứng từ..."
                />
              </label>
              {/* [F5] Lọc chính xác theo ID kho, hiển thị nhãn kèm tên Chi nhánh */}
              <Select
                value={warehouseFilter}
                onChange={(e) => {
                  setWarehouseFilter(e.target.value);
                  setLedgerPage(1);
                }}
              >
                <option value="all">Tất cả kho hàng</option>
                {warehouses.map((w) => {
                  const br = branches.find((b) => b.id === w.branchId);
                  const label = br ? `${br.name} — ${w.name}` : w.name;
                  return (
                    <option key={w.id} value={w.id}>
                      {label}
                    </option>
                  );
                })}
              </Select>
              <Select
                value={directionFilter}
                onChange={(e) => {
                  setDirectionFilter(e.target.value);
                  setLedgerPage(1);
                }}
              >
                <option value="all">Bút toán IN và OUT</option>
                <option value="IN">Chỉ xem IN (Nhập/Hoàn)</option>
                <option value="OUT">Chỉ xem OUT (Xuất bán/Điều chuyển)</option>
              </Select>
              <Select
                value={operationFilter}
                onChange={(e) => {
                  setOperationFilter(e.target.value);
                  setLedgerPage(1);
                }}
              >
                <option value="all">Tất cả nghiệp vụ</option>
                <option value="Nhập kho nhà cung cấp">Nhập kho nhà cung cấp</option>
                <option value="Xuất kho đơn hàng">Xuất kho đơn hàng</option>
                <option value="Điều chuyển kho">Điều chuyển kho</option>
                <option value="Kiểm kê cân kho">Kiểm kê cân kho</option>
                <option value="Điều chỉnh tồn kho">Điều chỉnh tồn kho</option>
                <option value="Khách trả hàng">Khách trả hàng</option>
                <option value="Biến động kho">Biến động kho khác</option>
              </Select>
            </div>

            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Thời gian</th>
                    <th>Chi nhánh / Kho</th>
                    <th>SKU</th>
                    <th>Loại</th>
                    <th>Nghiệp vụ</th>
                    <th>Số lượng</th>
                    <th>Số dư sau</th>
                    <th>Mã chứng từ</th>
                  </tr>
                </thead>
                <tbody>
                  {/* [F4] Tách biệt trạng thái tải, trạng thái lỗi sổ kho và trạng thái rỗng */}
                  {loadingLedger || (loading && ledger.length === 0) ? (
                    <tr>
                      <td colSpan={8} style={{ textAlign: "center", padding: "32px 0", color: "#64748b" }}>
                        <div
                          style={{
                            display: "inline-block",
                            width: "20px",
                            height: "20px",
                            border: "2px solid #cbd5e1",
                            borderTopColor: "#1769e0",
                            borderRadius: "50%",
                            animation: "spin 0.8s linear infinite",
                            verticalAlign: "middle",
                            marginRight: "8px",
                          }}
                        />
                        Đang truy vấn sổ cái kho bất biến...
                      </td>
                    </tr>
                  ) : ledgerError ? (
                    <tr>
                      <td colSpan={8} style={{ textAlign: "center", padding: "32px 0" }}>
                        <div
                          style={{
                            display: "inline-flex",
                            flexDirection: "column",
                            alignItems: "center",
                            gap: "10px",
                            color: "#b91c1c",
                            background: "#fef2f2",
                            border: "1px solid #fca5a5",
                            borderRadius: "8px",
                            padding: "16px 24px",
                            maxWidth: "520px",
                            margin: "0 auto",
                          }}
                        >
                          <div style={{ display: "flex", alignItems: "center", gap: "8px", fontWeight: 600 }}>
                            <AlertCircle size={18} />
                            <span>Lỗi tải sổ cái kho: {ledgerError}</span>
                          </div>
                          <Button variant="secondary" onClick={loadLedger}>
                            <RefreshCw size={14} /> Thử lại tải sổ kho
                          </Button>
                        </div>
                      </td>
                    </tr>
                  ) : pagedLedger.length === 0 ? (
                    <tr>
                      <td colSpan={8} style={{ textAlign: "center", padding: "36px 0", color: "#64748b" }}>
                        Chưa có bút toán sổ kho nào. Các thao tác Nhập kho hoặc Xuất bán đơn hàng sẽ tự động ghi sổ tại đây.
                      </td>
                    </tr>
                  ) : (
                    pagedLedger.map((item) => (
                      <tr key={item.id}>
                        <td>{item.createdAt}</td>
                        <td>{item.warehouse}</td>
                        <td>
                          <strong>{item.sku}</strong>
                        </td>
                        <td>
                          <Badge tone={item.direction === "IN" ? "success" : "danger"}>
                            {item.direction}
                          </Badge>
                        </td>
                        <td>{item.transaction}</td>
                        <td>
                          <strong>
                            {item.direction === "IN" ? "+" : "−"}
                            {item.quantity}
                          </strong>
                        </td>
                        <td>
                          <strong>{item.balanceAfter}</strong>
                        </td>
                        <td>
                          <Button
                            variant="ghost"
                            className="text-link"
                            onClick={() => setSelectedLedger(item)}
                          >
                            {item.reference}
                          </Button>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>

              {/* [A05] Thanh phân trang Sổ cái kho */}
              <div className="pagination">
                <span>
                  Hiển thị{" "}
                  {filteredLedger.length > 0 ? (ledgerPage - 1) * ledgerPageSize + 1 : 0}–
                  {Math.min(ledgerPage * ledgerPageSize, filteredLedger.length)} trong tổng số{" "}
                  <strong>{filteredLedger.length}</strong> bút toán sổ kho
                </span>
                <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                  <Select
                    aria-label="Số dòng mỗi trang sổ kho"
                    value={String(ledgerPageSize)}
                    onChange={(e) => {
                      setLedgerPageSize(Number(e.target.value));
                      setLedgerPage(1);
                    }}
                  >
                    <option value="10">10 dòng/trang</option>
                    <option value="20">20 dòng/trang</option>
                    <option value="50">50 dòng/trang</option>
                    <option value="100">100 dòng/trang</option>
                  </Select>
                  <Button
                    variant="secondary"
                    disabled={ledgerPage <= 1}
                    onClick={() => setLedgerPage((p) => Math.max(1, p - 1))}
                  >
                    <ChevronLeft size={16} /> Trang trước
                  </Button>
                  <span style={{ fontSize: "13px", fontWeight: 600, padding: "0 6px" }}>
                    Trang {ledgerPage} / {totalLedgerPages}
                  </span>
                  <Button
                    variant="secondary"
                    disabled={ledgerPage >= totalLedgerPages}
                    onClick={() => setLedgerPage((p) => Math.min(totalLedgerPages, p + 1))}
                  >
                    Trang sau <ChevronRight size={16} />
                  </Button>
                </div>
              </div>

              <div className="readonly-note">
                <BookOpen size={17} />
                <strong>Sổ cái kho bất biến (Append-Only):</strong> Nghiêm cấm mọi lệnh UPDATE hoặc DELETE trực tiếp lên số lượng tồn. Sai lệch thực tế bắt buộc phải lập chứng từ Kiểm kê cân kho bù trừ (Compensating Transaction).
              </div>
            </div>
          </>
        )}
      </section>

      {receiptListOpen && (
        <Modal title="Phiếu nhập đã lưu" onClose={() => setReceiptListOpen(false)}>
          <GoodsReceiptList tenantId={currentTenantId} onOpen={openSavedReceipt} />
        </Modal>
      )}

      {/* MODAL LẬP PHIẾU NHẬP KHO NHIỀU DÒNG (GOODS RECEIPT DIALOG) */}
      {receiptModal && (
        <Modal
          title={
            confirmedSuccess
              ? "Phiếu nhập kho đã ghi sổ thành công"
              : createdReceiptId ? "Xem lại phiếu nhập nháp" : "Lập phiếu nhập kho nhiều dòng hàng"
          }
          onClose={closeReceiptModal}
        >
          {confirmedSuccess ? (
            <div className="success-panel">
              <CheckCircle2 size={48} color="#16a34a" />
              <h3>Đã ghi sổ kho & tính lại giá vốn WAC</h3>
              <p>Mã phiếu: <strong>{receiptNumber}</strong></p>
              <p>Đã nhập {totalReceiptQuantity} sản phẩm vào kho lưu trữ.</p>
              <div className="cost-result">
                <span>
                  Tổng giá trị phiếu nhập:{" "}
                  <strong>{totalReceiptAmount.toLocaleString("vi-VN")} ₫</strong>
                </span>
                <span>
                  Bút toán: <strong>Đã ghi nhận Append-Only Ledger</strong>
                </span>
              </div>
              <Button
                onClick={() => {
                  setReceiptModal(false);
                  setTab("ledger");
                }}
              >
                Xem sổ kho
              </Button>
            </div>
          ) : (
            <>
              {receiptError && (
                <div
                  style={{
                    background: "#fee2e2",
                    border: "1px solid #ef4444",
                    color: "#b91c1c",
                    padding: "10px",
                    borderRadius: "8px",
                    fontSize: "12px",
                    marginBottom: "12px",
                    display: "flex",
                    alignItems: "center",
                    gap: "8px",
                  }}
                >
                  <AlertCircle size={16} />
                  <span>{receiptError}</span>
                </div>
              )}

              {verifyingPendingReceipt && (
                <div
                  style={{
                    background: "#f0fdf4",
                    border: "1px solid #86efac",
                    color: "#166534",
                    padding: "10px",
                    borderRadius: "8px",
                    fontSize: "12px",
                    marginBottom: "12px",
                    display: "flex",
                    alignItems: "center",
                    gap: "8px",
                  }}
                >
                  <div
                    style={{
                      display: "inline-block",
                      width: "14px",
                      height: "14px",
                      border: "2px solid #86efac",
                      borderTopColor: "#166534",
                      borderRadius: "50%",
                      animation: "spin 0.8s linear infinite",
                    }}
                  />
                  <span>Đang đối soát trạng thái phiếu nhập kho với máy chủ OISM...</span>
                </div>
              )}

              {createdReceiptId && (
                <div
                  style={{
                    background: "#eff6ff",
                    border: "1px solid #3b82f6",
                    color: "#1d4ed8",
                    padding: "12px",
                    borderRadius: "8px",
                    fontSize: "12px",
                    marginBottom: "12px",
                  }}
                >
                  <div style={{ fontWeight: 600, marginBottom: "4px" }}>
                    Phiếu nháp đã lưu — chưa cộng tồn: {receiptNumber || createdReceiptId.slice(0, 8)}
                  </div>
                  <div>
                    Kiểm tra lại kho, nhà cung cấp, số lượng và đơn giá bên dưới. Chỉ khi bấm <strong>"Xác nhận nhập kho"</strong>, hệ thống mới cộng tồn, ghi sổ và tính lại giá vốn. Bạn có thể đóng và mở lại phiếu trong <strong>"Phiếu nhập đã lưu"</strong> để xác nhận sau.
                  </div>
                </div>
              )}

              {modalWhError && (
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
                    <strong>Lỗi tải tồn kho kho tiếp nhận:</strong> Không thể tải số dư của kho đang chọn ({modalWhError}).
                  </span>
                  <button
                    type="button"
                    onClick={() => {
                      if (selectedWarehouse) {
                        loadModalWhStock(selectedWarehouse);
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

              {/* Thông tin chung phiếu nhập (U01-A) */}
              <div className="form-grid">
                <label style={{ gridColumn: "span 2" }}>
                  <span>Đơn mua hàng đã duyệt (Bắt buộc theo U01-A) *</span>
                  {createdReceiptId ? (
                    <div
                      style={{
                        padding: "8px 12px",
                        background: "#f1f5f9",
                        borderRadius: 6,
                        border: "1px solid #cbd5e1",
                        fontSize: 13,
                        fontWeight: 600,
                      }}
                    >
                      {selectedPoDetail?.number || receiptNumber || "Đơn mua đã gắn kết"}
                      {selectedPoDetail?.status && (
                        <span style={{ marginLeft: 8, fontWeight: 400, color: "#64748b" }}>
                          ({selectedPoDetail.status === "Approved" ? "Đã duyệt" : "Đã nhận một phần"})
                        </span>
                      )}
                    </div>
                  ) : (
                    <>
                      <Select
                        value={selectedPurchaseOrderId}
                        onChange={(e) => handleSelectPurchaseOrder(e.target.value)}
                        required
                      >
                        <option value="">-- Chọn đơn mua hàng đã duyệt --</option>
                        {receivablePos.map((po) => (
                          <option key={po.id} value={po.id}>
                            {po.number} — {po.supplierName} ({po.branchName}) [{po.status === "Approved" ? "Đã duyệt" : "Đã nhận một phần"}]
                          </option>
                        ))}
                      </Select>
                      {receivablePos.length === 0 && (
                        <div
                          style={{
                            background: "#fef3c7",
                            border: "1px solid #f59e0b",
                            color: "#92400e",
                            padding: "8px 12px",
                            borderRadius: 6,
                            fontSize: 12,
                            marginTop: 6,
                          }}
                        >
                          Chưa có đơn mua hàng nào đã duyệt. Theo quy định U01-A, mọi phiếu nhập NCC phải lập từ đơn mua đã duyệt.
                          <div style={{ marginTop: 6 }}>
                            <Button
                              variant="secondary"
                              onClick={() => {
                                closeReceiptModal();
                                navigate("/purchase-orders");
                              }}
                            >
                              Đi đến Quản lý Đơn mua hàng
                            </Button>
                          </div>
                        </div>
                      )}
                    </>
                  )}
                </label>

                <label>
                  <span>Nhà cung cấp (Khóa theo đơn mua)</span>
                  <div
                    style={{
                      padding: "8px 12px",
                      background: "#f8fafc",
                      borderRadius: 6,
                      border: "1px solid #e2e8f0",
                      fontSize: 13,
                      fontWeight: 600,
                    }}
                  >
                    {selectedPoDetail?.supplierName ||
                      suppliers.find((s) => s.id === selectedSupplier)?.name ||
                      "Chưa chọn đơn mua"}
                  </div>
                </label>

                <label>
                  <span>Chi nhánh nhận (Khóa theo đơn mua)</span>
                  <div
                    style={{
                      padding: "8px 12px",
                      background: "#f8fafc",
                      borderRadius: 6,
                      border: "1px solid #e2e8f0",
                      fontSize: 13,
                      fontWeight: 600,
                    }}
                  >
                    {selectedPoDetail?.branchName ||
                      branches.find((b) => b.id === selectedBranch)?.name ||
                      "Chưa chọn đơn mua"}
                  </div>
                </label>

                <label>
                  <span>Kho tiếp nhận (Thuộc chi nhánh nhận) *</span>
                  <Select
                    value={selectedWarehouse}
                    disabled={Boolean(createdReceiptId) || receiptBranchWarehouses.length === 0}
                    onChange={(e) => setSelectedWarehouse(e.target.value)}
                    required
                  >
                    {receiptBranchWarehouses.length === 0 ? (
                      <option value="">-- Chi nhánh này chưa có kho --</option>
                    ) : (
                      receiptBranchWarehouses.map((w) => (
                        <option key={w.id} value={w.id}>
                          {w.name}
                        </option>
                      ))
                    )}
                  </Select>
                </label>

                <label>
                  <span>Ghi chú chứng từ</span>
                  <Input
                    placeholder="Ghi chú số hóa đơn NCC hoặc biên bản giao nhận..."
                    value={receiptNotes}
                    disabled={Boolean(createdReceiptId)}
                    onChange={(e) => setReceiptNotes(e.target.value)}
                  />
                </label>
              </div>

              {/* Bảng dòng hàng đa dòng (U01-A) */}
              <div style={{ marginTop: "18px", borderTop: "1px solid #e2e8f0", paddingTop: "14px" }}>
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    marginBottom: "10px",
                  }}
                >
                  <strong>
                    Chi tiết mặt hàng nhập kho ({receiptItems.length} dòng hàng từ đơn mua)
                  </strong>
                </div>

                {receiptItems.length === 0 ? (
                  <div
                    style={{
                      padding: 24,
                      textAlign: "center",
                      background: "#f8fafc",
                      borderRadius: 6,
                      color: "#64748b",
                      fontSize: 13,
                    }}
                  >
                    Vui lòng chọn một Đơn mua hàng đã duyệt ở trên để tải danh sách mặt hàng, đơn vị tính và đơn giá đã chốt.
                  </div>
                ) : (
                  <div
                    style={{
                      display: "flex",
                      flexDirection: "column",
                      gap: "8px",
                      maxHeight: "280px",
                      overflowY: "auto",
                    }}
                  >
                    {receiptItems.map((item, idx) => {
                      const isOver =
                        item.remainingQuantity !== undefined &&
                        item.quantity > item.remainingQuantity;

                      return (
                        <div
                          key={idx}
                          style={{
                            background: isOver ? "#fef2f2" : "#f8fafc",
                            border: isOver ? "1px solid #ef4444" : "1px solid #e2e8f0",
                            borderRadius: "8px",
                            padding: "10px",
                            display: "grid",
                            gridTemplateColumns: "1.8fr 1.1fr 1fr 1.3fr 1.2fr auto",
                            gap: "8px",
                            alignItems: "center",
                          }}
                        >
                          <div>
                            <strong style={{ fontSize: "13px" }}>{item.skuCode}</strong>
                            <div style={{ fontSize: "11px", color: "#64748b" }}>
                              {item.skuName}
                              {modalWarehouseBalances.length > 0 && (
                                <span> · Tồn kho hiện tại: {modalWarehouseBalances.find(s => s.id === item.skuId)?.onHand ?? 0}</span>
                              )}
                            </div>
                          </div>

                          <div>
                            <span style={{ fontSize: "11px", color: "#64748b", display: "block" }}>
                              Đơn vị mua
                            </span>
                            <strong style={{ fontSize: "12px" }}>{item.unitName || "Đơn vị gốc"}</strong>
                            {item.factorSnapshot && item.factorSnapshot > 1 ? (
                              <div style={{ fontSize: "10px", color: "#0284c7" }}>
                                (x{item.factorSnapshot} đơn vị gốc)
                              </div>
                            ) : null}
                          </div>

                          <div>
                            <span style={{ fontSize: "11px", color: "#64748b", display: "block" }}>
                              Đặt / Còn lại
                            </span>
                            <span style={{ fontSize: "12px" }}>
                              {item.orderedQuantity ?? item.quantity} /{" "}
                              <strong style={{ color: "#059669" }}>
                                {item.remainingQuantity ?? item.quantity}
                              </strong>
                            </span>
                          </div>

                          <div>
                            <span style={{ fontSize: "11px", fontWeight: 600, display: "block" }}>
                              Thực nhận đợt này *
                            </span>
                            <Input
                              type="number"
                              min="0"
                              step="any"
                              max={item.remainingQuantity}
                              disabled={Boolean(createdReceiptId)}
                              value={item.quantity}
                              onChange={(e) => {
                                const next = [...receiptItems];
                                next[idx].quantity = Number(e.target.value);
                                setReceiptItems(next);
                              }}
                            />
                            {isOver && (
                              <span style={{ color: "#ef4444", fontSize: "10px" }}>
                                Vượt lượng còn lại!
                              </span>
                            )}
                          </div>

                          <div>
                            <span style={{ fontSize: "11px", color: "#64748b", display: "block" }}>
                              Đơn giá mua (Đã chốt)
                            </span>
                            <div style={{ fontWeight: 600, fontSize: "12px" }}>
                              {item.unitPrice.toLocaleString("vi-VN")} ₫
                            </div>
                            <div style={{ fontSize: "10px", color: "#059669" }}>
                              🔒 Khóa theo PO
                            </div>
                          </div>

                          <Button
                            variant="ghost"
                            disabled={receiptItems.length <= 1 || Boolean(createdReceiptId)}
                            onClick={() => removeReceiptLine(idx)}
                            title="Bỏ dòng khỏi đợt nhận này"
                          >
                            <Trash2 size={16} color="#ef4444" />
                          </Button>
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>

              {/* Tổng kết phiếu */}
              <div
                style={{
                  marginTop: "16px",
                  padding: "12px",
                  background: "#f1f5f9",
                  borderRadius: "8px",
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                }}
              >
                <span>
                  Tổng số lượng thực nhận: <strong>{totalReceiptQuantity}</strong> (theo đơn vị mua)
                </span>
                <span style={{ fontSize: "15px" }}>
                  Tổng giá trị phiếu nhập:{" "}
                  <strong style={{ color: "#16a34a" }}>
                    {totalReceiptAmount.toLocaleString("vi-VN")} ₫
                  </strong>
                </span>
              </div>

              {/* Tác động WAC */}
              <div className="impact-box" style={{ marginTop: "12px" }}>
                <strong>Cơ chế đối chiếu giá vốn WAC từ Đơn mua hàng (U01-A):</strong>
                <p>
                  Đơn giá nhập được <strong>khóa chặt theo Đơn mua hàng đã được duyệt</strong>, đảm bảo tính bất biến và chuẩn xác của giá vốn bình quân (WAC) khi tăng tồn kho và ghi nhận sổ cái IN.
                </p>
              </div>

              <div className="modal-actions">
                <Button variant="secondary" disabled={loading} onClick={closeReceiptModal}>
                  Đóng
                </Button>
                {createdReceiptId && (
                  <>
                    <Button
                      variant="danger"
                      onClick={handleCancelDraftReceipt}
                      disabled={loading || verifyingPendingReceipt}
                      title="Hủy hoàn toàn phiếu nhập nháp này trên máy chủ OISM"
                    >
                      Hủy phiếu nháp này
                    </Button>
                    <Button
                      variant="secondary"
                      onClick={handleDiscardPendingReceipt}
                      disabled={loading || verifyingPendingReceipt}
                      title="Bỏ theo dõi phiếu này trên giao diện và tạo phiếu mới (phiếu cũ vẫn lưu ở trạng thái Draft trên máy chủ)"
                    >
                      Tạo phiếu khác
                    </Button>
                  </>
                )}
                <Button
                  onClick={createdReceiptId ? handleConfirmReceipt : handleSaveReceipt}
                  disabled={
                    loading ||
                    verifyingPendingReceipt ||
                    (createdReceiptId
                      ? !receiptReady
                      : !selectedPurchaseOrderId || receiptItems.length === 0 || loadingModalWhStock || Boolean(modalWhError))
                  }
                >
                  {loading
                    ? "Đang xử lý..."
                    : verifyingPendingReceipt
                    ? "Đang đối soát..."
                    : createdReceiptId
                    ? "Xác nhận nhập kho"
                    : "Lưu nháp"}
                </Button>
              </div>
            </>
          )}
        </Modal>
      )}

      {/* [A08] MODAL GIỮ CHỖ TỒN KHO THEO CHI NHÁNH & KHO CỤ THỂ */}
      {reserveModalOpen && (
        <Modal
          title="Giữ chỗ tồn kho theo Chi nhánh & Kho cụ thể (Advisory Lock)"
          onClose={() => setReserveModalOpen(false)}
        >
          {reserveError && (
            <div
              style={{
                background: "#fee2e2",
                border: "1px solid #ef4444",
                color: "#b91c1c",
                padding: "10px",
                borderRadius: "8px",
                fontSize: "12px",
                marginBottom: "12px",
                display: "flex",
                alignItems: "center",
                gap: "8px",
              }}
            >
              <AlertCircle size={16} />
              <span>{reserveError}</span>
            </div>
          )}
          {reserveSuccess && (
            <div
              style={{
                background: "#dcfce7",
                border: "1px solid #22c55e",
                color: "#15803d",
                padding: "10px",
                borderRadius: "8px",
                fontSize: "12px",
                marginBottom: "12px",
              }}
            >
              {reserveSuccess}
            </div>
          )}

          {reserveIdempotencyKey && (
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
              <strong>Định danh giao dịch (F2 - Idempotency Key):</strong> <code>{reserveIdempotencyKey}</code>
              {isRetryingReserve ? (
                <div style={{ marginTop: "4px", color: "#b45309" }}>
                  Thao tác giữ chỗ trước đó chưa hoàn tất hoặc gặp lỗi mạng. Khi bấm <strong>"Thử lại thao tác giữ chỗ"</strong>, hệ thống sẽ gửi cùng định danh UUID và payload để tránh tạo thêm giữ chỗ ngoài ý muốn.
                </div>
              ) : (
                <div style={{ marginTop: "2px", color: "#64748b" }}>
                  Mã UUID chuẩn được gán tự động cho ý định giữ chỗ này.
                </div>
              )}
            </div>
          )}

          {reserveWhError && (
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
                <strong>Lỗi tải tồn kho kho giữ chỗ:</strong> Không thể tải số dư của kho đang chọn ({reserveWhError}).
              </span>
              <button
                type="button"
                onClick={() => {
                  if (reserveWarehouseId) {
                    loadReserveWhStock(reserveWarehouseId);
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
              <span>Chọn Chi nhánh *</span>
              <Select
                disabled={isRetryingReserve}
                value={reserveBranchId}
                onChange={(e) => {
                  const nextBr = e.target.value;
                  setReserveBranchId(nextBr);
                  const matchingWhs = warehouses.filter((w) => w.branchId === nextBr);
                  setReserveWarehouseId(matchingWhs[0]?.id || "");
                  handleReserveFormChange();
                }}
              >
                {branches.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </Select>
            </label>
            <label>
              <span>Chọn Kho giữ chỗ (Thuộc chi nhánh) *</span>
              <Select
                disabled={isRetryingReserve}
                value={reserveWarehouseId}
                onChange={(e) => {
                  setReserveWarehouseId(e.target.value);
                  handleReserveFormChange();
                }}
              >
                {reserveBranchWarehouses.map((w) => (
                  <option key={w.id} value={w.id}>
                    {w.name}
                  </option>
                ))}
              </Select>
            </label>
            <label>
              <span>Chọn Sản phẩm / SKU *</span>
              <Select
                disabled={isRetryingReserve}
                value={reserveSkuId}
                onChange={(e) => {
                  setReserveSkuId(e.target.value);
                  handleReserveFormChange();
                }}
              >
                {(availableSkus.length > 0 ? availableSkus : skus).map((s) => (
                  <option key={s.id} value={s.id} disabled={s.isActive === false}>
                    {s.code} - {s.name} {s.isActive === false ? "[Ngừng hoạt động]" : ""}
                  </option>
                ))}
              </Select>
            </label>
            <label>
              <span>Số lượng giữ chỗ *</span>
              <Input
                disabled={isRetryingReserve}
                type="number"
                min="1"
                value={reserveQty}
                onChange={(e) => {
                  setReserveQty(Math.max(1, Number(e.target.value)));
                  handleReserveFormChange();
                }}
              />
            </label>
          </div>

          {/* Bảng thông tin tồn kho thực tế tại đúng Kho đang chọn */}
          {(() => {
            const whBal = reserveWhBalances.find((b) => b.id === reserveSkuId);
            const onHand = whBal?.onHand ?? 0;
            const reserved = whBal?.reserved ?? 0;
            const available = Math.max(0, onHand - reserved);
            const wac = whBal?.averageCost ?? 0;
            return (
              <div
                style={{
                  marginTop: "14px",
                  padding: "12px 14px",
                  background: "#f8fafc",
                  border: "1px solid #cbd5e1",
                  borderRadius: "8px",
                  fontSize: "13px",
                }}
              >
                <strong style={{ display: "block", marginBottom: "6px", color: "#0f172a" }}>
                  Số dư thực tế tại Kho đang chọn ({loadingReserveWh ? "Đang kiểm tra..." : "Đã đồng bộ"}):
                </strong>
                <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: "8px" }}>
                  <div>
                    <span style={{ fontSize: "11px", color: "#64748b", display: "block" }}>
                      Tồn thực tế (OnHand)
                    </span>
                    <strong>{onHand}</strong>
                  </div>
                  <div>
                    <span style={{ fontSize: "11px", color: "#64748b", display: "block" }}>
                      Đang giữ chỗ (Reserved)
                    </span>
                    <strong style={{ color: "#d97706" }}>{reserved}</strong>
                  </div>
                  <div>
                    <span style={{ fontSize: "11px", color: "#64748b", display: "block" }}>
                      Khả dụng tại kho này
                    </span>
                    <strong style={{ color: available >= reserveQty ? "#16a34a" : "#dc2626" }}>
                      {available}
                    </strong>
                  </div>
                  <div>
                    <span style={{ fontSize: "11px", color: "#64748b", display: "block" }}>
                      Giá vốn WAC (Chi nhánh)
                    </span>
                    <strong>{wac.toLocaleString("vi-VN")} ₫</strong>
                  </div>
                </div>
              </div>
            );
          })()}

          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setReserveModalOpen(false)}>
              Đóng
            </Button>
            {isRetryingReserve && (
              <Button
                variant="secondary"
                onClick={handleDiscardPendingReservation}
                disabled={loading || loadingReserveWh}
                style={{ color: "#dc2626", borderColor: "#fca5a5" }}
              >
                Bỏ thao tác dở dang / Tạo mới
              </Button>
            )}
            <Button onClick={handleReserveStock} disabled={loading || loadingReserveWh || Boolean(reserveWhError)}>
              {loading
                ? "Đang khóa tồn..."
                : isRetryingReserve
                ? "Thử lại thao tác giữ chỗ (Gửi cùng định danh)"
                : "Xác nhận Giữ chỗ tại Kho này"}
            </Button>
          </div>
        </Modal>
      )}

      {/* MODAL CẤU HÌNH NGƯỠNG CẢNH BÁO TỒN KHO AN TOÀN (CS-03) */}
      {reorderModalSku && (
        <Modal
          title={`Cấu hình ngưỡng cảnh báo tồn kho: ${reorderModalSku.code}`}
          onClose={() => setReorderModalSku(null)}
        >
          {reorderError && (
            <div style={{ background: "#fee2e2", border: "1px solid #ef4444", color: "#b91c1c", padding: "10px", borderRadius: "8px", fontSize: "12px", marginBottom: "12px" }}>
              {reorderError}
            </div>
          )}
          <div className="form-grid">
            <label className="full-field">
              <span>Sản phẩm</span>
              <Input value={`${reorderModalSku.code} - ${reorderModalSku.name}`} disabled />
            </label>
            <label>
              <span>Chi nhánh áp dụng *</span>
              <Select value={reorderBranchId} onChange={(e) => setReorderBranchId(e.target.value)}>
                {branches.map((b) => (
                  <option key={b.id} value={b.id}>{b.name}</option>
                ))}
              </Select>
            </label>
            <label>
              <span>Ngưỡng cảnh báo tồn (ReorderPoint) *</span>
              <Input
                type="number"
                min="0"
                step="1"
                value={reorderInputVal}
                onChange={(e) => setReorderInputVal(Math.max(0, parseInt(e.target.value) || 0))}
                required
              />
            </label>
          </div>
          <p style={{ fontSize: "12px", color: "#64748b", margin: "10px 0 16px" }}>
            Khi số lượng tồn khả dụng (Available) tại chi nhánh nhỏ hơn hoặc bằng ngưỡng này, hệ thống sẽ kích hoạt trạng thái cảnh báo và đưa vào danh sách đề xuất nhập hàng.
          </p>
          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setReorderModalSku(null)}>Hủy</Button>
            <Button onClick={handleSaveReorderPoint} disabled={savingReorder}>
              {savingReorder ? "Đang lưu..." : "Lưu ngưỡng cảnh báo"}
            </Button>
          </div>
        </Modal>
      )}

      {/* MODAL XEM CHI TIẾT CHỨNG TỪ SỔ KHO BẤT BIẾN */}
      {selectedLedger && (
        <Modal
          title={`Chứng từ sổ kho: ${selectedLedger.reference}`}
          onClose={() => setSelectedLedger(null)}
        >
          <div className="document-detail">
            <Badge tone={selectedLedger.direction === "IN" ? "success" : "danger"}>
              Bút toán {selectedLedger.direction}
            </Badge>
            <h3>{selectedLedger.transaction}</h3>
            <p>
              Đã ghi sổ lúc {selectedLedger.createdAt} tại {selectedLedger.warehouse}.
            </p>
            <dl>
              <div>
                <dt>Mã SKU</dt>
                <dd>
                  <strong>{selectedLedger.sku}</strong>
                </dd>
              </div>
              <div>
                <dt>Số lượng biến động</dt>
                <dd>
                  <strong>
                    {selectedLedger.direction === "IN" ? "+" : "−"}
                    {selectedLedger.quantity}
                  </strong>
                </dd>
              </div>
              <div>
                <dt>Số dư tồn sau bút toán</dt>
                <dd>
                  <strong>{selectedLedger.balanceAfter}</strong>
                </dd>
              </div>
            </dl>
            <div className="readonly-note">
              <BookOpen size={16} />Bút toán đã chốt vào sổ cái là bất biến và chỉ đọc.
            </div>
          </div>
        </Modal>
      )}

    </>
  );
}
