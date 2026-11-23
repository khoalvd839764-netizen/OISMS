import { useCallback, useEffect, useMemo, useState } from "react";
import {
  ChevronDown,
  ChevronRight,
  Download,
  FileText,
  Printer,
  RefreshCw,
  Search,
} from "lucide-react";
import { Badge, Button, EmptyState, Input, Select } from "../ui";
import { useAuth } from "../../context/AuthContext";
import {
  inventoryPeriodService,
  type InventoryPeriodFilter,
  type InventoryPeriodReport,
} from "../../services/inventoryPeriodService";
import { StockCardModal } from "./StockCardModal";

interface BranchOption {
  id: string;
  name: string;
}

interface InventoryPeriodTabProps {
  branchId?: string;
  branches: BranchOption[];
  categories?: Array<{ id: string; name: string }>;
  onOpenTrace?: (referenceId: string) => void;
}

type PeriodPreset =
  | "today"
  | "yesterday"
  | "thisMonth"
  | "lastMonth"
  | "custom";

function formatDateStr(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

export function InventoryPeriodTab({
  branchId: initialBranchId,
  branches,
  categories = [],
  onOpenTrace,
}: InventoryPeriodTabProps) {
  const { account } = useAuth();
  const isOwner = account?.role === "Owner";

  // Mặc định: Hôm nay
  const todayStr = useMemo(() => formatDateStr(new Date()), []);
  const [preset, setPreset] = useState<PeriodPreset>("today");
  const [fromDate, setFromDate] = useState<string>(todayStr);
  const [toDate, setToDate] = useState<string>(todayStr);
  const [selectedBranchId, setSelectedBranchId] = useState<string>(initialBranchId || "");
  const [searchTerm, setSearchTerm] = useState("");
  const [selectedCategory, setSelectedCategory] = useState<string>("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);

  const [report, setReport] = useState<InventoryPeriodReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);

  // SKU được chọn để mở Thẻ kho
  const [stockCardTarget, setStockCardTarget] = useState<{
    skuId: string;
    branchId: string;
  } | null>(null);

  // Hàng đang mở xem chi tiết phân rã (Breakdown)
  const [expandedSkuId, setExpandedSkuId] = useState<string | null>(null);

  // Xử lý preset thời gian
  const handlePresetChange = (newPreset: PeriodPreset) => {
    setPreset(newPreset);
    const now = new Date();

    if (newPreset === "today") {
      const s = formatDateStr(now);
      setFromDate(s);
      setToDate(s);
    } else if (newPreset === "yesterday") {
      const y = new Date();
      y.setDate(now.getDate() - 1);
      const s = formatDateStr(y);
      setFromDate(s);
      setToDate(s);
    } else if (newPreset === "thisMonth") {
      const start = new Date(now.getFullYear(), now.getMonth(), 1);
      setFromDate(formatDateStr(start));
      setToDate(formatDateStr(now));
    } else if (newPreset === "lastMonth") {
      const start = new Date(now.getFullYear(), now.getMonth() - 1, 1);
      const end = new Date(now.getFullYear(), now.getMonth(), 0);
      setFromDate(formatDateStr(start));
      setToDate(formatDateStr(end));
    }
    setPage(1);
  };

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const filter: InventoryPeriodFilter = {
        fromDate,
        toDate,
        branchId: selectedBranchId || undefined,
        searchTerm: searchTerm.trim() || undefined,
        categoryId: selectedCategory || undefined,
        page,
        pageSize,
      };
      const data = await inventoryPeriodService.getInventoryPeriod(filter);
      setReport(data);
    } catch (err: any) {
      setError(
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        err?.message ||
        "Không thể tải báo cáo tồn theo kỳ."
      );
    } finally {
      setLoading(false);
    }
  }, [fromDate, toDate, selectedBranchId, searchTerm, selectedCategory, page, pageSize]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const handleExportExcel = async () => {
    setExporting(true);
    try {
      await inventoryPeriodService.exportInventoryPeriodExcel({
        fromDate,
        toDate,
        branchId: selectedBranchId || undefined,
        searchTerm: searchTerm.trim() || undefined,
        categoryId: selectedCategory || undefined,
      });
    } catch {
      alert("Không thể xuất file Excel. Vui lòng kiểm tra lại kết nối.");
    } finally {
      setExporting(false);
    }
  };

  const handlePrint = () => {
    window.print();
  };

  const formatNumber = (val: number | null | undefined, isCurrency = false) => {
    if (val === null || val === undefined) return "—";
    return isCurrency
      ? Math.round(val).toLocaleString("vi-VN") + " đ"
      : Number(val.toFixed(3)).toLocaleString("vi-VN");
  };

  const isAllBranches = !selectedBranchId;

  return (
    <div className="inventory-period-tab">
      {/* Bộ lọc kỳ, chi nhánh & tìm kiếm */}
      <div style={{ background: "#f8fafc", border: "1px solid #e2e8f0", borderRadius: "10px", padding: "16px", marginBottom: "16px" }} className="no-print">
        <div style={{ display: "flex", flexWrap: "wrap", gap: "12px", alignItems: "flex-end" }}>
          {/* Preset kỳ */}
          <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px", fontWeight: 500 }}>
            <span>Kỳ báo cáo</span>
            <Select value={preset} onChange={(e) => handlePresetChange(e.target.value as PeriodPreset)} style={{ width: "150px" }}>
              <option value="today">Hôm nay</option>
              <option value="yesterday">Hôm qua</option>
              <option value="thisMonth">Tháng này</option>
              <option value="lastMonth">Tháng trước</option>
              <option value="custom">Tùy chọn ngày</option>
            </Select>
          </label>

          {/* Từ ngày - Đến ngày */}
          <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px", fontWeight: 500 }}>
            <span>Từ ngày</span>
            <Input
              type="date"
              value={fromDate}
              max={todayStr}
              onChange={(e) => {
                setFromDate(e.target.value);
                setPreset("custom");
                setPage(1);
              }}
              style={{ width: "140px" }}
            />
          </label>

          <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px", fontWeight: 500 }}>
            <span>Đến ngày</span>
            <Input
              type="date"
              value={toDate}
              max={todayStr}
              onChange={(e) => {
                setToDate(e.target.value);
                setPreset("custom");
                setPage(1);
              }}
              style={{ width: "140px" }}
            />
          </label>

          {/* Chọn chi nhánh */}
          <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px", fontWeight: 500 }}>
            <span>Chi nhánh</span>
            <Select
              value={selectedBranchId}
              onChange={(e) => {
                setSelectedBranchId(e.target.value);
                setPage(1);
              }}
              style={{ width: "180px" }}
            >
              <option value="">Tất cả chi nhánh</option>
              {branches.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </Select>
          </label>

          {/* Lọc danh mục */}
          {categories.length > 0 && (
            <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px", fontWeight: 500 }}>
              <span>Danh mục</span>
              <Select
                value={selectedCategory}
                onChange={(e) => {
                  setSelectedCategory(e.target.value);
                  setPage(1);
                }}
                style={{ width: "160px" }}
              >
                <option value="">Tất cả danh mục</option>
                {categories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            </label>
          )}

          {/* Tìm kiếm */}
          <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px", flexGrow: 1, minWidth: "200px" }}>
            <span>Tìm kiếm</span>
            <div style={{ position: "relative" }}>
              <Input
                placeholder="Tìm mã SKU, Barcode, Tên sản phẩm..."
                value={searchTerm}
                onChange={(e) => {
                  setSearchTerm(e.target.value);
                  setPage(1);
                }}
                style={{ paddingLeft: "32px", width: "100%" }}
              />
              <Search size={15} style={{ position: "absolute", left: "10px", top: "50%", transform: "translateY(-50%)", color: "#94a3b8" }} />
            </div>
          </label>

          {/* Nút hành động */}
          <div style={{ display: "flex", gap: "8px" }}>
            <Button variant="secondary" onClick={() => void loadData()} disabled={loading}>
              <RefreshCw size={15} className={loading ? "spinning" : ""} /> Làm mới
            </Button>
            <Button variant="secondary" onClick={handleExportExcel} disabled={exporting || loading}>
              <Download size={15} /> {exporting ? "Đang xuất..." : "Xuất Excel"}
            </Button>
            <Button variant="secondary" onClick={handlePrint} disabled={loading}>
              <Printer size={15} /> In báo cáo
            </Button>
          </div>
        </div>
      </div>

      {/* Thông tin phạm vi & Chú thích */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px", fontSize: "13px", color: "#475569" }}>
        <div>
          <span>Phạm vi: <strong>{report?.branchName || "Đang tải..."}</strong></span>
          {" · "}
          <span>Kỳ: <strong>{fromDate}</strong> đến <strong>{toDate}</strong> {report?.isPeriodUpToNow && <Badge tone="info">Tính đến hiện tại</Badge>}</span>
        </div>
        <div>
          Tổng số SKU: <strong>{report?.totalCount ?? 0}</strong>
        </div>
      </div>

      {/* Bảng dữ liệu Tồn theo kỳ */}
      {error ? (
        <div style={{ background: "#fef2f2", border: "1px solid #fecaca", borderRadius: "8px", padding: "14px", color: "#b91c1c", marginBottom: "16px" }}>
          <strong>Lỗi:</strong> {error}
        </div>
      ) : loading ? (
        <div style={{ textAlign: "center", padding: "48px 0", color: "#64748b" }}>
          <RefreshCw size={24} className="spinning" style={{ margin: "0 auto 12px" }} />
          <p>Đang tổng hợp dữ liệu tồn kho theo kỳ...</p>
        </div>
      ) : !report || report.items.length === 0 ? (
        <EmptyState
          title="Không tìm thấy SKU nào"
          description="Không có sản phẩm nào thỏa mãn bộ lọc hiện tại trong khoảng thời gian đã chọn."
        />
      ) : (
        <>
          <div className="table-wrap print-target" style={{ border: "1px solid #e2e8f0", borderRadius: "8px", overflowX: "auto" }}>
            <table style={{ width: "100%", fontSize: "12px", borderCollapse: "collapse" }}>
              <thead>
                <tr style={{ background: "#1e3a8a", color: "#fff" }}>
                  <th style={{ padding: "10px 8px", textAlign: "center", width: "40px" }}>STT</th>
                  <th style={{ padding: "10px 8px", textAlign: "left" }}>Sản phẩm / Biến thể</th>
                  <th style={{ padding: "10px 8px", textAlign: "center", width: "60px" }}>ĐVT</th>

                  {/* Đầu kỳ */}
                  <th style={{ padding: "10px 8px", textAlign: "right" }}>Đầu kỳ (SL)</th>
                  {isOwner && <th style={{ padding: "10px 8px", textAlign: "right" }}>Đầu kỳ (Giá trị)</th>}

                  {/* Nhập */}
                  <th style={{ padding: "10px 8px", textAlign: "right" }}>Tổng nhập (SL)</th>
                  {isOwner && <th style={{ padding: "10px 8px", textAlign: "right" }}>Tổng nhập (Giá trị)</th>}

                  {/* Xuất */}
                  <th style={{ padding: "10px 8px", textAlign: "right" }}>Tổng xuất (SL)</th>
                  {isOwner && <th style={{ padding: "10px 8px", textAlign: "right" }}>Tổng xuất (Giá trị)</th>}

                  {/* Cuối kỳ */}
                  <th style={{ padding: "10px 8px", textAlign: "right" }}>Cuối kỳ (SL)</th>
                  {isOwner && <th style={{ padding: "10px 8px", textAlign: "right" }}>Cuối kỳ (Giá trị)</th>}

                  {/* Đang chuyển */}
                  {isAllBranches ? (
                    <>
                      <th style={{ padding: "10px 8px", textAlign: "right" }} title="Hàng đang chuyển nội bộ xuất trước đầu kỳ chưa nhận">
                        Đang chuyển đầu
                      </th>
                      <th style={{ padding: "10px 8px", textAlign: "right" }} title="Hàng đang chuyển nội bộ xuất trước cuối kỳ chưa nhận">
                        Đang chuyển cuối
                      </th>
                    </>
                  ) : (
                    <th style={{ padding: "10px 8px", textAlign: "right" }} title="Hàng đang chuyển đến chi nhánh này tại cuối kỳ">
                      Đang chuyển đến
                    </th>
                  )}

                  <th style={{ padding: "10px 8px", textAlign: "center", width: "90px" }} className="no-print">
                    Thao tác
                  </th>
                </tr>
              </thead>
              <tbody>
                {report.items.map((item, idx) => {
                  const isExpanded = expandedSkuId === item.skuId;
                  const itemIndex = (report.page - 1) * report.pageSize + idx + 1;

                  return (
                    <>
                      <tr
                        key={item.skuId}
                        style={{
                          borderBottom: isExpanded ? "none" : "1px solid #f1f5f9",
                          background: isExpanded ? "#f8fafc" : idx % 2 === 0 ? "#fff" : "#fafafa",
                        }}
                      >
                        <td style={{ padding: "8px", textAlign: "center", color: "#64748b" }}>{itemIndex}</td>
                        <td style={{ padding: "8px" }}>
                          <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
                            <button
                              onClick={() => setExpandedSkuId(isExpanded ? null : item.skuId)}
                              style={{ background: "none", border: "none", cursor: "pointer", padding: 0, color: "#64748b" }}
                              title="Xem chi tiết các nguồn Nhập / Xuất"
                              className="no-print"
                            >
                              {isExpanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                            </button>
                            <div>
                              <strong style={{ color: "#1e293b", display: "block" }}>{item.productName}</strong>
                              <span style={{ fontSize: "11px", color: "#64748b" }}>
                                SKU: <code>{item.skuCode}</code> {item.barcode && `| ${item.barcode}`} {item.variantName && `| ${item.variantName}`}
                              </span>
                            </div>
                          </div>
                        </td>
                        <td style={{ padding: "8px", textAlign: "center", color: "#64748b" }}>{item.unitName}</td>

                        {/* Đầu kỳ */}
                        <td style={{ padding: "8px", textAlign: "right", fontWeight: 500 }}>
                          {formatNumber(item.openingQuantity)}
                        </td>
                        {isOwner && (
                          <td style={{ padding: "8px", textAlign: "right", color: "#475569" }}>
                            {formatNumber(item.openingValue, true)}
                          </td>
                        )}

                        {/* Nhập */}
                        <td style={{ padding: "8px", textAlign: "right", color: item.inQuantity > 0 ? "#047857" : "#94a3b8", fontWeight: 500 }}>
                          {formatNumber(item.inQuantity)}
                        </td>
                        {isOwner && (
                          <td style={{ padding: "8px", textAlign: "right", color: item.inQuantity > 0 ? "#047857" : "#94a3b8" }}>
                            {formatNumber(item.inValue, true)}
                          </td>
                        )}

                        {/* Xuất */}
                        <td style={{ padding: "8px", textAlign: "right", color: item.outQuantity > 0 ? "#b91c1c" : "#94a3b8", fontWeight: 500 }}>
                          {formatNumber(item.outQuantity)}
                        </td>
                        {isOwner && (
                          <td style={{ padding: "8px", textAlign: "right", color: item.outQuantity > 0 ? "#b91c1c" : "#94a3b8" }}>
                            {formatNumber(item.outValue, true)}
                          </td>
                        )}

                        {/* Cuối kỳ */}
                        <td style={{ padding: "8px", textAlign: "right", fontWeight: 700, color: "#047857" }}>
                          {formatNumber(item.closingQuantity)}
                        </td>
                        {isOwner && (
                          <td style={{ padding: "8px", textAlign: "right", fontWeight: 700, color: "#047857" }}>
                            {formatNumber(item.closingValue, true)}
                          </td>
                        )}

                        {/* Đang chuyển */}
                        {isAllBranches ? (
                          <>
                            <td style={{ padding: "8px", textAlign: "right", color: item.openingInTransitQuantity > 0 ? "#d97706" : "#94a3b8" }}>
                              {formatNumber(item.openingInTransitQuantity)}
                            </td>
                            <td style={{ padding: "8px", textAlign: "right", color: item.closingInTransitQuantity > 0 ? "#d97706" : "#94a3b8" }}>
                              {formatNumber(item.closingInTransitQuantity)}
                            </td>
                          </>
                        ) : (
                          <td style={{ padding: "8px", textAlign: "right", color: item.closingInTransitQuantity > 0 ? "#d97706" : "#94a3b8" }}>
                            {formatNumber(item.closingInTransitQuantity)}
                          </td>
                        )}

                        {/* Nút Thẻ kho */}
                        <td style={{ padding: "8px", textAlign: "center" }} className="no-print">
                          <Button
                            variant="secondary"
                            onClick={() => {
                              // Nếu đang xem 1 chi nhánh thì dùng chi nhánh đó, nếu xem Tất cả thì dùng chi nhánh đầu tiên hoặc yêu cầu chọn
                              const targetBranch = selectedBranchId || (branches.length > 0 ? branches[0].id : "");
                              if (!targetBranch) {
                                alert("Vui lòng chọn một chi nhánh cụ thể để mở Thẻ kho.");
                                return;
                              }
                              setStockCardTarget({ skuId: item.skuId, branchId: targetBranch });
                            }}
                            style={{ padding: "4px 8px", fontSize: "11px" }}
                            title="Mở Thẻ kho / Sổ chi tiết hàng hóa"
                          >
                            <FileText size={13} /> Thẻ kho
                          </Button>
                        </td>
                      </tr>

                      {/* Dòng chi tiết phân rã Nhập / Xuất (Accordion) */}
                      {isExpanded && (
                        <tr style={{ background: "#f8fafc", borderBottom: "1px solid #cbd5e1" }}>
                          <td colSpan={isOwner ? (isAllBranches ? 13 : 12) : (isAllBranches ? 9 : 8)} style={{ padding: "10px 16px 14px 44px" }}>
                            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px", background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "12px 16px" }}>
                              {/* Phân rã Nhập */}
                              <div>
                                <strong style={{ color: "#047857", fontSize: "12px", display: "block", marginBottom: "6px" }}>
                                  Chi tiết nguồn nhập trong kỳ:
                                </strong>
                                <table style={{ width: "100%", fontSize: "11px" }}>
                                  <tbody>
                                    <tr>
                                      <td style={{ color: "#475569" }}>· Nhập mua từ NCC:</td>
                                      <td style={{ textAlign: "right", fontWeight: 600 }}>{formatNumber(item.inBreakdown.purchaseQuantity)}</td>
                                      {isOwner && <td style={{ textAlign: "right", color: "#64748b" }}>{formatNumber(item.inBreakdown.purchaseValue, true)}</td>}
                                    </tr>
                                    <tr>
                                      <td style={{ color: "#475569" }}>· Khách trả hàng:</td>
                                      <td style={{ textAlign: "right", fontWeight: 600 }}>{formatNumber(item.inBreakdown.returnQuantity)}</td>
                                      {isOwner && <td style={{ textAlign: "right", color: "#64748b" }}>{formatNumber(item.inBreakdown.returnValue, true)}</td>}
                                    </tr>
                                    {!isAllBranches && (
                                      <tr>
                                        <td style={{ color: "#475569" }}>· Nhập chuyển kho đến:</td>
                                        <td style={{ textAlign: "right", fontWeight: 600 }}>{formatNumber(item.inBreakdown.transferInQuantity)}</td>
                                        {isOwner && <td style={{ textAlign: "right", color: "#64748b" }}>{formatNumber(item.inBreakdown.transferInValue, true)}</td>}
                                      </tr>
                                    )}
                                    <tr>
                                      <td style={{ color: "#475569" }}>· Kiểm kê thừa / Điều chỉnh tăng:</td>
                                      <td style={{ textAlign: "right", fontWeight: 600 }}>{formatNumber(item.inBreakdown.adjustmentInQuantity)}</td>
                                      {isOwner && <td style={{ textAlign: "right", color: "#64748b" }}>{formatNumber(item.inBreakdown.adjustmentInValue, true)}</td>}
                                    </tr>
                                  </tbody>
                                </table>
                              </div>

                              {/* Phân rã Xuất */}
                              <div>
                                <strong style={{ color: "#b91c1c", fontSize: "12px", display: "block", marginBottom: "6px" }}>
                                  Chi tiết kênh xuất trong kỳ:
                                </strong>
                                <table style={{ width: "100%", fontSize: "11px" }}>
                                  <tbody>
                                    <tr>
                                      <td style={{ color: "#475569" }}>· Xuất bán tại quầy POS:</td>
                                      <td style={{ textAlign: "right", fontWeight: 600 }}>{formatNumber(item.outBreakdown.posQuantity)}</td>
                                      {isOwner && <td style={{ textAlign: "right", color: "#64748b" }}>{formatNumber(item.outBreakdown.posValue, true)}</td>}
                                    </tr>
                                    <tr>
                                      <td style={{ color: "#475569" }}>· Xuất bán sàn TMĐT (Shopee, TikTok, Lazada):</td>
                                      <td style={{ textAlign: "right", fontWeight: 600 }}>{formatNumber(item.outBreakdown.ecommerceQuantity)}</td>
                                      {isOwner && <td style={{ textAlign: "right", color: "#64748b" }}>{formatNumber(item.outBreakdown.ecommerceValue, true)}</td>}
                                    </tr>
                                    <tr>
                                      <td style={{ color: "#475569" }}>· Xuất bán đơn Admin:</td>
                                      <td style={{ textAlign: "right", fontWeight: 600 }}>{formatNumber(item.outBreakdown.adminQuantity)}</td>
                                      {isOwner && <td style={{ textAlign: "right", color: "#64748b" }}>{formatNumber(item.outBreakdown.adminValue, true)}</td>}
                                    </tr>
                                    {!isAllBranches && (
                                      <tr>
                                        <td style={{ color: "#475569" }}>· Xuất chuyển kho đi:</td>
                                        <td style={{ textAlign: "right", fontWeight: 600 }}>{formatNumber(item.outBreakdown.transferOutQuantity)}</td>
                                        {isOwner && <td style={{ textAlign: "right", color: "#64748b" }}>{formatNumber(item.outBreakdown.transferOutValue, true)}</td>}
                                      </tr>
                                    )}
                                    <tr>
                                      <td style={{ color: "#475569" }}>· Kiểm kê thiếu / Điều chỉnh giảm:</td>
                                      <td style={{ textAlign: "right", fontWeight: 600 }}>{formatNumber(item.outBreakdown.adjustmentOutQuantity)}</td>
                                      {isOwner && <td style={{ textAlign: "right", color: "#64748b" }}>{formatNumber(item.outBreakdown.adjustmentOutValue, true)}</td>}
                                    </tr>
                                  </tbody>
                                </table>
                              </div>
                            </div>
                          </td>
                        </tr>
                      )}
                    </>
                  );
                })}

                {/* Dòng TỔNG CỘNG */}
                <tr style={{ background: "#f1f5f9", fontWeight: 700, borderTop: "2px solid #cbd5e1" }}>
                  <td colSpan={3} style={{ padding: "10px 8px", textAlign: "right", color: "#1e3a8a" }}>
                    TỔNG CỘNG:
                  </td>
                  <td style={{ padding: "10px 8px", textAlign: "right" }}>
                    {formatNumber(report.summary.totalOpeningQuantity)}
                  </td>
                  {isOwner && (
                    <td style={{ padding: "10px 8px", textAlign: "right" }}>
                      {formatNumber(report.summary.totalOpeningValue, true)}
                    </td>
                  )}
                  <td style={{ padding: "10px 8px", textAlign: "right", color: "#047857" }}>
                    {formatNumber(report.summary.totalInQuantity)}
                  </td>
                  {isOwner && (
                    <td style={{ padding: "10px 8px", textAlign: "right", color: "#047857" }}>
                      {formatNumber(report.summary.totalInValue, true)}
                    </td>
                  )}
                  <td style={{ padding: "10px 8px", textAlign: "right", color: "#b91c1c" }}>
                    {formatNumber(report.summary.totalOutQuantity)}
                  </td>
                  {isOwner && (
                    <td style={{ padding: "10px 8px", textAlign: "right", color: "#b91c1c" }}>
                      {formatNumber(report.summary.totalOutValue, true)}
                    </td>
                  )}
                  <td style={{ padding: "10px 8px", textAlign: "right", color: "#047857", fontSize: "13px" }}>
                    {formatNumber(report.summary.totalClosingQuantity)}
                  </td>
                  {isOwner && (
                    <td style={{ padding: "10px 8px", textAlign: "right", color: "#047857", fontSize: "13px" }}>
                      {formatNumber(report.summary.totalClosingValue, true)}
                    </td>
                  )}
                  {isAllBranches ? (
                    <>
                      <td style={{ padding: "10px 8px", textAlign: "right", color: "#d97706" }}>
                        {formatNumber(report.summary.totalOpeningInTransitQuantity)}
                      </td>
                      <td style={{ padding: "10px 8px", textAlign: "right", color: "#d97706" }}>
                        {formatNumber(report.summary.totalClosingInTransitQuantity)}
                      </td>
                    </>
                  ) : (
                    <td style={{ padding: "10px 8px", textAlign: "right", color: "#d97706" }}>
                      {formatNumber(report.summary.totalClosingInTransitQuantity)}
                    </td>
                  )}
                  <td className="no-print" />
                </tr>
              </tbody>
            </table>
          </div>

          {/* Phân trang */}
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: "14px", flexWrap: "wrap", gap: "10px" }} className="no-print">
            <div style={{ fontSize: "13px", color: "#64748b" }}>
              Hiển thị {(report.page - 1) * report.pageSize + 1} - {Math.min(report.page * report.pageSize, report.totalCount)} trong tổng số {report.totalCount} SKU
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
              <label style={{ fontSize: "12px", color: "#64748b" }}>Số dòng:</label>
              <Select
                value={String(pageSize)}
                onChange={(e) => {
                  setPageSize(Number(e.target.value));
                  setPage(1);
                }}
                style={{ width: "80px", padding: "4px 8px", fontSize: "12px" }}
              >
                <option value="20">20</option>
                <option value="50">50</option>
                <option value="100">100</option>
              </Select>
              <Button variant="secondary" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={report.page <= 1} style={{ padding: "4px 10px" }}>
                Trang trước
              </Button>
              <span style={{ fontSize: "13px", fontWeight: 500, padding: "0 4px" }}>
                {report.page} / {report.totalPages || 1}
              </span>
              <Button variant="secondary" onClick={() => setPage((p) => p + 1)} disabled={report.page >= report.totalPages} style={{ padding: "4px 10px" }}>
                Trang sau
              </Button>
            </div>
          </div>
        </>
      )}

      {/* Modal Thẻ kho */}
      {stockCardTarget && (
        <StockCardModal
          filter={{
            skuId: stockCardTarget.skuId,
            branchId: stockCardTarget.branchId,
            fromDate,
            toDate,
          }}
          onClose={() => setStockCardTarget(null)}
          onOpenTrace={onOpenTrace}
        />
      )}
    </div>
  );
}
