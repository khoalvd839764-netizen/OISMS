import React, { useEffect, useState } from 'react';
import {
  AlertCircle,
  ArrowRight,
  Boxes,
  Clock,
  FileCheck,
  FileText,
  GitBranch,
  Layers,
  Loader2,
  Package,
  ShoppingBag,
  User,
  Warehouse,
  X,
} from 'lucide-react';
import { salesInvoiceService } from '../../services/salesInvoiceService';
import type { DocumentTrace } from '../../types/salesInvoices';

interface SalesInvoiceTraceModalProps {
  invoiceId: string;
  invoiceNumber: string;
  onClose: () => void;
}

export const SalesInvoiceTraceModal: React.FC<SalesInvoiceTraceModalProps> = ({
  invoiceId,
  invoiceNumber,
  onClose,
}) => {
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [trace, setTrace] = useState<DocumentTrace | null>(null);

  useEffect(() => {
    let isMounted = true;
    const fetchTrace = async () => {
      setLoading(true);
      setError(null);
      try {
        const data = await salesInvoiceService.traceInvoiceById(invoiceId);
        if (isMounted) {
          setTrace(data);
        }
      } catch (err: any) {
        if (isMounted) {
          const detail =
            err.response?.data?.detail ||
            err.message ||
            'Không thể truy vết chuỗi chứng từ và dòng sổ kho.';
          setError(detail);
        }
      } finally {
        if (isMounted) {
          setLoading(false);
        }
      }
    };

    fetchTrace();
    return () => {
      isMounted = false;
    };
  }, [invoiceId]);

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-fade-in">
      <div className="bg-white rounded-3xl shadow-2xl border border-slate-200 max-w-4xl w-full overflow-hidden flex flex-col max-h-[92vh]">
        {/* Header */}
        <div className="px-6 py-4 bg-slate-900 text-white flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-indigo-500/20 rounded-xl text-indigo-400">
              <GitBranch className="w-5 h-5" />
            </div>
            <div>
              <h3 className="font-bold text-base leading-tight">
                Truy Vết Chứng Từ & Dòng Sổ Kho Xuất (OUT)
              </h3>
              <p className="text-xs text-slate-400 mt-0.5">
                Hóa đơn: <span className="font-mono text-indigo-300 font-bold">{invoiceNumber}</span>
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6 overflow-y-auto space-y-6">
          {loading && (
            <div className="flex flex-col items-center justify-center py-16 text-slate-500">
              <Loader2 className="w-10 h-10 animate-spin text-indigo-600 mb-3" />
              <p className="text-sm font-semibold">Đang liên kết chuỗi chứng từ và truy vấn sổ kho...</p>
            </div>
          )}

          {error && (
            <div className="p-5 bg-rose-50 border border-rose-200 rounded-2xl text-rose-800 flex items-start gap-3">
              <AlertCircle className="w-6 h-6 shrink-0 mt-0.5" />
              <div>
                <h4 className="font-bold text-sm">Truy vết không thành công</h4>
                <p className="text-xs mt-1 text-rose-700">{error}</p>
              </div>
            </div>
          )}

          {!loading && !error && trace && (
            <>
              {/* Document Flow Pipeline Visual */}
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4 relative">
                {/* Step 1: Hóa đơn */}
                <div className="bg-gradient-to-br from-indigo-50 to-blue-50/50 border border-indigo-100 rounded-2xl p-4 shadow-xs relative">
                  <div className="flex items-center justify-between mb-3">
                    <span className="text-[11px] font-bold uppercase tracking-wider text-indigo-600 bg-indigo-100/80 px-2.5 py-0.5 rounded-full">
                      1. Hóa Đơn Bán Lẻ
                    </span>
                    <FileText className="w-4 h-4 text-indigo-500" />
                  </div>
                  <div className="font-mono font-bold text-base text-slate-900 mb-1">
                    {trace.salesInvoice?.number || invoiceNumber}
                  </div>
                  <div className="space-y-1 text-xs text-slate-600">
                    <div className="flex items-center gap-1.5">
                      <Clock className="w-3.5 h-3.5 text-slate-400" />
                      <span>{trace.salesInvoice?.issuedAt ? new Date(trace.salesInvoice.issuedAt).toLocaleString('vi-VN') : '-'}</span>
                    </div>
                    {trace.salesInvoice?.issuedByName && (
                      <div className="flex items-center gap-1.5">
                        <User className="w-3.5 h-3.5 text-slate-400" />
                        <span>Người lập: {trace.salesInvoice.issuedByName}</span>
                      </div>
                    )}
                    <div className="pt-2 font-bold text-indigo-900 text-sm">
                      Tổng tiền: {formatCurrency(trace.salesInvoice?.totalAmount || 0)}
                    </div>
                  </div>
                </div>

                {/* Arrow desktop */}
                <div className="hidden md:flex items-center justify-center absolute left-[31.5%] top-1/2 -translate-y-1/2 z-10">
                  <div className="w-7 h-7 rounded-full bg-white shadow-md border border-slate-200 flex items-center justify-center text-slate-400">
                    <ArrowRight className="w-4 h-4" />
                  </div>
                </div>

                {/* Step 2: Đơn hàng nguồn */}
                <div className="bg-gradient-to-br from-amber-50 to-orange-50/50 border border-amber-100 rounded-2xl p-4 shadow-xs relative">
                  <div className="flex items-center justify-between mb-3">
                    <span className="text-[11px] font-bold uppercase tracking-wider text-amber-700 bg-amber-100 px-2.5 py-0.5 rounded-full">
                      2. Đơn Hàng Nguồn ({trace.source?.type || 'Order'})
                    </span>
                    <ShoppingBag className="w-4 h-4 text-amber-600" />
                  </div>
                  <div className="font-mono font-bold text-base text-slate-900 mb-1">
                    {trace.source?.number || 'Chưa liên kết'}
                  </div>
                  <div className="space-y-1 text-xs text-slate-600">
                    <div className="flex items-center gap-1.5">
                      <Clock className="w-3.5 h-3.5 text-slate-400" />
                      <span>{trace.source?.actedAt ? new Date(trace.source.actedAt).toLocaleString('vi-VN') : '-'}</span>
                    </div>
                    {trace.source?.actorName && (
                      <div className="flex items-center gap-1.5">
                        <User className="w-3.5 h-3.5 text-slate-400" />
                        <span>Người tạo: {trace.source.actorName}</span>
                      </div>
                    )}
                    <div className="pt-2 font-semibold text-amber-900 text-xs">
                      Trạng thái: <span className="font-bold">{trace.source?.status || 'Completed'}</span>
                    </div>
                  </div>
                </div>

                {/* Arrow desktop */}
                <div className="hidden md:flex items-center justify-center absolute left-[65%] top-1/2 -translate-y-1/2 z-10">
                  <div className="w-7 h-7 rounded-full bg-white shadow-md border border-slate-200 flex items-center justify-center text-slate-400">
                    <ArrowRight className="w-4 h-4" />
                  </div>
                </div>

                {/* Step 3: Dòng sổ kho */}
                <div className="bg-gradient-to-br from-emerald-50 to-teal-50/50 border border-emerald-100 rounded-2xl p-4 shadow-xs">
                  <div className="flex items-center justify-between mb-3">
                    <span className="text-[11px] font-bold uppercase tracking-wider text-emerald-700 bg-emerald-100 px-2.5 py-0.5 rounded-full">
                      3. Sổ Kho Xuất (OUT)
                    </span>
                    <Boxes className="w-4 h-4 text-emerald-600" />
                  </div>
                  <div className="font-mono font-bold text-base text-slate-900 mb-1">
                    {trace.lines?.length || 0} bút toán OUT
                  </div>
                  <div className="space-y-1 text-xs text-slate-600">
                    <div className="flex items-center gap-1.5">
                      <Layers className="w-3.5 h-3.5 text-slate-400" />
                      <span>Loại bút toán: Xuất bán hàng (OUT)</span>
                    </div>
                    <div className="pt-2 font-semibold text-emerald-900 text-xs">
                      Tồn kho đã cập nhật tự động
                    </div>
                  </div>
                </div>
              </div>

              {/* Step 3 Detail Table: Dòng sổ kho OUT */}
              <div className="border border-slate-200 rounded-2xl overflow-hidden shadow-xs">
                <div className="bg-slate-50 px-5 py-3 border-b border-slate-200 flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <FileCheck className="w-4 h-4 text-emerald-600" />
                    <h4 className="font-bold text-sm text-slate-800">
                      Chi Tiết Dòng Sổ Kho Xuất Hàng (Inventory Ledger - OUT)
                    </h4>
                  </div>
                  <span className="text-xs text-slate-500 font-medium">
                    (Giá vốn WAC chỉ hiển thị cho Chủ cửa hàng & Nhân viên)
                  </span>
                </div>

                {(!trace.lines || trace.lines.length === 0) ? (
                  <div className="p-8 text-center text-slate-500 text-xs">
                    Không tìm thấy dòng sổ kho nào được liên kết với hóa đơn này.
                  </div>
                ) : (
                  <div className="overflow-x-auto">
                    <table className="w-full text-left text-xs">
                      <thead className="bg-slate-100/70 border-b border-slate-200 text-slate-600 font-bold uppercase text-[10px] tracking-wider">
                        <tr>
                          <th className="py-2.5 px-3">Bút toán #</th>
                          <th className="py-2.5 px-3">Mặt hàng / SKU</th>
                          <th className="py-2.5 px-3">Kho hàng</th>
                          <th className="py-2.5 px-3 text-right">Lượng giao dịch</th>
                          <th className="py-2.5 px-3 text-right">Lượng xuất gốc</th>
                          <th className="py-2.5 px-3 text-right">Giá vốn (WAC)</th>
                          <th className="py-2.5 px-3 text-right">Tồn sau xuất</th>
                          <th className="py-2.5 px-3 text-right">Thời gian ghi sổ</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-slate-100 font-mono">
                        {trace.lines.map((line, idx) => (
                          <tr key={line.ledgerId || idx} className="hover:bg-slate-50/80 transition-colors">
                            <td className="py-2.5 px-3 text-slate-700">
                              <span className="font-bold text-slate-900">#{line.sequenceNo}</span>
                              <div className="text-[10px] text-slate-400 truncate max-w-[100px]" title={line.ledgerId}>
                                {line.ledgerId.slice(0, 8)}...
                              </div>
                            </td>
                            <td className="py-2.5 px-3 font-sans">
                              <div className="flex items-center gap-1.5 font-bold font-mono text-slate-800">
                                <Package className="w-3.5 h-3.5 text-slate-400 shrink-0" />
                                <span>{line.skuCode}</span>
                              </div>
                            </td>
                            <td className="py-2.5 px-3 font-sans text-slate-700">
                              <div className="flex items-center gap-1.5">
                                <Warehouse className="w-3.5 h-3.5 text-slate-400 shrink-0" />
                                <span>{line.warehouseName}</span>
                              </div>
                            </td>
                            <td className="py-2.5 px-3 text-right font-sans">
                              {line.orderItemQuantity != null ? (
                                <span className="font-semibold text-slate-800">
                                  {line.orderItemQuantity.toLocaleString('vi-VN')} {line.unitName || ''}
                                </span>
                              ) : (
                                <span className="text-slate-400">-</span>
                              )}
                            </td>
                            <td className="py-2.5 px-3 text-right font-bold text-rose-600">
                              -{Math.abs(line.quantity).toLocaleString('vi-VN')} (gốc)
                            </td>
                            <td className="py-2.5 px-3 text-right text-slate-700">
                              {formatCurrency(line.unitCost)}
                            </td>
                            <td className="py-2.5 px-3 text-right font-semibold text-slate-900">
                              {line.balanceAfter.toLocaleString('vi-VN')}
                            </td>
                            <td className="py-2.5 px-3 text-right font-sans text-slate-500 text-[11px]">
                              {new Date(line.createdAt).toLocaleString('vi-VN')}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            </>
          )}
        </div>

        {/* Footer */}
        <div className="px-6 py-3.5 bg-slate-50 border-t border-slate-200 flex justify-end">
          <button
            type="button"
            onClick={onClose}
            className="px-5 py-2 bg-slate-200 hover:bg-slate-300 text-slate-800 font-semibold rounded-xl text-xs transition-colors"
          >
            Đóng
          </button>
        </div>
      </div>
    </div>
  );
};
