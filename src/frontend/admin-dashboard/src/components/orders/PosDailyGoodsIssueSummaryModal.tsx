import React, { useCallback, useEffect, useState } from 'react';
import { Calendar, FileText, Printer, RefreshCw, X } from 'lucide-react';
import { goodsIssueService } from '../../services/goodsIssueService';
import type { PosDailyGoodsIssueSummary } from '../../types/goodsIssues';
import { PrintArea } from '../print/PrintArea';

interface PosDailyGoodsIssueSummaryModalProps {
  isOpen: boolean;
  branches: Array<{ id: string; name: string }>;
  tenantName?: string;
  isOwner: boolean;
  onClose: () => void;
}

function getTodayVietnamDateString(): string {
  const now = new Date();
  const vnTime = new Date(now.getTime() + 7 * 60 * 60 * 1000);
  return vnTime.toISOString().slice(0, 10);
}

export const PosDailyGoodsIssueSummaryModal: React.FC<PosDailyGoodsIssueSummaryModalProps> = ({
  isOpen,
  branches,
  tenantName,
  isOwner,
  onClose,
}) => {
  const [selectedDate, setSelectedDate] = useState<string>(() => getTodayVietnamDateString());
  const [selectedBranchId, setSelectedBranchId] = useState<string>('');
  const [summary, setSummary] = useState<PosDailyGoodsIssueSummary | null>(null);
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  const loadSummary = useCallback(async () => {
    if (!isOpen || !selectedDate) return;
    setLoading(true);
    setError(null);
    try {
      const data = await goodsIssueService.getPosDailySummary({
        date: selectedDate,
        branchId: selectedBranchId || undefined,
      });
      setSummary(data);
    } catch (err: any) {
      const msg =
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        err?.message ||
        'Không thể tải bảng kê xuất bán POS theo ngày.';
      setError(msg);
    } finally {
      setLoading(false);
    }
  }, [isOpen, selectedDate, selectedBranchId]);

  useEffect(() => {
    if (isOpen) {
      loadSummary();
    }
  }, [isOpen, loadSummary]);

  if (!isOpen) return null;

  const formatNumber = (val: number) =>
    new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 }).format(val);

  const formatCurrency = (val: number) =>
    new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);

  const showCost = isOwner && summary !== null && summary.totalCost !== null;

  return (
    <div className="fixed inset-0 z-60 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs no-print-bg">
      <style>{`
        @media print {
          html, body { width: auto !important; }
          .print-container, .pos-daily-summary-paper {
            width: 100% !important;
            max-width: 190mm !important;
            margin: 0 auto !important;
            padding: 0 !important;
            border: none !important;
            box-shadow: none !important;
          }
          .pos-daily-summary-paper tr {
            break-inside: avoid;
            page-break-inside: avoid;
          }
        }
        @page { size: A4 portrait; margin: 12mm; }
      `}</style>

      <div className="bg-white rounded-2xl shadow-2xl border border-slate-200 max-w-5xl w-full overflow-hidden flex flex-col max-h-[95vh] no-print-border">
        {/* Header */}
        <div className="px-5 py-3.5 text-white flex items-center justify-between no-print bg-slate-900">
          <div className="flex items-center gap-2">
            <FileText className="w-5 h-5 text-indigo-400" />
            <span className="font-bold text-sm">
              Bảng Kê Xuất Bán POS Theo Ngày (UTC+07:00)
            </span>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-1 rounded-lg text-slate-300 hover:text-white hover:bg-slate-800 transition-colors"
            aria-label="Đóng"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Filter & Print controls */}
        <div className="p-3 border-b flex flex-wrap items-center justify-between gap-3 no-print bg-indigo-50 border-indigo-100">
          <div className="flex flex-wrap items-center gap-2 text-xs">
            <label className="flex items-center gap-1.5 font-semibold text-slate-700">
              <Calendar className="w-3.5 h-3.5 text-indigo-600" />
              <span>Ngày (UTC+7):</span>
              <input
                type="date"
                value={selectedDate}
                onChange={(e) => setSelectedDate(e.target.value)}
                className="px-2.5 py-1 bg-white border border-slate-300 rounded-lg text-xs font-medium"
              />
            </label>

            <label className="flex items-center gap-1.5 font-semibold text-slate-700">
              <span>Chi nhánh:</span>
              <select
                value={selectedBranchId}
                onChange={(e) => setSelectedBranchId(e.target.value)}
                className="px-2.5 py-1 bg-white border border-slate-300 rounded-lg text-xs font-medium"
              >
                <option value="">Tất cả chi nhánh</option>
                {branches.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </select>
            </label>

            <button
              type="button"
              onClick={loadSummary}
              disabled={loading}
              className="inline-flex items-center gap-1 px-3 py-1 bg-white border border-slate-300 hover:bg-slate-50 rounded-lg font-semibold text-slate-700"
            >
              <RefreshCw className="w-3.5 h-3.5" />
              <span>Tải bảng kê</span>
            </button>
          </div>

          <button
            type="button"
            disabled={loading || !summary}
            onClick={() => window.print()}
            className="inline-flex items-center gap-1.5 px-4 py-1.5 text-white bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50 rounded-xl font-bold text-xs shadow-md transition-all"
          >
            <Printer className="w-4 h-4" />
            <span>In Bảng Kê (Ctrl + P)</span>
          </button>
        </div>

        {/* Printable content */}
        <div className="p-6 overflow-y-auto bg-slate-100/60 flex justify-center">
          {loading ? (
            <div className="py-12 text-sm text-slate-600 font-medium">
              Đang tổng hợp bảng kê xuất bán POS...
            </div>
          ) : error ? (
            <div className="p-4 bg-rose-50 border border-rose-200 rounded-xl text-rose-700 text-xs font-medium">
              {error}
            </div>
          ) : !summary ? null : (
            <PrintArea>
              <div className="pos-daily-summary-paper bg-white border border-slate-300 text-slate-900 font-sans shadow-sm p-8 text-sm w-full max-w-[820px]">
                {/* Header */}
                <div className="text-center pb-4 border-b border-slate-300">
                  <div className="font-extrabold uppercase text-base text-slate-900">
                    {tenantName || 'CỬA HÀNG OISMS'}
                  </div>
                  <div className="text-xs text-slate-600 mt-0.5">
                    Phạm vi chi nhánh: <span className="font-semibold text-slate-900">{summary.branchName}</span>
                  </div>
                  <h2 className="text-lg font-black uppercase tracking-wide text-slate-900 mt-3 mb-0">
                    BẢNG KÊ XUẤT BÁN POS THEO NGÀY
                  </h2>
                  <div className="text-xs text-slate-600 mt-1">
                    Ngày nghiệp vụ (UTC+07:00):{' '}
                    <span className="font-mono font-bold text-slate-900">{summary.businessDate}</span>
                  </div>
                  <div className="text-xs text-slate-500 mt-0.5">
                    Số đơn POS: <b>{summary.totalOrders}</b> · Số hóa đơn: <b>{summary.totalInvoices}</b> · Số phiếu xuất kho: <b>{summary.totalGoodsIssues}</b>
                  </div>
                </div>

                {/* Section 1: Summary by SKU */}
                <div className="py-4">
                  <h3 className="text-xs font-bold uppercase text-slate-700 mb-2">
                    I. Tổng hợp số lượng xuất bán POS theo mặt hàng (SKU)
                  </h3>
                  {summary.items.length === 0 ? (
                    <div className="p-4 border border-dashed border-slate-300 text-center text-xs text-slate-500">
                      Không có phát sinh xuất bán POS trong ngày {summary.businessDate}.
                    </div>
                  ) : (
                    <table className="w-full text-xs border-collapse border border-slate-300">
                      <thead>
                        <tr className="bg-slate-100 text-slate-700 font-bold">
                          <th className="border border-slate-300 py-2 px-2 text-center w-10">STT</th>
                          <th className="border border-slate-300 py-2 px-2 text-left">Mã SKU</th>
                          <th className="border border-slate-300 py-2 px-2 text-left">Tên mặt hàng</th>
                          <th className="border border-slate-300 py-2 px-2 text-center">ĐVT gốc</th>
                          <th className="border border-slate-300 py-2 px-2 text-right">
                            Tổng SL xuất (ĐV gốc)
                          </th>
                          {showCost && (
                            <>
                              <th className="border border-slate-300 py-2 px-2 text-right">
                                Đơn giá vốn BQ
                              </th>
                              <th className="border border-slate-300 py-2 px-2 text-right">
                                Thành tiền giá vốn
                              </th>
                            </>
                          )}
                        </tr>
                      </thead>
                      <tbody>
                        {summary.items.map((sku, idx) => (
                          <tr key={sku.skuId}>
                            <td className="border border-slate-300 py-1.5 px-2 text-center">
                              {idx + 1}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2 font-mono font-semibold">
                              {sku.skuCode}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2 font-medium text-slate-900">
                              {sku.skuName}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2 text-center">
                              {sku.baseUnitName || 'SP'}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2 text-right font-semibold">
                              {formatNumber(sku.totalBaseQuantity)}
                            </td>
                            {showCost && (
                              <>
                                <td className="border border-slate-300 py-1.5 px-2 text-right font-mono">
                                  {sku.unitCost !== null ? formatCurrency(sku.unitCost) : '—'}
                                </td>
                                <td className="border border-slate-300 py-1.5 px-2 text-right font-mono font-semibold">
                                  {sku.totalCost !== null ? formatCurrency(sku.totalCost) : '—'}
                                </td>
                              </>
                            )}
                          </tr>
                        ))}
                      </tbody>
                      <tfoot>
                        <tr className="bg-slate-50 font-bold text-slate-900">
                          <td
                            colSpan={4}
                            className="border border-slate-300 py-2 px-2 text-right uppercase"
                          >
                            Tổng cộng theo SKU:
                          </td>
                          <td className="border border-slate-300 py-2 px-2 text-right">
                            {formatNumber(summary.totalBaseQuantity)}
                          </td>
                          {showCost && (
                            <>
                              <td className="border border-slate-300 py-2 px-2 text-right">—</td>
                              <td className="border border-slate-300 py-2 px-2 text-right font-mono">
                                {summary.totalCost !== null ? formatCurrency(summary.totalCost) : '—'}
                              </td>
                            </>
                          )}
                        </tr>
                      </tfoot>
                    </table>
                  )}
                </div>

                {/* Section 2: Invoices & Goods Issues List */}
                <div className="py-3">
                  <h3 className="text-xs font-bold uppercase text-slate-700 mb-2">
                    II. Danh sách hóa đơn bán POS & phiếu xuất kho trong ngày ({summary.invoices.length})
                  </h3>
                  {summary.invoices.length === 0 ? (
                    <div className="text-xs text-slate-500">Không có hóa đơn POS trong ngày.</div>
                  ) : (
                    <table className="w-full text-xs border-collapse border border-slate-300">
                      <thead>
                        <tr className="bg-slate-100 text-slate-700 font-bold">
                          <th className="border border-slate-300 py-1.5 px-2 text-center w-10">STT</th>
                          <th className="border border-slate-300 py-1.5 px-2 text-left">Số phiếu xuất</th>
                          <th className="border border-slate-300 py-1.5 px-2 text-left">Số hóa đơn</th>
                          <th className="border border-slate-300 py-1.5 px-2 text-left">Kho xuất</th>
                          <th className="border border-slate-300 py-1.5 px-2 text-left">Thời điểm</th>
                          <th className="border border-slate-300 py-1.5 px-2 text-right">Tổng SL gốc</th>
                          {showCost && (
                            <th className="border border-slate-300 py-1.5 px-2 text-right">
                              Giá vốn xuất
                            </th>
                          )}
                        </tr>
                      </thead>
                      <tbody>
                        {summary.invoices.map((inv, idx) => (
                          <tr key={inv.goodsIssueId}>
                            <td className="border border-slate-300 py-1.5 px-2 text-center">
                              {idx + 1}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2 font-mono font-semibold">
                              {inv.goodsIssueNumber}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2 font-mono">
                              {inv.salesInvoiceNumber}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2">
                              {inv.warehouseName}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2">
                              {new Date(inv.issuedAt).toLocaleTimeString('vi-VN')}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2 text-right font-semibold">
                              {formatNumber(inv.totalBaseQuantity)}
                            </td>
                            {showCost && (
                              <td className="border border-slate-300 py-1.5 px-2 text-right font-mono">
                                {inv.totalCost !== null ? formatCurrency(inv.totalCost) : '—'}
                              </td>
                            )}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                </div>

                {/* Signatures */}
                <div className="pt-6 grid grid-cols-3 gap-6 text-center text-xs text-slate-700">
                  <div>
                    <div className="font-bold text-slate-900">Người lập bảng kê</div>
                    <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
                    <div className="mt-12">&nbsp;</div>
                  </div>
                  <div>
                    <div className="font-bold text-slate-900">Thủ kho</div>
                    <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
                    <div className="mt-12">&nbsp;</div>
                  </div>
                  <div>
                    <div className="font-bold text-slate-900">Quản lý / Chủ cửa hàng</div>
                    <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
                    <div className="mt-12">&nbsp;</div>
                  </div>
                </div>
              </div>
            </PrintArea>
          )}
        </div>
      </div>
    </div>
  );
};
