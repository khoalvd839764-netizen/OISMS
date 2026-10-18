import { ArrowDownToLine, ArrowRightLeft, BookOpen, CheckCircle2, ClipboardCheck, Plus, RotateCcw, Search, Settings2, Trash2, TriangleAlert, AlertCircle } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Badge, Button, Input, Modal, PageTitle, Select } from "../components/ui";
import { apiAdapter } from "../services/apiAdapter";
import type { LedgerEntry, Sku } from "../types/domain";

export function InventoryPage() {
  const [tab, setTab] = useState<"balance" | "ledger">("balance");
  const [loading, setLoading] = useState(false);

  // Dữ liệu Tồn kho & Sổ kho
  const [skus, setSkus] = useState<Sku[]>([]);
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

  // Bộ lọc
  const [searchQuery, setSearchQuery] = useState("");
  const [branchFilter, setBranchFilter] = useState("all");
  const [stockStatusFilter, setStockStatusFilter] = useState("all");
  const [warehouseFilter, setWarehouseFilter] = useState("all");
  const [operationFilter, setOperationFilter] = useState("all");
  const [directionFilter, setDirectionFilter] = useState("all");

  // Form Phiếu nhập kho nhiều dòng
  const [selectedSupplier, setSelectedSupplier] = useState("");
  const [quickSupplierOpen, setQuickSupplierOpen] = useState(false);
  const [newSupplierName, setNewSupplierName] = useState("");
  const [selectedBranch, setSelectedBranch] = useState("");
  const [selectedWarehouse, setSelectedWarehouse] = useState("");
  const [receiptNotes, setReceiptNotes] = useState("");
  const [receiptItems, setReceiptItems] = useState<Array<{
    skuId: string;
    skuCode: string;
    skuName: string;
    quantity: number;
    unitPrice: number;
  }>>([]);
  const [receiptError, setReceiptError] = useState<string | null>(null);

  // Cấu hình ngưỡng cảnh báo
  const [thresholds, setThresholds] = useState<Record<string, number>>({});

  const loadData = async () => {
    setLoading(true);
    const [bRes, lRes, sRes, brRes, wRes, pRes] = await Promise.all([
      apiAdapter.getBalances(),
      apiAdapter.getLedger(),
      apiAdapter.getSuppliers(),
      apiAdapter.getBranches(),
      apiAdapter.getWarehouses(),
      apiAdapter.getSkus(),
    ]);

    if ("data" in bRes && Array.isArray(bRes.data)) {
      setSkus(bRes.data);
    }
    if ("data" in pRes && Array.isArray(pRes.data)) {
      setAvailableSkus(pRes.data);
    }
    if ("data" in lRes && Array.isArray(lRes.data)) {
      setLedger(lRes.data);
    }
    if ("data" in sRes && Array.isArray(sRes.data)) {
      setSuppliers(sRes.data);
      if (sRes.data[0]?.id && !selectedSupplier) setSelectedSupplier(sRes.data[0].id);
    }
    if ("data" in brRes && Array.isArray(brRes.data)) {
      setBranches(brRes.data);
      if (brRes.data[0]?.id && !selectedBranch) setSelectedBranch(brRes.data[0].id);
    }
    if ("data" in wRes && Array.isArray(wRes.data)) {
      setWarehouses(wRes.data);
      if (wRes.data[0]?.id && !selectedWarehouse) setSelectedWarehouse(wRes.data[0].id);
    }
    setLoading(false);
  };

  useEffect(() => {
    loadData();
  }, []);

  const filteredLedger = useMemo(() => ledger.filter((item) =>
    (warehouseFilter === "all" || item.warehouse.includes(warehouseFilter)) &&
    (operationFilter === "all" || item.transaction === operationFilter) &&
    (directionFilter === "all" || item.direction === directionFilter) &&
    (`${item.sku} ${item.reference}`.toLowerCase().includes(searchQuery.toLowerCase()))
  ), [ledger, warehouseFilter, operationFilter, directionFilter, searchQuery]);

  const filteredSkus = useMemo(() => skus.filter((sku) => {
    const matchesSearch = `${sku.name} ${sku.code} ${sku.variant || ""}`.toLowerCase().includes(searchQuery.toLowerCase());
    const matchesBranch = branchFilter === "all" || sku.branchId === branchFilter || sku.branchName === branchFilter;
    const available = sku.onHand - sku.reserved;
    const thresh = thresholds[sku.id] ?? sku.threshold;
    const matchesStatus = stockStatusFilter === "all" || (stockStatusFilter === "low" && available <= thresh);
    return matchesSearch && matchesBranch && matchesStatus;
  }), [skus, searchQuery, branchFilter, stockStatusFilter, thresholds]);

  const lowStock = skus.filter((sku) => (sku.onHand - sku.reserved) <= (thresholds[sku.id] ?? sku.threshold));

  // Thêm dòng sản phẩm mới vào phiếu nhập
  const addReceiptLine = () => {
    const candidateList = availableSkus.length > 0 ? availableSkus : skus;
    const candidate = candidateList[receiptItems.length % Math.max(1, candidateList.length)] || candidateList[0];
    if (!candidate) {
      setReceiptError("Chưa có SKU nào trong hệ thống. Vui lòng sang mục Sản phẩm & biến thể để tạo SKU trước khi lập phiếu nhập.");
      return;
    }
    setReceiptItems([
      ...receiptItems,
      {
        skuId: candidate.id,
        skuCode: candidate.code,
        skuName: candidate.name,
        quantity: 5,
        unitPrice: candidate.averageCost > 0 ? candidate.averageCost : 100000,
      },
    ]);
  };

  const openReceiptModal = () => {
    setReceiptModal(true);
    setConfirmedSuccess(false);
    setReceiptError(null);
    if (!selectedSupplier && suppliers[0]?.id) {
      setSelectedSupplier(suppliers[0].id);
    }
    if (receiptItems.length === 0) {
      const candidate = availableSkus[0] || skus[0];
      if (candidate) {
        setReceiptItems([
          {
            skuId: candidate.id,
            skuCode: candidate.code,
            skuName: candidate.name,
            quantity: 10,
            unitPrice: candidate.averageCost > 0 ? candidate.averageCost : 120000,
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
    if (receiptItems.length <= 1) return;
    setReceiptItems(receiptItems.filter((_, i) => i !== idx));
  };

  const totalReceiptQuantity = receiptItems.reduce((acc, cur) => acc + (Number(cur.quantity) || 0), 0);
  const totalReceiptAmount = receiptItems.reduce((acc, cur) => acc + ((Number(cur.quantity) || 0) * (Number(cur.unitPrice) || 0)), 0);

  // Xử lý tạo và xác nhận phiếu nhập kho (WAC Calculation & Atomic Confirm)
  const handleConfirmReceipt = async () => {
    setReceiptError(null);
    if (!selectedBranch || !selectedWarehouse) {
      setReceiptError("Vui lòng chọn chi nhánh và kho hàng tiếp nhận.");
      return;
    }
    if (!selectedSupplier) {
      setReceiptError("Vui lòng chọn Nhà cung cấp (hoặc bấm '+ Tạo nhanh NCC' nếu cửa hàng chưa có nhà cung cấp).");
      return;
    }
    if (receiptItems.length === 0) {
      setReceiptError("Vui lòng thêm ít nhất một dòng mặt hàng.");
      return;
    }
    if (receiptItems.some((item) => !item.skuId || Number(item.quantity) <= 0 || Number(item.unitPrice) < 0)) {
      setReceiptError("Vui lòng kiểm tra lại SKU, số lượng (> 0) và đơn giá nhập (≥ 0) trên từng dòng hàng.");
      return;
    }

    setLoading(true);
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

    if (res.error) {
      setLoading(false);
      setReceiptError(res.error.message || "Không thể tạo phiếu nhập kho.");
      return;
    }

    // Xác nhận và ghi sổ kho nguyên tử (Atomic Confirm)
    if (res.data?.id) {
      const confirmRes = await apiAdapter.confirmGoodsReceipt(res.data.id);
      if (confirmRes.error) {
        setLoading(false);
        setReceiptError(confirmRes.error.message || "Tạo phiếu thành công nhưng ghi sổ thất bại.");
        return;
      }
    }

    setLoading(false);
    setConfirmedSuccess(true);
    await loadData();
  };

  return (
    <>
      <PageTitle
        eyebrow="FR-INV · FR-COST"
        title="Kho & sổ kho"
        description="Theo dõi tồn thực tế, tồn giữ chỗ, thực bán khả dụng và bút toán sổ cái bất biến theo từng SKU."
        actions={
          <>
            <Button variant="secondary" onClick={() => setReturnOpen(true)}>
              <RotateCcw size={17} />Ghi nhận trả hàng
            </Button>
            <Button variant="secondary">
              <ArrowRightLeft size={17} />Chuyển kho
            </Button>
            <Button onClick={openReceiptModal}>
              <Plus size={17} />Tạo phiếu nhập
            </Button>
          </>
        }
      />

      <div className="process-strip">
        <div><span><ArrowDownToLine size={18} /></span><strong>Phiếu nhập</strong><small>Sẵn sàng ghi sổ WAC</small></div>
        <div><span><ArrowRightLeft size={18} /></span><strong>Chuyển kho</strong><small>2 bước nội bộ</small></div>
        <div><span><ClipboardCheck size={18} /></span><strong>Kiểm kê</strong><small>Cân kho tự động</small></div>
        <div><span><TriangleAlert size={18} /></span><strong>Cảnh báo tồn</strong><small>{lowStock.length} SKU dưới ngưỡng</small></div>
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
              <button key={sku.id} onClick={() => setTab("balance")}>
                <strong>{sku.code}</strong>
                <span>Khả dụng {sku.onHand - sku.reserved} / Ngưỡng {thresholds[sku.id] ?? sku.threshold}</span>
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
            Bảng Tồn kho
          </button>
          <button className={tab === "ledger" ? "active" : ""} onClick={() => setTab("ledger")}>
            Sổ kho bất biến (Append-Only)
          </button>
        </div>

        {tab === "balance" ? (
          <>
            <div className="toolbar">
              <label className="search-field">
                <Search size={18} />
                <Input value={searchQuery} onChange={(e) => setSearchQuery(e.target.value)} placeholder="Tìm SKU hoặc tên sản phẩm..." />
              </label>
              <Select aria-label="Chi nhánh" value={branchFilter} onChange={(e) => setBranchFilter(e.target.value)}>
                <option value="all">Tất cả chi nhánh</option>
                {branches.map((b) => (
                  <option key={b.id} value={b.id}>{b.name}</option>
                ))}
              </Select>
              <Select aria-label="Trạng thái tồn" value={stockStatusFilter} onChange={(e) => setStockStatusFilter(e.target.value)}>
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
                    <th>Ngưỡng</th>
                  </tr>
                </thead>
                <tbody>
                  {loading && skus.length === 0 ? (
                    <tr>
                      <td colSpan={8} style={{ textAlign: "center", padding: "32px 0", color: "#64748b" }}>
                        <div style={{ display: "inline-block", width: "20px", height: "20px", border: "2px solid #cbd5e1", borderTopColor: "#1769e0", borderRadius: "50%", animation: "spin 0.8s linear infinite", verticalAlign: "middle", marginRight: "8px" }} />
                        Đang nạp dữ liệu tồn kho từ máy chủ OISM...
                      </td>
                    </tr>
                  ) : filteredSkus.length === 0 ? (
                    <tr>
                      <td colSpan={8} style={{ textAlign: "center", padding: "36px 0", color: "#64748b" }}>
                        Chưa có dữ liệu tồn kho. Bấm <strong>"Tạo phiếu nhập"</strong> phía trên để nhập hàng và ghi nhận số dư ban đầu.
                      </td>
                    </tr>
                  ) : (
                    filteredSkus.map((sku) => {
                      const available = sku.onHand - sku.reserved;
                      const thresh = thresholds[sku.id] ?? sku.threshold;
                      const isUnder = available <= thresh;
                      return (
                        <tr key={`${sku.id}-${sku.warehouseId || "default"}`}>
                          <td>
                            <div className="product-cell">
                              <span className="product-thumb">{sku.image || "📦"}</span>
                              <div>
                                <strong>{sku.code}</strong>
                                <span>{sku.name} {sku.variant ? `· ${sku.variant}` : ""}</span>
                              </div>
                            </div>
                          </td>
                          <td>{sku.branchName || "Chi nhánh chính"} / {sku.warehouseName || "Kho chính"}</td>
                          <td><strong>{sku.onHand}</strong></td>
                          <td><span style={{ color: "#d97706" }}>{sku.reserved}</span></td>
                          <td>
                            <strong className={isUnder ? "text-warning" : ""}>{available}</strong>
                          </td>
                          <td>{sku.averageCost.toLocaleString("vi-VN")} ₫</td>
                          <td><strong>{(sku.averageCost * sku.onHand).toLocaleString("vi-VN")} ₫</strong></td>
                          <td>
                            <Button variant="ghost" onClick={() => setThresholdOpen(true)}>
                              {thresh}
                            </Button>
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </>
        ) : (
          <>
            <div className="toolbar ledger-filters">
              <label className="search-field">
                <Search size={18} />
                <Input value={searchQuery} onChange={(e) => setSearchQuery(e.target.value)} placeholder="Tìm SKU hoặc mã chứng từ..." />
              </label>
              <Select value={warehouseFilter} onChange={(e) => setWarehouseFilter(e.target.value)}>
                <option value="all">Tất cả kho hàng</option>
                {warehouses.map((w) => (
                  <option key={w.id} value={w.name}>{w.name}</option>
                ))}
              </Select>
              <Select value={directionFilter} onChange={(e) => setDirectionFilter(e.target.value)}>
                <option value="all">Bút toán IN và OUT</option>
                <option value="IN">Chỉ xem IN (Nhập/Hoàn)</option>
                <option value="OUT">Chỉ xem OUT (Xuất bán/Điều chuyển)</option>
              </Select>
              <Select value={operationFilter} onChange={(e) => setOperationFilter(e.target.value)}>
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
                        <div style={{ display: "inline-block", width: "20px", height: "20px", border: "2px solid #cbd5e1", borderTopColor: "#1769e0", borderRadius: "50%", animation: "spin 0.8s linear infinite", verticalAlign: "middle", marginRight: "8px" }} />
                        Đang truy vấn sổ cái kho bất biến...
                      </td>
                    </tr>
                  ) : filteredLedger.length === 0 ? (
                    <tr>
                      <td colSpan={8} style={{ textAlign: "center", padding: "36px 0", color: "#64748b" }}>
                        Chưa có bút toán sổ kho nào. Các thao tác Nhập kho hoặc Xuất bán đơn hàng sẽ tự động ghi sổ tại đây.
                      </td>
                    </tr>
                  ) : (
                    filteredLedger.map((item) => (
                      <tr key={item.id}>
                        <td>{item.createdAt}</td>
                        <td>{item.warehouse}</td>
                        <td><strong>{item.sku}</strong></td>
                        <td>
                          <Badge tone={item.direction === "IN" ? "success" : "danger"}>
                            {item.direction}
                          </Badge>
                        </td>
                        <td>{item.transaction}</td>
                        <td>
                          <strong>{item.direction === "IN" ? "+" : "−"}{item.quantity}</strong>
                        </td>
                        <td><strong>{item.balanceAfter}</strong></td>
                        <td>
                          <Button variant="ghost" className="text-link" onClick={() => setSelectedLedger(item)}>
                            {item.reference}
                          </Button>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
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
          title={confirmedSuccess ? "Phiếu nhập kho đã ghi sổ thành công" : "Lập phiếu nhập kho nhiều dòng hàng"}
          onClose={() => setReceiptModal(false)}
        >
          {confirmedSuccess ? (
            <div className="success-panel">
              <CheckCircle2 size={48} color="#16a34a" />
              <h3>Đã ghi sổ kho & tính lại giá vốn WAC</h3>
              <p>Đã nhập {totalReceiptQuantity} sản phẩm vào kho lưu trữ.</p>
              <div className="cost-result">
                <span>Tổng giá trị phiếu nhập: <strong>{totalReceiptAmount.toLocaleString("vi-VN")} ₫</strong></span>
                <span>Bút toán: <strong>Đã ghi nhận Append-Only Ledger</strong></span>
              </div>
              <Button onClick={() => { setReceiptModal(false); setTab("ledger"); }}>Xem sổ kho</Button>
            </div>
          ) : (
            <>
              {receiptError && (
                <div style={{ background: "#fee2e2", border: "1px solid #ef4444", color: "#b91c1c", padding: "10px", borderRadius: "8px", fontSize: "12px", marginBottom: "12px", display: "flex", alignItems: "center", gap: "8px" }}>
                  <AlertCircle size={16} />
                  <span>{receiptError}</span>
                </div>
              )}

              {/* Thông tin chung phiếu nhập */}
              <div className="form-grid">
                <label>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    <span>Nhà cung cấp</span>
                    <button
                      type="button"
                      onClick={() => setQuickSupplierOpen(!quickSupplierOpen)}
                      style={{ background: "none", border: "none", color: "#1769e0", fontSize: "11px", fontWeight: 600, cursor: "pointer" }}
                    >
                      {quickSupplierOpen ? "Đóng" : "+ Tạo nhanh NCC"}
                    </button>
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
                    <Select value={selectedSupplier} onChange={(e) => setSelectedSupplier(e.target.value)}>
                      <option value="">-- Khách lẻ / Chưa chọn NCC --</option>
                      {suppliers.map((s) => (
                        <option key={s.id} value={s.id}>{s.name}</option>
                      ))}
                    </Select>
                  )}
                </label>
                <label>
                  <span>Chi nhánh nhận *</span>
                  <Select
                    value={selectedBranch}
                    onChange={(e) => {
                      const nextBranch = e.target.value;
                      setSelectedBranch(nextBranch);
                      const matchingWh = warehouses.find((w) => w.branchId === nextBranch);
                      if (matchingWh) setSelectedWarehouse(matchingWh.id);
                    }}
                  >
                    {branches.map((b) => (
                      <option key={b.id} value={b.id}>{b.name}</option>
                    ))}
                  </Select>
                </label>
                <label>
                  <span>Kho tiếp nhận *</span>
                  <Select value={selectedWarehouse} onChange={(e) => setSelectedWarehouse(e.target.value)}>
                    {warehouses.map((w) => (
                      <option key={w.id} value={w.id}>{w.name}</option>
                    ))}
                  </Select>
                </label>
                <label>
                  <span>Ghi chú chứng từ</span>
                  <Input placeholder="Nhập số hóa đơn NCC hoặc ghi chú..." value={receiptNotes} onChange={(e) => setReceiptNotes(e.target.value)} />
                </label>
              </div>

              {/* Bảng dòng hàng đa dòng */}
              <div style={{ marginTop: "18px", borderTop: "1px solid #e2e8f0", paddingTop: "14px" }}>
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "10px" }}>
                  <strong>Chi tiết mặt hàng nhập kho ({receiptItems.length} dòng)</strong>
                  <Button variant="secondary" onClick={addReceiptLine}><Plus size={15} />Thêm dòng sản phẩm</Button>
                </div>

                <div style={{ display: "flex", flexDirection: "column", gap: "8px", maxHeight: "240px", overflowY: "auto" }}>
                  {receiptItems.map((item, idx) => {
                    const candidateList = availableSkus.length > 0 ? availableSkus : skus;
                    const currentSku = skus.find((s) => s.id === item.skuId) || candidateList.find((s) => s.id === item.skuId);
                    const oldOnHand = currentSku?.onHand ?? 0;
                    const oldWac = currentSku?.averageCost ?? 0;
                    const inQty = Math.max(0, Number(item.quantity) || 0);
                    const inPrice = Math.max(0, Number(item.unitPrice) || 0);
                    const projectedWac = (oldOnHand + inQty) > 0
                      ? Math.round(((oldOnHand * oldWac) + (inQty * inPrice)) / (oldOnHand + inQty))
                      : inPrice;

                    return (
                      <div key={idx} style={{ background: "#f8fafc", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "10px", display: "grid", gridTemplateColumns: "2fr 0.9fr 1.2fr 1.3fr auto", gap: "8px", alignItems: "end" }}>
                        <label>
                          <span style={{ fontSize: "11px" }}>Chọn SKU / Sản phẩm *</span>
                          <Select
                            value={item.skuId}
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
                              <option key={s.id} value={s.id}>{s.code} - {s.name}</option>
                            ))}
                          </Select>
                        </label>
                        <label>
                          <span style={{ fontSize: "11px" }}>Số lượng *</span>
                          <Input
                            type="number"
                            min="1"
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
                            value={item.unitPrice}
                            onChange={(e) => {
                              const next = [...receiptItems];
                              next[idx].unitPrice = Number(e.target.value);
                              setReceiptItems(next);
                            }}
                          />
                        </label>
                        <div>
                          <span style={{ fontSize: "11px", color: "#64748b" }}>Thành tiền & WAC dự kiến</span>
                          <div style={{ fontWeight: 600, padding: "4px 0 2px" }}>
                            {((item.quantity || 0) * (item.unitPrice || 0)).toLocaleString("vi-VN")} ₫
                          </div>
                          <div style={{ fontSize: "11px", color: "#0284c7" }}>
                            WAC mới: ~{projectedWac.toLocaleString("vi-VN")} ₫
                          </div>
                        </div>
                        <Button variant="ghost" disabled={receiptItems.length <= 1} onClick={() => removeReceiptLine(idx)}>
                          <Trash2 size={16} color="#ef4444" />
                        </Button>
                      </div>
                    );
                  })}
                </div>
              </div>

              {/* Tổng kết phiếu */}
              <div style={{ marginTop: "16px", padding: "12px", background: "#f1f5f9", borderRadius: "8px", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                <span>Tổng số lượng: <strong>{totalReceiptQuantity} sản phẩm</strong></span>
                <span style={{ fontSize: "15px" }}>Tổng thanh toán: <strong style={{ color: "#16a34a" }}>{totalReceiptAmount.toLocaleString("vi-VN")} ₫</strong></span>
              </div>

              {/* Tác động WAC */}
              <div className="impact-box" style={{ marginTop: "12px" }}>
                <strong>Cơ chế tự động hóa giá vốn bình quân (WAC):</strong>
                <p>Khi bấm "Xác nhận & ghi sổ", hệ thống sẽ tạo các bút toán <code>IN</code> tương ứng, đồng thời tính toán lại giá vốn mới của từng SKU theo công thức bình quân gia quyền. Chứng từ sẽ bị khóa sửa sau khi xác nhận.</p>
              </div>

              <div className="modal-actions">
                <Button variant="secondary" onClick={() => setReceiptModal(false)}>Hủy</Button>
                <Button onClick={handleConfirmReceipt} disabled={loading}>
                  {loading ? "Đang ghi sổ..." : "Xác nhận & Ghi sổ kho"}
                </Button>
              </div>
            </>
          )}
        </Modal>
      )}

      {/* MODAL CẤU HÌNH NGƯỠNG TỒN */}
      {thresholdOpen && (
        <Modal title="Cấu hình ngưỡng cảnh báo tồn kho an toàn" onClose={() => setThresholdOpen(false)}>
          <div className="threshold-scope">
            <p>Hệ thống tự động kích hoạt cảnh báo khi Tồn khả dụng (Available) ≤ Ngưỡng (Threshold).</p>
          </div>
          <div className="threshold-list" style={{ maxHeight: "300px", overflowY: "auto" }}>
            {skus.map((sku) => (
              <label key={sku.id}>
                <span><strong>{sku.code}</strong><small>{sku.name}</small></span>
                <Input
                  type="number"
                  min="0"
                  value={thresholds[sku.id] ?? sku.threshold}
                  onChange={(e) => setThresholds({ ...thresholds, [sku.id]: Number(e.target.value) })}
                />
              </label>
            ))}
          </div>
          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setThresholdOpen(false)}>Hủy</Button>
            <Button onClick={() => setThresholdOpen(false)}>Lưu cấu hình</Button>
          </div>
        </Modal>
      )}

      {/* MODAL XEM CHI TIẾT CHỨNG TỪ SỔ KHO BẤT BIẾN */}
      {selectedLedger && (
        <Modal title={`Chứng từ sổ kho: ${selectedLedger.reference}`} onClose={() => setSelectedLedger(null)}>
          <div className="document-detail">
            <Badge tone={selectedLedger.direction === "IN" ? "success" : "danger"}>
              Bút toán {selectedLedger.direction}
            </Badge>
            <h3>{selectedLedger.transaction}</h3>
            <p>Đã ghi sổ lúc {selectedLedger.createdAt} tại {selectedLedger.warehouse}.</p>
            <dl>
              <div><dt>Mã SKU</dt><dd><strong>{selectedLedger.sku}</strong></dd></div>
              <div><dt>Số lượng biến động</dt><dd><strong>{selectedLedger.direction === "IN" ? "+" : "−"}{selectedLedger.quantity}</strong></dd></div>
              <div><dt>Số dư tồn sau bút toán</dt><dd><strong>{selectedLedger.balanceAfter}</strong></dd></div>
            </dl>
            <div className="readonly-note">
              <BookOpen size={16} />Bút toán đã chốt vào sổ cái là bất biến và chỉ đọc.
            </div>
          </div>
        </Modal>
      )}

      {/* MODAL TRẢ HÀNG */}
      {returnOpen && (
        <Modal title="Ghi nhận trả hàng từ khách (Compensating Return)" onClose={() => setReturnOpen(false)}>
          <div className="form-grid">
            <label>
              <span>Mã đơn hàng gốc</span>
              <Input placeholder="SO-2026-001" defaultValue="SO-2606-0181" />
            </label>
            <label>
              <span>Kho nhận lại hàng</span>
              <Select>
                {warehouses.map((w) => (
                  <option key={w.id} value={w.id}>{w.name}</option>
                ))}
              </Select>
            </label>
            <label>
              <span>Sản phẩm / SKU hoàn lại</span>
              <Select>
                {skus.map((s) => (
                  <option key={s.id} value={s.id}>{s.code} - {s.name}</option>
                ))}
              </Select>
            </label>
            <label>
              <span>Số lượng trả</span>
              <Input type="number" min="1" defaultValue="1" />
            </label>
          </div>
          <div className="impact-box" style={{ marginTop: "12px" }}>
            <strong>Bút toán bù trừ:</strong> Việc nhận trả hàng sẽ tạo một bút toán <code>IN</code> mới vào sổ cái kho để tăng lại tồn thực tế, không sửa bút toán xuất bán cũ.
          </div>
          <div className="modal-actions">
            <Button variant="secondary" onClick={() => setReturnOpen(false)}>Hủy</Button>
            <Button onClick={() => setReturnOpen(false)}>Tạo bút toán hoàn kho</Button>
          </div>
        </Modal>
      )}
    </>
  );
}
