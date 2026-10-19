import {
  AlertCircle,
  ArrowDownToLine,
  ArrowRightLeft,
  BookOpen,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  ClipboardCheck,
  Lock,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
  Settings2,
  Trash2,
  TriangleAlert,
} from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Badge, Button, Input, Modal, PageTitle, Select } from "../components/ui";
import { apiAdapter } from "../services/apiAdapter";
import type { LedgerEntry, Sku } from "../types/domain";

export function InventoryPage() {
  const [tab, setTab] = useState<"balance" | "ledger">("balance");
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

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
  const [confirmedSuccess, setConfirmedSuccess] = useState(false);
  const [thresholdOpen, setThresholdOpen] = useState(false);
  const [returnOpen, setReturnOpen] = useState(false);
  const [selectedLedger, setSelectedLedger] = useState<LedgerEntry | null>(null);

  // [A08] Modal Giữ chỗ tồn kho (Reserve) hiển thị rõ tồn theo từng Chi nhánh / Kho cụ thể
  const [reserveModalOpen, setReserveModalOpen] = useState(false);
  const [reserveBranchId, setReserveBranchId] = useState("");
  const [reserveWarehouseId, setReserveWarehouseId] = useState("");
  const [reserveSkuId, setReserveSkuId] = useState("");
  const [reserveQty, setReserveQty] = useState(1);
  const [reserveError, setReserveError] = useState<string | null>(null);
  const [reserveSuccess, setReserveSuccess] = useState<string | null>(null);
  const [reserveWhBalances, setReserveWhBalances] = useState<Sku[]>([]);
  const [loadingReserveWh, setLoadingReserveWh] = useState(false);

  // Bộ lọc Bảng Tồn kho & Sổ kho
  const [searchQuery, setSearchQuery] = useState("");
  const [branchFilter, setBranchFilter] = useState("all");
  const [balanceWarehouseFilter, setBalanceWarehouseFilter] = useState("all");
  const [stockStatusFilter, setStockStatusFilter] = useState("all");
  const [warehouseFilter, setWarehouseFilter] = useState("all");
  const [operationFilter, setOperationFilter] = useState("all");
  const [directionFilter, setDirectionFilter] = useState("all");

  // [A05] Phân trang (Pagination) cho Bảng Tồn kho & Sổ kho
  const [balancePage, setBalancePage] = useState(1);
  const [balancePageSize, setBalancePageSize] = useState(20);
  const [ledgerPage, setLedgerPage] = useState(1);
  const [ledgerPageSize, setLedgerPageSize] = useState(20);

  // Form Phiếu nhập kho nhiều dòng
  const [selectedSupplier, setSelectedSupplier] = useState("");
  const [quickSupplierOpen, setQuickSupplierOpen] = useState(false);
  const [newSupplierName, setNewSupplierName] = useState("");
  const [selectedBranch, setSelectedBranch] = useState("");
  const [selectedWarehouse, setSelectedWarehouse] = useState("");
  const [receiptNotes, setReceiptNotes] = useState("");
  const [receiptItems, setReceiptItems] = useState<
    Array<{
      skuId: string;
      skuCode: string;
      skuName: string;
      quantity: number;
      unitPrice: number;
    }>
  >([]);
  const [receiptError, setReceiptError] = useState<string | null>(null);
  // [A07] Lưu ID phiếu nhập nháp vừa tạo để khi Retry lỗi mạng chỉ gọi Confirm, không tạo trùng phiếu mới
  const [createdReceiptId, setCreatedReceiptId] = useState<string | null>(null);
  const [modalWarehouseBalances, setModalWarehouseBalances] = useState<Sku[]>([]);

  // Cấu hình ngưỡng cảnh báo
  const [thresholds, setThresholds] = useState<Record<string, number>>({});

  const loadData = async () => {
    setLoading(true);
    setLoadError(null);
    const [bRes, lRes, sRes, brRes, wRes, pRes] = await Promise.all([
      apiAdapter.getBalances(),
      apiAdapter.getLedger(),
      apiAdapter.getSuppliers(),
      apiAdapter.getBranches(),
      apiAdapter.getWarehouses(),
      apiAdapter.getSkus(),
    ]);

    // [A09] Kiểm tra lỗi kết nối / API để báo rõ lên UI thay vì hiển thị 0
    if (bRes.error || pRes.error) {
      setLoadError(bRes.error?.message || pRes.error?.message || "Không thể tải dữ liệu kho từ máy chủ.");
    }

    if (bRes.data) {
      setSkus(bRes.data);
    }
    if (pRes.data) {
      setAvailableSkus(pRes.data);
    }
    if (lRes.data) {
      setLedger(lRes.data);
    }
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

    setLoading(false);
  };

  useEffect(() => {
    loadData();
  }, []);

  // Khi lọc theo Kho cụ thể trên Bảng Tồn kho, nạp số dư chi tiết của kho đó
  useEffect(() => {
    async function loadSpecificWarehouseBalances() {
      if (balanceWarehouseFilter === "all") {
        setWarehouseBalances([]);
        return;
      }
      const res = await apiAdapter.getBalances({
        branchId: branchFilter !== "all" ? branchFilter : undefined,
        warehouseId: balanceWarehouseFilter,
      });
      if (res.data) {
        setWarehouseBalances(res.data);
      }
    }
    loadSpecificWarehouseBalances();
  }, [branchFilter, balanceWarehouseFilter]);

  // [A03] Khi mở Modal Nhập kho và chọn Chi nhánh / Kho, tải số dư thực tế tại đúng Kho đó để mô phỏng WAC chuẩn xác
  useEffect(() => {
    async function loadModalWhStock() {
      if (!receiptModal || !selectedBranch || !selectedWarehouse) return;
      const res = await apiAdapter.getBalances({
        branchId: selectedBranch,
        warehouseId: selectedWarehouse,
      });
      if (res.data) {
        setModalWarehouseBalances(res.data);
      }
    }
    loadModalWhStock();
  }, [receiptModal, selectedBranch, selectedWarehouse]);

  // [A08] Khi mở Modal Giữ chỗ (Reserve), tải số dư khả dụng của đúng Chi nhánh & Kho được chọn
  useEffect(() => {
    async function loadReserveWhStock() {
      if (!reserveModalOpen || !reserveBranchId || !reserveWarehouseId) {
        setReserveWhBalances([]);
        return;
      }
      setLoadingReserveWh(true);
      const res = await apiAdapter.getBalances({
        branchId: reserveBranchId,
        warehouseId: reserveWarehouseId,
      });
      setReserveWhBalances(res.data || []);
      setLoadingReserveWh(false);
    }
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

  const filteredLedger = useMemo(
    () =>
      ledger.filter(
        (item) =>
          (warehouseFilter === "all" || item.warehouse.includes(warehouseFilter)) &&
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

  // Thêm dòng sản phẩm mới vào phiếu nhập
  const addReceiptLine = () => {
    if (createdReceiptId) return; // Đã tạo phiếu nháp, đang chờ retry confirm
    const candidateList = (availableSkus.length > 0 ? availableSkus : skus).filter(
      (s) => s.isActive !== false
    );
    const candidate =
      candidateList[receiptItems.length % Math.max(1, candidateList.length)] || candidateList[0];
    if (!candidate) {
      setReceiptError(
        "Chưa có SKU đang hoạt động nào trong hệ thống. Vui lòng sang mục Sản phẩm & biến thể để tạo SKU trước khi lập phiếu nhập."
      );
      return;
    }
    const branchMatch = skus.find((s) => s.id === candidate.id && s.branchId === selectedBranch);
    const defaultPrice =
      branchMatch && branchMatch.averageCost > 0
        ? branchMatch.averageCost
        : candidate.averageCost > 0
        ? candidate.averageCost
        : 100000;

    setReceiptItems([
      ...receiptItems,
      {
        skuId: candidate.id,
        skuCode: candidate.code,
        skuName: candidate.name,
        quantity: 5,
        unitPrice: defaultPrice,
      },
    ]);
  };

  const openReceiptModal = () => {
    setReceiptModal(true);
    setConfirmedSuccess(false);
    setReceiptError(null);
    setCreatedReceiptId(null);

    const defaultBr = selectedBranch || branches[0]?.id || "";
    if (defaultBr) {
      setSelectedBranch(defaultBr);
      const validWhs = warehouses.filter((w) => w.branchId === defaultBr);
      if (!selectedWarehouse || !validWhs.some((w) => w.id === selectedWarehouse)) {
        setSelectedWarehouse(validWhs[0]?.id || "");
      }
    }

    if (!selectedSupplier && suppliers[0]?.id) {
      setSelectedSupplier(suppliers[0].id);
    }
    if (receiptItems.length === 0) {
      const activeCandidates = (availableSkus.length > 0 ? availableSkus : skus).filter(
        (s) => s.isActive !== false
      );
      const candidate = activeCandidates[0];
      if (candidate) {
        const branchMatch = skus.find((s) => s.id === candidate.id && s.branchId === defaultBr);
        setReceiptItems([
          {
            skuId: candidate.id,
            skuCode: candidate.code,
            skuName: candidate.name,
            quantity: 10,
            unitPrice:
              branchMatch && branchMatch.averageCost > 0
                ? branchMatch.averageCost
                : candidate.averageCost > 0
                ? candidate.averageCost
                : 120000,
          },
        ]);
      }
    }
  };

  const handleQuickCreateSupplier = async () => {
    if (!newSupplierName.trim()) return;
    setLoading(true);
    const res = await apiAdapter.createSupplier({
      name: newSupplierName.trim(),
      code: `SUP-${Date.now().toString().slice(-4)}`,
    });
    setLoading(false);
    if (res.error) {
      setReceiptError(res.error.message);
    } else if (res.data) {
      const nextSuppliers = [...suppliers, { id: res.data.id, name: res.data.name }];
      setSuppliers(nextSuppliers);
      setSelectedSupplier(res.data.id);
      setNewSupplierName("");
      setQuickSupplierOpen(false);
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

  // [A07] Xử lý tạo và xác nhận phiếu nhập kho an toàn: Lưu createdReceiptId để khi Retry chỉ gọi Confirm
  const handleConfirmReceipt = async () => {
    setReceiptError(null);
    if (!selectedBranch || !selectedWarehouse) {
      setReceiptError("Vui lòng chọn Chi nhánh và Kho hàng tiếp nhận thuộc chi nhánh đó.");
      return;
    }
    // Kiểm tra Kho phải thuộc đúng Chi nhánh đã chọn [A04]
    const validWh = warehouses.find((w) => w.id === selectedWarehouse && w.branchId === selectedBranch);
    if (!validWh) {
      setReceiptError("Kho tiếp nhận không thuộc Chi nhánh đã chọn. Vui lòng chọn lại kho hợp lệ.");
      return;
    }
    if (!selectedSupplier) {
      setReceiptError(
        "Vui lòng chọn Nhà cung cấp (hoặc bấm '+ Tạo nhanh NCC' nếu cửa hàng chưa có nhà cung cấp)."
      );
      return;
    }
    if (receiptItems.length === 0) {
      setReceiptError("Vui lòng thêm ít nhất một dòng mặt hàng.");
      return;
    }
    if (
      receiptItems.some(
        (item) => !item.skuId || Number(item.quantity) <= 0 || Number(item.unitPrice) < 0
      )
    ) {
      setReceiptError(
        "Vui lòng kiểm tra lại SKU, số lượng (> 0) và đơn giá nhập (≥ 0) trên từng dòng hàng."
      );
      return;
    }

    setLoading(true);
    let receiptIdToConfirm = createdReceiptId;

    try {
      // Bước 1: Chỉ gọi API tạo mới nếu chưa có createdReceiptId (tránh tạo trùng phiếu khi Retry [A07])
      if (!receiptIdToConfirm) {
        const payload = {
          branchId: selectedBranch,
          warehouseId: selectedWarehouse,
          supplierId: selectedSupplier || undefined,
          notes: receiptNotes,
          items: receiptItems.map((item) => ({
            skuId: item.skuId,
            quantity: Number(item.quantity) || 1,
            unitPrice: Number(item.unitPrice) || 0,
          })),
        };

        const res = await apiAdapter.createGoodsReceipt(payload);

        if (res.error || !res.data?.id) {
          setLoading(false);
          setReceiptError(res.error?.message || "Không thể tạo phiếu nhập kho.");
          return;
        }

        receiptIdToConfirm = res.data.id;
        setCreatedReceiptId(receiptIdToConfirm);
      }

      // Bước 2: Xác nhận và ghi sổ kho nguyên tử (Atomic Confirm)
      if (receiptIdToConfirm) {
        const confirmRes = await apiAdapter.confirmGoodsReceipt(receiptIdToConfirm);
        if (confirmRes.error) {
          // Trường hợp timeout ở lượt gọi trước nhưng backend đã kịp xác nhận
          if (confirmRes.error.message?.includes("đã được xác nhận")) {
            setLoading(false);
            setCreatedReceiptId(null);
            setConfirmedSuccess(true);
            await loadData();
            return;
          }

          setLoading(false);
          setReceiptError(
            `${confirmRes.error.message}. Phiếu nháp đã được lưu (ID: ${receiptIdToConfirm.slice(
              0,
              8
            )}) — Bấm "Thử lại Xác nhận & Ghi sổ kho" để xác nhận tiếp mà không sợ tạo trùng phiếu.`
          );
          return;
        }
      }

      setLoading(false);
      setCreatedReceiptId(null);
      setConfirmedSuccess(true);
      await loadData();
    } catch (err: any) {
      setLoading(false);
      setReceiptError(err?.message || "Đã xảy ra lỗi khi xử lý phiếu nhập kho. Vui lòng bấm thử lại.");
    }
  };

  // [A08] Mở modal Giữ chỗ tồn kho (Reserve) cho 1 SKU tại đúng Chi nhánh / Kho
  const openReserveModal = (sku?: Sku) => {
    setReserveError(null);
    setReserveSuccess(null);
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
    setReserveModalOpen(true);
  };

  const handleReserveStock = async () => {
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
    if (reserveQty > whAvailable) {
      setReserveError(
        `Kho đang chọn chỉ có ${whAvailable} sản phẩm khả dụng (Yêu cầu: ${reserveQty}). Vui lòng chọn kho khác hoặc giảm số lượng.`
      );
      return;
    }

    setLoading(true);
    const res = await apiAdapter.reserveStock({
      branchId: reserveBranchId,
      warehouseId: reserveWarehouseId,
      skuId: reserveSkuId,
      quantity: reserveQty,
      durationMinutes: 15,
    });
    setLoading(false);

    if (res.error) {
      setReserveError(res.error.message);
      return;
    }

    setReserveSuccess(
      `Đã khóa giữ chỗ thành công ${reserveQty} SP tại kho đã chọn (Hiệu lực 15 phút - Advisory Lock).`
    );
    await loadData();
    // Nạp lại tồn kho của kho vừa giữ chỗ
    const updatedWh = await apiAdapter.getBalances({
      branchId: reserveBranchId,
      warehouseId: reserveWarehouseId,
    });
    if (updatedWh.data) setReserveWhBalances(updatedWh.data);
  };

  return (
    <>
      <PageTitle
        eyebrow="FR-INV · FR-COST"
        title="Kho & sổ kho"
        description="Theo dõi tồn thực tế, tồn giữ chỗ, thực bán khả dụng theo từng Chi nhánh / Kho và bút toán sổ cái bất biến."
        actions={
          <>
            <Button variant="secondary" onClick={() => setReturnOpen(true)}>
              <RotateCcw size={17} />Ghi nhận trả hàng <Badge tone="neutral">[Sprint 3 Preview]</Badge>
            </Button>
            <Button variant="secondary" onClick={() => openReserveModal()}>
              <Lock size={17} />Giữ chỗ theo kho
            </Button>
            <Button onClick={openReceiptModal}>
              <Plus size={17} />Tạo phiếu nhập
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
          <strong>Chuyển kho <Badge tone="neutral">[Sprint 3 Preview]</Badge></strong>
          <small>2 bước nội bộ</small>
        </div>
        <div>
          <span><ClipboardCheck size={18} /></span>
          <strong>Kiểm kê <Badge tone="neutral">[Sprint 3 Preview]</Badge></strong>
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
          <Button variant="secondary" onClick={() => setThresholdOpen(true)}>
            <Settings2 size={16} />Cấu hình ngưỡng
          </Button>
        </section>
      )}

      <section className="card">
        <div className="tabs">
          <button className={tab === "balance" ? "active" : ""} onClick={() => setTab("balance")}>
            Bảng Tồn kho ({filteredSkus.length})
          </button>
          <button className={tab === "ledger" ? "active" : ""} onClick={() => setTab("ledger")}>
            Sổ kho bất biến — Append-Only ({filteredLedger.length})
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
                                onClick={() => setThresholdOpen(true)}
                                title="Ngưỡng cảnh báo"
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
              <Select
                value={warehouseFilter}
                onChange={(e) => {
                  setWarehouseFilter(e.target.value);
                  setLedgerPage(1);
                }}
              >
                <option value="all">Tất cả kho hàng</option>
                {warehouses.map((w) => (
                  <option key={w.id} value={w.name}>
                    {w.name}
                  </option>
                ))}
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
                  {loading && ledger.length === 0 ? (
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

      {/* MODAL LẬP PHIẾU NHẬP KHO NHIỀU DÒNG (GOODS RECEIPT DIALOG) */}
      {receiptModal && (
        <Modal
          title={
            confirmedSuccess
              ? "Phiếu nhập kho đã ghi sổ thành công"
              : "Lập phiếu nhập kho nhiều dòng hàng"
          }
          onClose={() => setReceiptModal(false)}
        >
          {confirmedSuccess ? (
            <div className="success-panel">
              <CheckCircle2 size={48} color="#16a34a" />
              <h3>Đã ghi sổ kho & tính lại giá vốn WAC</h3>
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

              {createdReceiptId && (
                <div
                  style={{
                    background: "#eff6ff",
                    border: "1px solid #3b82f6",
                    color: "#1d4ed8",
                    padding: "10px",
                    borderRadius: "8px",
                    fontSize: "12px",
                    marginBottom: "12px",
                  }}
                >
                  <strong>Bảo vệ chống trùng phiếu nhập (Idempotent Retry):</strong> Phiếu nháp{" "}
                  <code>{createdReceiptId.slice(0, 8)}</code> đã được khởi tạo. Khi bấm nút bên dưới, hệ thống chỉ gửi lệnh <strong>Xác nhận & Ghi sổ</strong> cho phiếu này, không tạo lặp phiếu thứ 2.
                </div>
              )}

              {/* Thông tin chung phiếu nhập */}
              <div className="form-grid">
                <label>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    <span>Nhà cung cấp *</span>
                    {!createdReceiptId && (
                      <button
                        type="button"
                        onClick={() => setQuickSupplierOpen(!quickSupplierOpen)}
                        style={{
                          background: "none",
                          border: "none",
                          color: "#1769e0",
                          fontSize: "11px",
                          fontWeight: 600,
                          cursor: "pointer",
                        }}
                      >
                        {quickSupplierOpen ? "Đóng" : "+ Tạo nhanh NCC"}
                      </button>
                    )}
                  </div>
                  {quickSupplierOpen ? (
                    <div className="inline-input">
                      <Input
                        placeholder="Tên NCC mới..."
                        value={newSupplierName}
                        onChange={(e) => setNewSupplierName(e.target.value)}
                      />
                      <Button variant="secondary" onClick={handleQuickCreateSupplier} disabled={loading}>
                        Lưu
                      </Button>
                    </div>
                  ) : (
                    <Select
                      value={selectedSupplier}
                      disabled={Boolean(createdReceiptId)}
                      onChange={(e) => setSelectedSupplier(e.target.value)}
                    >
                      <option value="">-- Chọn Nhà cung cấp --</option>
                      {suppliers.map((s) => (
                        <option key={s.id} value={s.id}>
                          {s.name}
                        </option>
                      ))}
                    </Select>
                  )}
                </label>

                {/* [A04] Chọn Chi nhánh tự động lọc dropdown Kho hàng của đúng chi nhánh đó */}
                <label>
                  <span>Chi nhánh nhận *</span>
                  <Select
                    value={selectedBranch}
                    disabled={Boolean(createdReceiptId)}
                    onChange={(e) => {
                      const nextBranch = e.target.value;
                      setSelectedBranch(nextBranch);
                      const branchWhs = warehouses.filter((w) => w.branchId === nextBranch);
                      setSelectedWarehouse(branchWhs[0]?.id || "");
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
                  <span>Kho tiếp nhận (Thuộc chi nhánh chọn) *</span>
                  <Select
                    value={selectedWarehouse}
                    disabled={Boolean(createdReceiptId) || receiptBranchWarehouses.length === 0}
                    onChange={(e) => setSelectedWarehouse(e.target.value)}
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
                    placeholder="Nhập số hóa đơn NCC hoặc ghi chú..."
                    value={receiptNotes}
                    disabled={Boolean(createdReceiptId)}
                    onChange={(e) => setReceiptNotes(e.target.value)}
                  />
                </label>
              </div>

              {/* Bảng dòng hàng đa dòng */}
              <div style={{ marginTop: "18px", borderTop: "1px solid #e2e8f0", paddingTop: "14px" }}>
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    marginBottom: "10px",
                  }}
                >
                  <strong>Chi tiết mặt hàng nhập kho ({receiptItems.length} dòng)</strong>
                  {!createdReceiptId && (
                    <Button variant="secondary" onClick={addReceiptLine}>
                      <Plus size={15} />Thêm dòng sản phẩm
                    </Button>
                  )}
                </div>

                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: "8px",
                    maxHeight: "250px",
                    overflowY: "auto",
                  }}
                >
                  {receiptItems.map((item, idx) => {
                    const candidateList = availableSkus.length > 0 ? availableSkus : skus;
                    // [A03] Lấy số dư OnHand và giá vốn WAC thực tế tại đúng Chi nhánh & Kho được chọn ở Header phiếu nhập
                    const branchSpecBalance = skus.find(
                      (s) => s.id === item.skuId && s.branchId === selectedBranch
                    );
                    const whSpecBalance = modalWarehouseBalances.find((s) => s.id === item.skuId);

                    const oldBranchOnHand = branchSpecBalance?.onHand ?? 0;
                    const oldWarehouseOnHand = whSpecBalance?.onHand ?? oldBranchOnHand;
                    const oldBranchWac = branchSpecBalance?.averageCost ?? 0;

                    const inQty = Math.max(0, Number(item.quantity) || 0);
                    const inPrice = Math.max(0, Number(item.unitPrice) || 0);
                    const projectedWac =
                      oldBranchOnHand + inQty > 0
                        ? Math.round(
                            (oldBranchOnHand * oldBranchWac + inQty * inPrice) /
                              (oldBranchOnHand + inQty)
                          )
                        : inPrice;

                    return (
                      <div
                        key={idx}
                        style={{
                          background: "#f8fafc",
                          border: "1px solid #e2e8f0",
                          borderRadius: "8px",
                          padding: "10px",
                          display: "grid",
                          gridTemplateColumns: "1.9fr 0.8fr 1.1fr 1.5fr auto",
                          gap: "8px",
                          alignItems: "end",
                        }}
                      >
                        <label>
                          <span style={{ fontSize: "11px" }}>Chọn SKU / Sản phẩm *</span>
                          <Select
                            value={item.skuId}
                            disabled={Boolean(createdReceiptId)}
                            onChange={(e) => {
                              const found = candidateList.find((s) => s.id === e.target.value);
                              const next = [...receiptItems];
                              next[idx].skuId = e.target.value;
                              if (found) {
                                next[idx].skuCode = found.code;
                                next[idx].skuName = found.name;
                              }
                              setReceiptItems(next);
                            }}
                          >
                            {candidateList.map((s) => (
                              <option key={s.id} value={s.id} disabled={s.isActive === false}>
                                {s.code} - {s.name} {s.isActive === false ? "[Ngừng hoạt động]" : ""}
                              </option>
                            ))}
                          </Select>
                        </label>
                        <label>
                          <span style={{ fontSize: "11px" }}>Số lượng *</span>
                          <Input
                            type="number"
                            min="1"
                            disabled={Boolean(createdReceiptId)}
                            value={item.quantity}
                            onChange={(e) => {
                              const next = [...receiptItems];
                              next[idx].quantity = Math.max(1, Number(e.target.value));
                              setReceiptItems(next);
                            }}
                          />
                        </label>
                        <label>
                          <span style={{ fontSize: "11px" }}>Đơn giá nhập (VNĐ)</span>
                          <Input
                            type="number"
                            min="0"
                            disabled={Boolean(createdReceiptId)}
                            value={item.unitPrice}
                            onChange={(e) => {
                              const next = [...receiptItems];
                              next[idx].unitPrice = Number(e.target.value);
                              setReceiptItems(next);
                            }}
                          />
                        </label>
                        <div>
                          <span style={{ fontSize: "11px", color: "#64748b" }}>
                            Tồn CN: {oldBranchOnHand} (Kho: {oldWarehouseOnHand}) · WAC cũ:{" "}
                            {oldBranchWac.toLocaleString("vi-VN")} ₫
                          </span>
                          <div style={{ fontWeight: 600, padding: "2px 0" }}>
                            Thành tiền:{" "}
                            {((item.quantity || 0) * (item.unitPrice || 0)).toLocaleString("vi-VN")} ₫
                          </div>
                          <div style={{ fontSize: "11px", color: "#0284c7", fontWeight: 600 }}>
                            WAC mới tại CN: ~{projectedWac.toLocaleString("vi-VN")} ₫
                          </div>
                        </div>
                        <Button
                          variant="ghost"
                          disabled={receiptItems.length <= 1 || Boolean(createdReceiptId)}
                          onClick={() => removeReceiptLine(idx)}
                        >
                          <Trash2 size={16} color="#ef4444" />
                        </Button>
                      </div>
                    );
                  })}
                </div>
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
                  Tổng số lượng: <strong>{totalReceiptQuantity} sản phẩm</strong>
                </span>
                <span style={{ fontSize: "15px" }}>
                  Tổng thanh toán:{" "}
                  <strong style={{ color: "#16a34a" }}>
                    {totalReceiptAmount.toLocaleString("vi-VN")} ₫
                  </strong>
                </span>
              </div>

              {/* Tác động WAC */}
              <div className="impact-box" style={{ marginTop: "12px" }}>
                <strong>Cơ chế tự động hóa giá vốn bình quân (WAC) theo Chi nhánh:</strong>
                <p>
                  Giá vốn dự kiến được tính dựa trên số dư thực tế (<code>OnHand</code>) và giá vốn cũ (
                  <code>AverageCost</code>) tại đúng <strong>Chi nhánh & Kho tiếp nhận</strong> đã chọn ở trên.
                </p>
              </div>

              <div className="modal-actions">
                <Button variant="secondary" onClick={() => setReceiptModal(false)}>
                  Hủy
                </Button>
                <Button onClick={handleConfirmReceipt} disabled={loading}>
                  {loading
                    ? "Đang xử lý..."
                    : createdReceiptId
                    ? "Thử lại Xác nhận & Ghi sổ kho"
                    : "Xác nhận & Ghi sổ kho"}
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

          <div className="form-grid">
            <label>
              <span>Chọn Chi nhánh *</span>
              <Select
                value={reserveBranchId}
                onChange={(e) => {
                  const nextBr = e.target.value;
                  setReserveBranchId(nextBr);
                  const matchingWhs = warehouses.filter((w) => w.branchId === nextBr);
                  setReserveWarehouseId(matchingWhs[0]?.id || "");
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
                value={reserveWarehouseId}
                onChange={(e) => setReserveWarehouseId(e.target.value)}
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
              <Select value={reserveSkuId} onChange={(e) => setReserveSkuId(e.target.value)}>
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
                type="number"
                min="1"
                value={reserveQty}
                onChange={(e) => setReserveQty(Math.max(1, Number(e.target.value)))}
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
            <Button onClick={handleReserveStock} disabled={loading || loadingReserveWh}>
              {loading ? "Đang khóa tồn..." : "Xác nhận Giữ chỗ tại Kho này"}
            </Button>
          </div>
        </Modal>
      )}

      {/* MODAL CẤU HÌNH NGƯỠNG TỒN */}
      {thresholdOpen && (
        <Modal
          title="Cấu hình ngưỡng cảnh báo tồn kho an toàn"
          onClose={() => setThresholdOpen(false)}
        >
          <div className="threshold-scope">
            <p>
              Hệ thống tự động kích hoạt cảnh báo khi Tồn khả dụng (Available) ≤ Ngưỡng (Threshold).
            </p>
          </div>
          <div className="threshold-list" style={{ maxHeight: "300px", overflowY: "auto" }}>
            {skus.map((sku) => (
              <label key={`${sku.id}-${sku.branchId || "default"}`}>
                <span>
                  <strong>
                    {sku.code} ({sku.branchName || "CN"})
                  </strong>
                  <small>{sku.name}</small>
                </span>
                <Input
                  type="number"
                  min="0"
                  value={thresholds[sku.id] ?? sku.threshold}
                  onChange={(e) =>
                    setThresholds({ ...thresholds, [sku.id]: Number(e.target.value) })
                  }
                />
              </label>
            ))}
          </div>
          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setThresholdOpen(false)}>
              Hủy
            </Button>
            <Button onClick={() => setThresholdOpen(false)}>Lưu cấu hình</Button>
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

      {/* MODAL TRẢ HÀNG [Sprint 3 Preview] */}
      {returnOpen && (
        <Modal
          title="Ghi nhận trả hàng từ khách (Compensating Return) — [Sprint 3 Preview]"
          onClose={() => setReturnOpen(false)}
        >
          <div style={{ marginBottom: "10px" }}>
            <Badge tone="neutral">[Sprint 3 Preview]</Badge>
          </div>
          <div className="form-grid">
            <label>
              <span>Mã đơn hàng gốc</span>
              <Input placeholder="SO-2026-001" defaultValue="SO-2606-0181" />
            </label>
            <label>
              <span>Kho nhận lại hàng</span>
              <Select>
                {warehouses.map((w) => (
                  <option key={w.id} value={w.id}>
                    {w.name}
                  </option>
                ))}
              </Select>
            </label>
            <label>
              <span>Sản phẩm / SKU hoàn lại</span>
              <Select>
                {skus.map((s) => (
                  <option key={`${s.id}-${s.branchId}`} value={s.id}>
                    {s.code} - {s.name}
                  </option>
                ))}
              </Select>
            </label>
            <label>
              <span>Số lượng trả</span>
              <Input type="number" min="1" defaultValue="1" />
            </label>
          </div>
          <div className="impact-box" style={{ marginTop: "12px" }}>
            <strong>Bút toán bù trừ (Sprint 3):</strong> Việc nhận trả hàng sẽ tạo một bút toán{" "}
            <code>IN</code> mới vào sổ cái kho để tăng lại tồn thực tế, không sửa bút toán xuất bán cũ.
          </div>
          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setReturnOpen(false)}>
              Hủy
            </Button>
            <Button onClick={() => setReturnOpen(false)}>
              Đóng xem trước [Sprint 3 Preview]
            </Button>
          </div>
        </Modal>
      )}
    </>
  );
}
