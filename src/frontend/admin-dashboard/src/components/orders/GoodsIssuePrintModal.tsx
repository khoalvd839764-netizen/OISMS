import React, { useEffect, useState } from 'react';
import { FileSpreadsheet, Printer, X } from 'lucide-react';
import { goodsIssueService } from '../../services/goodsIssueService';
import type { GoodsIssue } from '../../types/goodsIssues';
import { PrintArea } from '../print/PrintArea';

interface GoodsIssuePrintModalProps {
  isOpen: boolean;
  orderIds: string[];
  tenantName?: string;
  isOwner: boolean;
  onClose: () => void;
}

export const GoodsIssuePrintModal: React.FC<GoodsIssuePrintModalProps> = ({
  isOpen,
  orderIds,
  tenantName,
  isOwner,
  onClose,
}) => {
  const [issues, setIssues] = useState<GoodsIssue[]>([]);
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!isOpen || orderIds.length === 0) {
      setIssues([]);
      setError(null);
      return;
    }

    let isMounted = true;
    setLoading(true);
    setError(null);

    const loadPromise =
      orderIds.length === 1
        ? goodsIssueService.getByOrderId(orderIds[0])
        : goodsIssueService.getByOrderIds(orderIds);

    loadPromise
      .then((data) => {
        if (!isMounted) return;
        setIssues(data || []);
      })
      .catch((err: any) => {
        if (!isMounted) return;
        const msg =
          err?.response?.data?.detail ||
          err?.response?.data?.message ||
          err?.message ||
          'Không thể tải dữ liệu phiếu xuất kho 02-VT.';
        setError(msg);
      })
      .finally(() => {
        if (isMounted) setLoading(false);
      });

    return () => {
      isMounted = false;
    };
  }, [isOpen, orderIds]);

  if (!isOpen) return null;

  const formatNumber = (val: number) =>
    new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 }).format(val);

  const formatCurrency = (val: number) =>
    new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);

  const showCostColumns = isOwner && issues.some((gi) => gi.totalCost !== null);

  return (
    <div className="fixed inset-0 z-60 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs no-print-bg">
      <style>{`
        @media print {
          html, body { width: auto !important; }
          .print-container, .goods-issue-print-wrapper {
            width: 100% !important;
            max-width: 190mm !important;
            margin: 0 auto !important;
            padding: 0 !important;
            border: none !important;
            box-shadow: none !important;
          }
          .goods-issue-paper {
            border: none !important;
            box-shadow: none !important;
            padding: 0 !important;
            margin: 0 !important;
          }
          .goods-issue-page-break {
            break-after: page;
            page-break-after: always;
          }
          .goods-issue-paper tr {
            break-inside: avoid;
            page-break-inside: avoid;
          }
        }
        @page { size: A4 portrait; margin: 12mm; }
      `}</style>

      <div className="bg-white rounded-2xl shadow-2xl border border-slate-200 max-w-4xl w-full overflow-hidden flex flex-col max-h-[95vh] no-print-border">
        {/* Header */}
        <div className="px-5 py-3.5 text-white flex items-center justify-between no-print bg-slate-900">
          <div className="flex items-center gap-2">
            <FileSpreadsheet className="w-5 h-5 text-emerald-400" />
            <span className="font-bold text-sm">
              In Phiếu Xuất Kho (Mẫu 02-VT){' '}
              {orderIds.length > 1 ? `— In hàng loạt (${orderIds.length} đơn / ${issues.length} phiếu)` : ''}
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

        {/* Action bar */}
        <div className="p-3 border-b flex items-center justify-between no-print bg-indigo-50 border-indigo-100">
          <div className="text-xs text-slate-600 font-medium">
            {showCostColumns
              ? 'Bản in Chủ cửa hàng (Owner): Có hiển thị đơn giá vốn và thành tiền giá vốn.'
              : 'Bản in Nhân viên (Staff): Chỉ hiển thị số lượng xuất kho (ẩn giá vốn).'}
          </div>

          <button
            type="button"
            disabled={loading || issues.length === 0}
            onClick={() => window.print()}
            className="inline-flex items-center gap-1.5 px-4 py-1.5 text-white bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50 rounded-xl font-bold text-xs shadow-md transition-all"
          >
            <Printer className="w-4 h-4" />
            <span>In Phiếu 02-VT ({issues.length} phiếu)</span>
          </button>
        </div>

        {/* Body */}
        <div className="p-6 overflow-y-auto bg-slate-100/60 flex justify-center">
          {loading ? (
            <div className="py-12 text-sm text-slate-600 font-medium">
              Đang tải dữ liệu phiếu xuất kho 02-VT...
            </div>
          ) : error ? (
            <div className="p-4 bg-rose-50 border border-rose-200 rounded-xl text-rose-700 text-xs font-medium">
              {error}
            </div>
          ) : issues.length === 0 ? (
            <div className="py-12 text-sm text-slate-500">
              Không tìm thấy phiếu xuất kho cho đơn hàng đã chọn.
            </div>
          ) : (
            <PrintArea>
              <div className="goods-issue-print-wrapper space-y-6 w-full max-w-[760px]">
                {issues.map((issue, idx) => {
                  const isLast = idx === issues.length - 1;
                  const issueDate = new Date(issue.issuedAt);
                  const issueHasCost = isOwner && issue.totalCost !== null;

                  return (
                    <div
                      key={issue.id}
                      className={`goods-issue-paper bg-white border border-slate-300 text-slate-900 font-sans shadow-sm p-8 text-sm ${
                        !isLast ? 'goods-issue-page-break' : ''
                      }`}
                    >
                      {/* Top Header 02-VT */}
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
                            (Ban hành theo Thông tư số 200/2014/TT-BTC
                          </div>
                          <div className="text-[11px] text-slate-500">
                            ngày 22/12/2014 của Bộ Tài chính)
                          </div>
                        </div>
                      </div>

                      {/* Title */}
                      <div className="text-center py-4">
                        <h2 className="text-xl font-black uppercase tracking-wide text-slate-900 m-0">
                          PHIẾU XUẤT KHO
                        </h2>
                        <div className="text-xs text-slate-600 mt-1">
                          Ngày xuất: <span className="font-semibold">{issueDate.toLocaleString('vi-VN')}</span>
                        </div>
                        <div className="text-xs text-slate-700 mt-0.5">
                          Số phiếu: <span className="font-mono font-bold text-slate-900">{issue.number}</span>
                        </div>
                      </div>

                      {/* Metadata */}
                      <div className="space-y-1.5 text-xs pb-4 border-b border-dashed border-slate-300">
                        <div>
                          <span className="text-slate-500">- Họ và tên người nhận hàng: </span>
                          <span className="font-semibold text-slate-900">
                            {issue.customerName || 'Khách lẻ'}
                          </span>
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

                      {/* Items Table */}
                      <div className="py-4">
                        <table className="w-full text-xs border-collapse border border-slate-300">
                          <thead>
                            <tr className="bg-slate-100 text-slate-700 font-bold">
                              <th className="border border-slate-300 py-2 px-2 text-center w-10">STT</th>
                              <th className="border border-slate-300 py-2 px-2 text-left">Mã SKU</th>
                              <th className="border border-slate-300 py-2 px-2 text-left">
                                Tên, nhãn hiệu, quy cách, phẩm chất vật tư, hàng hóa
                              </th>
                              <th className="border border-slate-300 py-2 px-2 text-center">ĐVT</th>
                              <th className="border border-slate-300 py-2 px-2 text-right">SL xuất</th>
                              <th className="border border-slate-300 py-2 px-2 text-right">
                                SL quy đổi (ĐV gốc)
                              </th>
                              {issueHasCost && (
                                <>
                                  <th className="border border-slate-300 py-2 px-2 text-right">
                                    Đơn giá vốn
                                  </th>
                                  <th className="border border-slate-300 py-2 px-2 text-right">
                                    Thành tiền
                                  </th>
                                </>
                              )}
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
                                {issueHasCost && (
                                  <>
                                    <td className="border border-slate-300 py-1.5 px-2 text-right font-mono">
                                      {item.unitCost !== null ? formatCurrency(item.unitCost) : '—'}
                                    </td>
                                    <td className="border border-slate-300 py-1.5 px-2 text-right font-mono font-semibold">
                                      {item.totalCost !== null ? formatCurrency(item.totalCost) : '—'}
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
                                Cộng phiếu xuất:
                              </td>
                              <td className="border border-slate-300 py-2 px-2 text-right">
                                {formatNumber(issue.totalQuantity)}
                              </td>
                              <td className="border border-slate-300 py-2 px-2 text-right">
                                {formatNumber(issue.totalBaseQuantity)}
                              </td>
                              {issueHasCost && (
                                <>
                                  <td className="border border-slate-300 py-2 px-2 text-right">—</td>
                                  <td className="border border-slate-300 py-2 px-2 text-right font-mono">
                                    {issue.totalCost !== null ? formatCurrency(issue.totalCost) : '—'}
                                  </td>
                                </>
                              )}
                            </tr>
                          </tfoot>
                        </table>
                      </div>

                      {/* Signatures 02-VT */}
                      <div className="pt-6 grid grid-cols-3 gap-6 text-center text-xs text-slate-700">
                        <div>
                          <div className="font-bold text-slate-900">Người lập phiếu</div>
                          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
                          <div className="mt-14 font-semibold text-slate-800">
                            {issue.issuedByName || ''}
                          </div>
                        </div>
                        <div>
                          <div className="font-bold text-slate-900">Thủ kho</div>
                          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
                          <div className="mt-14 font-semibold text-slate-800">&nbsp;</div>
                        </div>
                        <div>
                          <div className="font-bold text-slate-900">Người nhận hàng</div>
                          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
                          <div className="mt-14 font-semibold text-slate-800">
                            {issue.customerName || ''}
                          </div>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            </PrintArea>
          )}
        </div>
      </div>
    </div>
  );
};
