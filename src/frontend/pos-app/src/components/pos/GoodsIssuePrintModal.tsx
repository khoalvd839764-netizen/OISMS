import React, { useCallback, useEffect, useState } from 'react';
import { AlertCircle, FileSpreadsheet, Loader2, Printer, RefreshCw, X } from 'lucide-react';
import { getGoodsIssueFetchErrorMessage, splitGoodsIssuePages } from '../../services/goodsIssuePrint';
import { getPosOrderGoodsIssues, PosGoodsIssueFetchError } from '../../services/posApi';
import type { PosGoodsIssue } from '../../types/pos';

interface GoodsIssuePrintModalProps {
  isOpen: boolean;
  orderId: string | null | undefined;
  tenantName?: string;
  printedByName?: string;
  onClose: () => void;
}

export const GoodsIssuePrintModal: React.FC<GoodsIssuePrintModalProps> = ({
  isOpen,
  orderId,
  tenantName,
  printedByName,
  onClose,
}) => {
  const [paperSize, setPaperSize] = useState<'A4' | 'A5'>('A4');
  const [issues, setIssues] = useState<PosGoodsIssue[]>([]);
  const [printedAt, setPrintedAt] = useState<Date>(() => new Date());
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message: string } | null>(null);

  const loadIssues = useCallback(async () => {
    if (!orderId) return;
    setLoading(true);
    setError(null);
    try {
      const data = await getPosOrderGoodsIssues(orderId);
      if (data.length === 0) {
        setError({ status: 404, message: 'Không tìm thấy phiếu xuất kho của đơn này' });
        setIssues([]);
      } else {
        setIssues(data);
        setPrintedAt(new Date());
      }
    } catch (err: any) {
      if (err instanceof PosGoodsIssueFetchError) {
        setError({
          status: err.status,
          message: getGoodsIssueFetchErrorMessage(err.status, err.message),
        });
      } else {
        setError({
          message: getGoodsIssueFetchErrorMessage(undefined, err?.message),
        });
      }
      setIssues([]);
    } finally {
      setLoading(false);
    }
  }, [orderId]);

  useEffect(() => {
    if (!isOpen || !orderId) {
      setIssues([]);
      setError(null);
      return;
    }
    void loadIssues();
  }, [isOpen, orderId, loadIssues]);

  if (!isOpen || !orderId) return null;

  const pages = splitGoodsIssuePages(issues);
  const formatNumber = (val: number) =>
    new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 }).format(val);
  const formatDateVn = (iso?: string | Date) => {
    if (!iso) return '—';
    const d = typeof iso === 'string' ? new Date(iso) : iso;
    if (Number.isNaN(d.getTime())) return '—';
    return new Intl.DateTimeFormat('vi-VN', {
      timeZone: 'Asia/Ho_Chi_Minh',
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    }).format(d);
  };

  return (
    <div className="fixed inset-0 z-60 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs no-print-bg">
      <style>{`
        @media print {
          html, body { width: auto !important; }
          .print-container, .pos-gi-wrapper {
            width: 100% !important;
            max-width: ${paperSize === 'A5' ? '132mm' : '190mm'} !important;
            margin: 0 auto !important;
            padding: 0 !important;
            border: none !important;
            box-shadow: none !important;
          }
          .pos-gi-paper {
            border: none !important;
            box-shadow: none !important;
            padding: 0 !important;
            margin: 0 !important;
          }
          .pos-gi-page-break {
            break-after: page;
            page-break-after: always;
          }
          .pos-gi-paper tr {
            break-inside: avoid;
            page-break-inside: avoid;
          }
        }
        @page { size: ${paperSize} portrait; margin: 12mm; }
      `}</style>

      <div className="bg-white rounded-2xl shadow-2xl border border-slate-200 max-w-4xl w-full overflow-hidden flex flex-col max-h-[95vh] no-print-border">
        <div className="px-5 py-3.5 text-white flex items-center justify-between no-print bg-slate-900">
          <div className="flex items-center gap-2">
            <FileSpreadsheet className="w-5 h-5 text-emerald-400" />
            <span className="font-bold text-sm">
              In Phiếu Xuất Kho (Mẫu 02-VT)
              {pages.length > 1 ? ` — ${pages.length} kho (${pages.length} trang)` : ''}
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

        <div className="p-3 border-b flex items-center justify-between gap-2 no-print bg-indigo-50 border-indigo-100">
          <div className="flex items-center gap-2 text-xs">
            <span className="font-semibold text-slate-700">Khổ giấy:</span>
            <div className="inline-flex bg-white p-0.5 rounded-lg border border-slate-200 font-semibold">
              <button
                type="button"
                onClick={() => setPaperSize('A4')}
                className={`px-2.5 py-1 rounded-md ${
                  paperSize === 'A4' ? 'bg-indigo-600 text-white' : 'text-slate-600'
                }`}
              >
                A4
              </button>
              <button
                type="button"
                onClick={() => setPaperSize('A5')}
                className={`px-2.5 py-1 rounded-md ${
                  paperSize === 'A5' ? 'bg-indigo-600 text-white' : 'text-slate-600'
                }`}
              >
                A5
              </button>
            </div>
          </div>

          <button
            type="button"
            disabled={loading || pages.length === 0}
            onClick={() => window.print()}
            className="inline-flex items-center gap-1.5 px-4 py-1.5 text-white bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50 rounded-xl font-bold text-xs shadow-md transition-all"
          >
            <Printer className="w-4 h-4" />
            <span>In phiếu xuất kho ({pages.length} tờ)</span>
          </button>
        </div>

        <div className="p-6 overflow-y-auto bg-slate-100/60 flex justify-center print-container min-h-[300px]">
          {loading ? (
            <div className="flex flex-col items-center justify-center py-12 text-slate-600">
              <Loader2 className="w-8 h-8 animate-spin text-indigo-600 mb-2" />
              <span className="text-sm font-medium">Đang tải phiếu xuất kho 02-VT...</span>
            </div>
          ) : error ? (
            <div className="p-5 bg-rose-50 border border-rose-200 rounded-xl text-rose-700 text-xs font-medium flex flex-col items-start gap-3 max-w-md self-center">
              <div className="flex items-start gap-2.5">
                <AlertCircle className="w-5 h-5 shrink-0 mt-0.5" />
                <div>{error.message}</div>
              </div>
              {error.status !== 404 && (
                <button
                  type="button"
                  onClick={() => void loadIssues()}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-rose-600 hover:bg-rose-700 text-white font-semibold text-xs"
                >
                  <RefreshCw className="w-3.5 h-3.5" />
                  <span>Thử lại</span>
                </button>
              )}
            </div>
          ) : (
            <div className="pos-gi-wrapper space-y-6 w-full max-w-[760px]">
              {pages.map(({ issue, hasPageBreakAfter }) => (
                <div
                  key={issue.id}
                  className={`pos-gi-paper bg-white border border-slate-300 text-slate-900 font-sans shadow-sm p-8 text-sm ${
                    hasPageBreakAfter ? 'pos-gi-page-break' : ''
                  }`}
                >
                  <div className="flex justify-between items-start pb-4 border-b border-slate-300">
                    <div>
                      <div className="font-extrabold uppercase text-base text-slate-900">
                        {tenantName || 'CỬA HÀNG OISMS'}
                      </div>
                      <div className="text-xs text-slate-600 mt-0.5">
                        Chi nhánh: <span className="font-semibold text-slate-800">{issue.branchName}</span>
                      </div>
                      <div className="text-xs text-slate-600">
                        Kho xuất: <span className="font-semibold text-slate-800">{issue.warehouseName}</span>
                      </div>
                    </div>
                    <div className="text-right text-xs">
                      <div className="font-bold text-slate-900">Mẫu số: 02-VT</div>
                      <div className="text-[11px] text-slate-500">
                        (Ban hành theo Thông tư số 200/2014/TT-BTC)
                      </div>
                    </div>
                  </div>

                  <div className="text-center py-4">
                    <h2 className="text-xl font-black uppercase tracking-wide text-slate-900 m-0">
                      PHIẾU XUẤT KHO
                    </h2>
                    <div className="text-xs text-slate-600 mt-1">
                      Ngày xuất: <span className="font-semibold">{formatDateVn(issue.issuedAt)}</span>
                    </div>
                    <div className="text-xs text-slate-700 mt-0.5">
                      Số phiếu: <span className="font-mono font-bold text-slate-900">{issue.number}</span>
                    </div>
                  </div>

                  <div className="space-y-1.5 text-xs pb-4 border-b border-dashed border-slate-300">
                    <div>
                      <span className="text-slate-500">- Họ và tên người nhận hàng: </span>
                      <span className="font-semibold text-slate-900">{issue.customerName || 'Khách lẻ'}</span>
                    </div>
                    <div>
                      <span className="text-slate-500">- Lý do xuất kho: </span>
                      <span className="font-semibold text-slate-900">{issue.reason}</span>
                    </div>
                    <div className="grid grid-cols-2 gap-2">
                      <div>
                        <span className="text-slate-500">- Xuất tại kho: </span>
                        <span className="font-bold text-slate-900">{issue.warehouseName}</span>
                      </div>
                      <div>
                        <span className="text-slate-500">- Chứng từ gốc: </span>
                        <span className="font-mono font-semibold text-slate-900">
                          {issue.orderNumber} / {issue.salesInvoiceNumber}
                        </span>
                      </div>
                    </div>
                  </div>

                  {/* Bảng 02-VT: KHÔNG có cột giá vốn (Q3, Q4) */}
                  <div className="py-4">
                    <table className="w-full text-xs border-collapse border border-slate-300">
                      <thead>
                        <tr className="bg-slate-100 text-slate-700 font-bold">
                          <th className="border border-slate-300 py-2 px-2 text-center w-10">STT</th>
                          <th className="border border-slate-300 py-2 px-2 text-left">Mã SKU</th>
                          <th className="border border-slate-300 py-2 px-2 text-left">Tên hàng hóa</th>
                          <th className="border border-slate-300 py-2 px-2 text-center">ĐVT</th>
                          <th className="border border-slate-300 py-2 px-2 text-right">SL xuất</th>
                          <th className="border border-slate-300 py-2 px-2 text-right">SL quy đổi (ĐV gốc)</th>
                        </tr>
                      </thead>
                      <tbody>
                        {issue.items.map((item, itemIdx) => (
                          <tr key={item.id}>
                            <td className="border border-slate-300 py-1.5 px-2 text-center">
                              {itemIdx + 1}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2 font-mono font-semibold">
                              {item.skuCode}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2 font-medium text-slate-900">
                              {item.skuName}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2 text-center">
                              {item.unitName || item.baseUnitName || 'SP'}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2 text-right font-semibold">
                              {formatNumber(item.quantity)}
                            </td>
                            <td className="border border-slate-300 py-1.5 px-2 text-right">
                              {formatNumber(item.baseQuantity)}
                              {item.baseUnitName ? ` ${item.baseUnitName}` : ''}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                      <tfoot>
                        <tr className="bg-slate-50 font-bold text-slate-900">
                          <td colSpan={4} className="border border-slate-300 py-2 px-2 text-right uppercase">
                            Cộng phiếu xuất:
                          </td>
                          <td className="border border-slate-300 py-2 px-2 text-right">
                            {formatNumber(issue.totalQuantity)}
                          </td>
                          <td className="border border-slate-300 py-2 px-2 text-right">
                            {formatNumber(issue.totalBaseQuantity)}
                          </td>
                        </tr>
                      </tfoot>
                    </table>
                  </div>

                  <div className="pt-6 grid grid-cols-3 gap-6 text-center text-xs text-slate-700">
                    <div>
                      <div className="font-bold text-slate-900">Người lập phiếu</div>
                      <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
                      <div className="mt-12 font-semibold text-slate-800">{issue.issuedByName || ''}</div>
                    </div>
                    <div>
                      <div className="font-bold text-slate-900">Thủ kho</div>
                      <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
                      <div className="mt-12 font-semibold text-slate-800">&nbsp;</div>
                    </div>
                    <div>
                      <div className="font-bold text-slate-900">Người nhận hàng</div>
                      <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
                      <div className="mt-12 font-semibold text-slate-800">{issue.customerName || ''}</div>
                    </div>
                  </div>

                  <div className="mt-6 pt-3 border-t border-dashed border-slate-300 flex flex-wrap justify-between text-[11px] text-slate-600">
                    <div>
                      Ngày in: <span className="font-semibold text-slate-800">{formatDateVn(printedAt)} (UTC+07:00)</span>
                    </div>
                    <div>
                      Người in: <span className="font-semibold text-slate-800">{printedByName || issue.issuedByName || 'Thu ngân'}</span>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
