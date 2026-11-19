import { useEffect, useState } from "react";
import { Download, Printer, RefreshCw } from "lucide-react";
import { Button, Modal } from "../ui";
import {
  inventoryPeriodService,
  type StockCardFilter,
  type StockCardReport,
} from "../../services/inventoryPeriodService";

interface StockCardModalProps {
  filter: StockCardFilter;
  onClose: () => void;
  onOpenTrace?: (referenceId: string) => void;
}

export function StockCardModal({ filter, onClose, onOpenTrace }: StockCardModalProps) {
  const [report, setReport] = useState<StockCardReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);

  useEffect(() => {
    let active = true;
    async function load() {
      setLoading(true);
      setError(null);
      try {
        const data = await inventoryPeriodService.getStockCard(filter);
        if (active) setReport(data);
      } catch (err: any) {
        if (active) {
          setError(
            err?.response?.data?.detail ||
            err?.response?.data?.message ||
            err?.message ||
            "Không tải được thẻ kho."
          );
        }
      } finally {
        if (active) setLoading(false);
      }
    }
    void load();
    return () => {
      active = false;
    };
  }, [filter.skuId, filter.branchId, filter.fromDate, filter.toDate]);

  const handleExportExcel = async () => {
    if (!report) return;
    setExporting(true);
    try {
      await inventoryPeriodService.exportStockCardExcel(filter, report.skuCode);
    } catch {
      alert("Không thể xuất file Excel thẻ kho. Vui lòng thử lại.");
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

  return (
    <Modal
      title={report?.reportTitle || "Thẻ kho / Sổ chi tiết hàng hóa"}
      onClose={onClose}
    >
      <div className="stock-card-container" style={{ padding: "16px 20px" }}>
        {loading ? (
          <div style={{ textAlign: "center", padding: "48px 0", color: "#64748b" }}>
            <RefreshCw size={24} className="spinning" style={{ margin: "0 auto 12px" }} />
            <p>Đang tải dữ liệu thẻ kho...</p>
          </div>
        ) : error ? (
          <div style={{ background: "#fef2f2", border: "1px solid #fecaca", borderRadius: "8px", padding: "14px", color: "#b91c1c" }}>
            <strong>Lỗi:</strong> {error}
          </div>
        ) : report ? (
          <div>
            {/* Header thông tin thẻ kho */}
            <div style={{ background: "#f8fafc", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "12px 16px", marginBottom: "16px" }}>
              <div style={{ display: "flex", justifyContent: "space-between", flexWrap: "wrap", gap: "10px" }}>
                <div>
                  <h3 style={{ margin: "0 0 4px", fontSize: "16px", color: "#1e293b" }}>
                    {report.productName} {report.variantName ? `(${report.variantName})` : ""}
                  </h3>
                  <div style={{ fontSize: "13px", color: "#475569", display: "flex", gap: "16px", flexWrap: "wrap" }}>
                    <span>Mã SKU: <strong>{report.skuCode}</strong></span>
                    <span>Mã vạch: <strong>{report.barcode || "—"}</strong></span>
                    <span>ĐVT: <strong>{report.unitName}</strong></span>
                  </div>
                </div>
                <div style={{ textAlign: "right" }}>
                  <div style={{ fontSize: "13px", color: "#475569" }}>
                    Chi nhánh: <strong>{report.branchName}</strong>
                  </div>
                  <div style={{ fontSize: "12px", color: "#64748b" }}>
                    Kỳ: <strong>{report.fromDate}</strong> đến <strong>{report.toDate}</strong>
                  </div>
                </div>
              </div>
            </div>

            {/* Thao tác In và Xuất Excel */}
            <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px", marginBottom: "12px" }} className="no-print">
              <Button variant="secondary" onClick={handleExportExcel} disabled={exporting}>
                <Download size={15} /> {exporting ? "Đang xuất..." : "Xuất Excel"}
              </Button>
              <Button variant="secondary" onClick={handlePrint}>
                <Printer size={15} /> In thẻ kho
              </Button>
            </div>

            {/* Bảng sổ chi tiết / thẻ kho */}
            <div className="table-wrap print-target" style={{ maxHeight: "550px", overflowY: "auto", border: "1px solid #e2e8f0", borderRadius: "8px" }}>
              <table style={{ width: "100%", fontSize: "12px", borderCollapse: "collapse" }}>
                <thead>
                  <tr style={{ background: "#f1f5f9", position: "sticky", top: 0, zIndex: 2 }}>
                    <th style={{ padding: "8px 10px", textAlign: "center", borderBottom: "2px solid #cbd5e1" }}>STT</th>
                    <th style={{ padding: "8px 10px", textAlign: "left", borderBottom: "2px solid #cbd5e1" }}>Ngày ghi sổ</th>
                    <th style={{ padding: "8px 10px", textAlign: "left", borderBottom: "2px solid #cbd5e1" }}>Số chứng từ</th>
                    <th style={{ padding: "8px 10px", textAlign: "left", borderBottom: "2px solid #cbd5e1" }}>Diễn giải</th>
                    <th style={{ padding: "8px 10px", textAlign: "right", borderBottom: "2px solid #cbd5e1" }}>Nhập (SL)</th>
                    {report.isOwner && (
                      <>
                        <th style={{ padding: "8px 10px", textAlign: "right", borderBottom: "2px solid #cbd5e1" }}>Đơn giá</th>
                        <th style={{ padding: "8px 10px", textAlign: "right", borderBottom: "2px solid #cbd5e1" }}>Thành tiền</th>
                      </>
                    )}
                    <th style={{ padding: "8px 10px", textAlign: "right", borderBottom: "2px solid #cbd5e1" }}>Xuất (SL)</th>
                    {report.isOwner && (
                      <>
                        <th style={{ padding: "8px 10px", textAlign: "right", borderBottom: "2px solid #cbd5e1" }}>Đơn giá</th>
                        <th style={{ padding: "8px 10px", textAlign: "right", borderBottom: "2px solid #cbd5e1" }}>Thành tiền</th>
                      </>
                    )}
                    <th style={{ padding: "8px 10px", textAlign: "right", borderBottom: "2px solid #cbd5e1" }}>Tồn sau (SL)</th>
                    {report.isOwner && (
                      <th style={{ padding: "8px 10px", textAlign: "right", borderBottom: "2px solid #cbd5e1" }}>Tồn sau (Giá trị)</th>
                    )}
                  </tr>
                </thead>
                <tbody>
                  {/* Dòng Số dư đầu kỳ */}
                  <tr style={{ background: "#f8fafc", fontWeight: 600 }}>
                    <td style={{ padding: "8px 10px", textAlign: "center" }}>—</td>
                    <td style={{ padding: "8px 10px" }}>00:00 {report.fromDate}</td>
                    <td style={{ padding: "8px 10px" }}>—</td>
                    <td style={{ padding: "8px 10px", color: "#1e3a8a" }}>SỐ DƯ ĐẦU KỲ</td>
                    <td style={{ padding: "8px 10px", textAlign: "right" }}>—</td>
                    {report.isOwner && (
                      <>
                        <td style={{ padding: "8px 10px", textAlign: "right" }}>—</td>
                        <td style={{ padding: "8px 10px", textAlign: "right" }}>—</td>
                      </>
                    )}
                    <td style={{ padding: "8px 10px", textAlign: "right" }}>—</td>
                    {report.isOwner && (
                      <>
                        <td style={{ padding: "8px 10px", textAlign: "right" }}>—</td>
                        <td style={{ padding: "8px 10px", textAlign: "right" }}>—</td>
                      </>
                    )}
                    <td style={{ padding: "8px 10px", textAlign: "right", color: "#047857" }}>
                      {formatNumber(report.openingQuantity)}
                    </td>
                    {report.isOwner && (
                      <td style={{ padding: "8px 10px", textAlign: "right", color: "#047857" }}>
                        {formatNumber(report.openingValue, true)}
                      </td>
                    )}
                  </tr>

                  {/* Các dòng phát sinh */}
                  {report.entries.length === 0 ? (
                    <tr>
                      <td colSpan={report.isOwner ? 11 : 6} style={{ textAlign: "center", padding: "24px 0", color: "#64748b" }}>
                        Không có phát sinh xuất nhập kho nào trong khoảng thời gian này.
                      </td>
                    </tr>
                  ) : (
                    report.entries.map((entry, idx) => (
                      <tr key={entry.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                        <td style={{ padding: "7px 10px", textAlign: "center", color: "#64748b" }}>{idx + 1}</td>
                        <td style={{ padding: "7px 10px", whiteSpace: "nowrap" }}>
                          {new Date(entry.createdAt).toLocaleString("vi-VN", {
                            day: "2-digit",
                            month: "2-digit",
                            year: "numeric",
                            hour: "2-digit",
                            minute: "2-digit",
                          })}
                        </td>
                        <td style={{ padding: "7px 10px", whiteSpace: "nowrap" }}>
                          {entry.referenceId && onOpenTrace ? (
                            <button
                              onClick={() => onOpenTrace(entry.referenceId!)}
                              style={{
                                background: "none",
                                border: "none",
                                color: "#2563eb",
                                textDecoration: "underline",
                                cursor: "pointer",
                                padding: 0,
                                fontSize: "12px",
                              }}
                            >
                              {entry.documentNumber || "Xem chứng từ"}
                            </button>
                          ) : (
                            entry.documentNumber || "—"
                          )}
                        </td>
                        <td style={{ padding: "7px 10px" }}>{entry.sourceType}</td>

                        {/* Nhập */}
                        <td style={{ padding: "7px 10px", textAlign: "right", color: entry.type === "IN" ? "#047857" : "#94a3b8" }}>
                          {entry.type === "IN" ? formatNumber(entry.inQuantity) : "—"}
                        </td>
                        {report.isOwner && (
                          <>
                            <td style={{ padding: "7px 10px", textAlign: "right", color: "#64748b" }}>
                              {entry.type === "IN" ? formatNumber(entry.inUnitPrice, true) : "—"}
                            </td>
                            <td style={{ padding: "7px 10px", textAlign: "right", color: entry.type === "IN" ? "#047857" : "#94a3b8" }}>
                              {entry.type === "IN" ? formatNumber(entry.inValue, true) : "—"}
                            </td>
                          </>
                        )}

                        {/* Xuất */}
                        <td style={{ padding: "7px 10px", textAlign: "right", color: entry.type === "OUT" ? "#b91c1c" : "#94a3b8" }}>
                          {entry.type === "OUT" ? formatNumber(entry.outQuantity) : "—"}
                        </td>
                        {report.isOwner && (
                          <>
                            <td style={{ padding: "7px 10px", textAlign: "right", color: "#64748b" }}>
                              {entry.type === "OUT" ? formatNumber(entry.outUnitPrice, true) : "—"}
                            </td>
                            <td style={{ padding: "7px 10px", textAlign: "right", color: entry.type === "OUT" ? "#b91c1c" : "#94a3b8" }}>
                              {entry.type === "OUT" ? formatNumber(entry.outValue, true) : "—"}
                            </td>
                          </>
                        )}

                        {/* Tồn sau */}
                        <td style={{ padding: "7px 10px", textAlign: "right", fontWeight: 500 }}>
                          {formatNumber(entry.balanceAfterQuantity)}
                        </td>
                        {report.isOwner && (
                          <td style={{ padding: "7px 10px", textAlign: "right", fontWeight: 500 }}>
                            {formatNumber(entry.balanceAfterValue, true)}
                          </td>
                        )}
                      </tr>
                    ))
                  )}

                  {/* Dòng Cộng phát sinh trong kỳ */}
                  <tr style={{ background: "#f1f5f9", fontWeight: 600 }}>
                    <td colSpan={4} style={{ padding: "8px 10px", textAlign: "right" }}>CỘNG PHÁT SINH TRONG KỲ:</td>
                    <td style={{ padding: "8px 10px", textAlign: "right", color: "#047857" }}>
                      {formatNumber(report.totalInQuantity)}
                    </td>
                    {report.isOwner && (
                      <>
                        <td style={{ padding: "8px 10px", textAlign: "right" }}>—</td>
                        <td style={{ padding: "8px 10px", textAlign: "right", color: "#047857" }}>
                          {formatNumber(report.totalInValue, true)}
                        </td>
                      </>
                    )}
                    <td style={{ padding: "8px 10px", textAlign: "right", color: "#b91c1c" }}>
                      {formatNumber(report.totalOutQuantity)}
                    </td>
                    {report.isOwner && (
                      <>
                        <td style={{ padding: "8px 10px", textAlign: "right" }}>—</td>
                        <td style={{ padding: "8px 10px", textAlign: "right", color: "#b91c1c" }}>
                          {formatNumber(report.totalOutValue, true)}
                        </td>
                      </>
                    )}
                    <td style={{ padding: "8px 10px", textAlign: "right" }}>—</td>
                    {report.isOwner && <td style={{ padding: "8px 10px", textAlign: "right" }}>—</td>}
                  </tr>

                  {/* Dòng Số dư cuối kỳ */}
                  <tr style={{ background: "#e2e8f0", fontWeight: 700 }}>
                    <td colSpan={4} style={{ padding: "9px 10px", textAlign: "right", color: "#1e3a8a" }}>SỐ DƯ CUỐI KỲ:</td>
                    <td style={{ padding: "9px 10px", textAlign: "right" }}>—</td>
                    {report.isOwner && (
                      <>
                        <td style={{ padding: "9px 10px", textAlign: "right" }}>—</td>
                        <td style={{ padding: "9px 10px", textAlign: "right" }}>—</td>
                      </>
                    )}
                    <td style={{ padding: "9px 10px", textAlign: "right" }}>—</td>
                    {report.isOwner && (
                      <>
                        <td style={{ padding: "9px 10px", textAlign: "right" }}>—</td>
                        <td style={{ padding: "9px 10px", textAlign: "right" }}>—</td>
                      </>
                    )}
                    <td style={{ padding: "9px 10px", textAlign: "right", color: "#047857", fontSize: "13px" }}>
                      {formatNumber(report.closingQuantity)}
                    </td>
                    {report.isOwner && (
                      <td style={{ padding: "9px 10px", textAlign: "right", color: "#047857", fontSize: "13px" }}>
                        {formatNumber(report.closingValue, true)}
                      </td>
                    )}
                  </tr>

                  {/* Dòng chênh lệch làm tròn nếu có (Q14) */}
                  {report.isOwner && report.hasRoundingDifference && (
                    <tr style={{ background: "#fffbeb", color: "#b45309", fontSize: "11px", fontStyle: "italic" }}>
                      <td colSpan={report.isOwner ? 10 : 5} style={{ padding: "7px 10px", textAlign: "right" }}>
                        Chênh lệch làm tròn WAC sổ cái so với tồn hiện tại ({report.currentOnHand} × {formatNumber(report.currentWac, true)} = {formatNumber(report.currentTheoreticalValue, true)}):
                      </td>
                      <td style={{ padding: "7px 10px", textAlign: "right", fontWeight: 700 }}>
                        {formatNumber(report.roundingDifference, true)}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>

            {/* Chữ ký mẫu in */}
            <div style={{ marginTop: "24px", display: "flex", justifyContent: "space-between", textAlign: "center", fontSize: "12px" }}>
              <div style={{ width: "30%" }}>
                <strong>Người lập biểu</strong>
                <p style={{ margin: "4px 0 36px", color: "#64748b", fontStyle: "italic" }}>(Ký, họ tên)</p>
              </div>
              <div style={{ width: "30%" }}>
                <strong>Thủ kho</strong>
                <p style={{ margin: "4px 0 36px", color: "#64748b", fontStyle: "italic" }}>(Ký, họ tên)</p>
              </div>
              <div style={{ width: "30%" }}>
                <strong>Giám đốc / Chủ shop</strong>
                <p style={{ margin: "4px 0 36px", color: "#64748b", fontStyle: "italic" }}>(Ký, đóng dấu)</p>
              </div>
            </div>
          </div>
        ) : null}
      </div>

      <style>{`
        @media print {
          body * {
            visibility: hidden;
          }
          .stock-card-container, .stock-card-container * {
            visibility: visible;
          }
          .stock-card-container {
            position: absolute;
            left: 0;
            top: 0;
            width: 100%;
          }
          .no-print {
            display: none !important;
          }
        }
      `}</style>
    </Modal>
  );
}
