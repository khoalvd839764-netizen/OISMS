import React, { useEffect, useState } from 'react';
import {
  AlertCircle,
  Building2,
  Calendar,
  FileText,
  GitBranch,
  Loader2,
  Package,
  Printer,
  ShoppingBag,
  User,
  X,
} from 'lucide-react';
import { salesInvoiceService } from '../../services/salesInvoiceService';
import type { SalesInvoiceReceipt, SalesInvoiceSummary } from '../../types/salesInvoices';

interface SalesInvoiceDetailModalProps {
  invoiceId: string;
  onClose: () => void;
  onOpenReprint: (invoiceId: string, invoiceNumber: string) => void;
  onOpenTrace: (invoiceId: string, invoiceNumber: string) => void;
}

export const SalesInvoiceDetailModal: React.FC<SalesInvoiceDetailModalProps> = ({
  invoiceId,
  onClose,
  onOpenReprint,
  onOpenTrace,
}) => {
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [invoice, setInvoice] = useState<SalesInvoiceSummary | null>(null);
  const [receipt, setReceipt] = useState<SalesInvoiceReceipt | null>(null);

  useEffect(() => {
    let isMounted = true;
    const fetchDetails = async () => {
      setLoading(true);
      setError(null);
      try {
        const [invData, recData] = await Promise.all([
          salesInvoiceService.getSalesInvoiceById(invoiceId),
          salesInvoiceService.getReceiptById(invoiceId).catch(() => null),
        ]);
        if (isMounted) {
          setInvoice(invData);
          setReceipt(recData);
        }
      } catch (err: any) {
        if (isMounted) {
          const detail =
            err.response?.data?.detail ||
            err.message ||
            'Không thể tải chi tiết hóa đơn bán hàng.';
          setError(detail);
        }
      } finally {
        if (isMounted) {
          setLoading(false);
        }
      }
    };

    fetchDetails();
    return () => {
      isMounted = false;
    };
  }, [invoiceId]);

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-fade-in">
      <div className="bg-white rounded-3xl shadow-2xl border border-slate-200 max-w-3xl w-full overflow-hidden flex flex-col max-h-[90vh]">
        {/* Header */}
        <div className="px-6 py-4 bg-slate-900 text-white flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-indigo-500/20 rounded-xl text-indigo-400">
              <FileText className="w-5 h-5" />
            </div>
            <div>
              <h3 className="font-bold text-base leading-tight">
                Chi Tiết Hóa Đơn Bán Hàng
              </h3>
              <p className="text-xs text-slate-400 mt-0.5">
                Số HĐ: <span className="font-mono text-indigo-300 font-bold">{invoice?.number || '...'}</span>
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
              <p className="text-sm font-semibold">Đang tải thông tin hóa đơn...</p>
            </div>
          )}

          {error && (
            <div className="p-5 bg-rose-50 border border-rose-200 rounded-2xl text-rose-800 flex items-start gap-3">
              <AlertCircle className="w-6 h-6 shrink-0 mt-0.5" />
              <div>
                <h4 className="font-bold text-sm">Không thể tải dữ liệu</h4>
                <p className="text-xs mt-1 text-rose-700">{error}</p>
              </div>
            </div>
          )}

          {!loading && !error && invoice && (
            <>
              {/* Meta Grid */}
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 bg-slate-50 p-4 rounded-2xl border border-slate-200 text-xs">
                <div>
                  <span className="text-slate-400 block text-[10px] uppercase font-bold">Số hóa đơn</span>
                  <span className="font-mono font-bold text-indigo-600 text-sm">{invoice.number}</span>
                </div>
                <div>
                  <span className="text-slate-400 block text-[10px] uppercase font-bold">Mã đơn hàng</span>
                  <div className="flex items-center gap-1 font-mono font-bold text-slate-800 text-sm">
                    <ShoppingBag className="w-3.5 h-3.5 text-slate-400" />
                    <span>{invoice.orderNumber}</span>
                  </div>
                </div>
                <div>
                  <span className="text-slate-400 block text-[10px] uppercase font-bold">Chi nhánh</span>
                  <div className="flex items-center gap-1 font-semibold text-slate-700">
                    <Building2 className="w-3.5 h-3.5 text-slate-400" />
                    <span>{invoice.branchName}</span>
                  </div>
                </div>
                <div>
                  <span className="text-slate-400 block text-[10px] uppercase font-bold">Kênh bán</span>
                  <span className="inline-block px-2 py-0.5 rounded-full text-[10px] font-bold bg-indigo-100 text-indigo-700">
                    {invoice.channel || 'POS'}
                  </span>
                </div>
                <div>
                  <span className="text-slate-400 block text-[10px] uppercase font-bold">Khách hàng</span>
                  <div className="flex items-center gap-1 font-semibold text-slate-700">
                    <User className="w-3.5 h-3.5 text-slate-400" />
                    <span>{invoice.customerName || 'Khách lẻ'}</span>
                  </div>
                </div>
                <div>
                  <span className="text-slate-400 block text-[10px] uppercase font-bold">Người lập</span>
                  <span className="font-semibold text-slate-700">{invoice.issuedByName || 'Thu ngân'}</span>
                </div>
                <div>
                  <span className="text-slate-400 block text-[10px] uppercase font-bold">Thời gian lập</span>
                  <div className="flex items-center gap-1 font-sans text-slate-600">
                    <Calendar className="w-3.5 h-3.5 text-slate-400" />
                    <span>{new Date(invoice.issuedAt).toLocaleString('vi-VN')}</span>
                  </div>
                </div>
                <div>
                  <span className="text-slate-400 block text-[10px] uppercase font-bold">Tổng tiền</span>
                  <span className="font-mono font-bold text-emerald-600 text-sm">
                    {formatCurrency(invoice.totalAmount)}
                  </span>
                </div>
              </div>

              {/* Items Breakdown */}
              <div className="border border-slate-200 rounded-2xl overflow-hidden shadow-xs">
                <div className="bg-slate-50 px-4 py-2.5 border-b border-slate-200 font-bold text-xs text-slate-700 flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <Package className="w-4 h-4 text-indigo-600" />
                    <span>Danh Sách Mặt Hàng Xuất Hóa Đơn</span>
                  </div>
                  <span className="text-[11px] font-normal text-slate-500">
                    {receipt?.items?.length || 0} sản phẩm
                  </span>
                </div>

                {receipt?.items && receipt.items.length > 0 ? (
                  <table className="w-full text-left text-xs">
                    <thead className="bg-slate-100/60 border-b border-slate-200 text-slate-600 font-bold uppercase text-[10px] tracking-wider">
                      <tr>
                        <th className="py-2.5 px-4">Tên sản phẩm / Phân loại</th>
                        <th className="py-2.5 px-4">Mã SKU</th>
                        <th className="py-2.5 px-3 text-center">ĐVT</th>
                        <th className="py-2.5 px-3 text-right">Số lượng</th>
                        <th className="py-2.5 px-4 text-right">Đơn giá</th>
                        <th className="py-2.5 px-4 text-right">Thành tiền</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {receipt.items.map((item, idx) => (
                        <tr key={item.skuId || idx} className="hover:bg-slate-50/60 transition-colors">
                          <td className="py-2.5 px-4">
                            <div className="font-semibold text-slate-800">
                              {item.productName}
                            </div>
                            {item.variantName && (
                              <div className="text-[11px] text-slate-500">
                                {item.variantName}
                              </div>
                            )}
                          </td>
                          <td className="py-2.5 px-4 font-mono text-slate-600">
                            {item.skuCode}
                          </td>
                          <td className="py-2.5 px-3 text-center text-slate-600">
                            <span className="px-2 py-0.5 bg-slate-100 rounded text-[11px] font-medium">
                              {item.unitName || '-'}
                            </span>
                          </td>
                          <td className="py-2.5 px-3 text-right font-bold text-slate-800">
                            {item.quantity.toLocaleString('vi-VN')}
                          </td>
                          <td className="py-2.5 px-4 text-right font-mono text-slate-600">
                            {formatCurrency(item.unitPrice)}
                          </td>
                          <td className="py-2.5 px-4 text-right font-mono font-bold text-slate-900">
                            {formatCurrency(item.subtotal)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                ) : (
                  <div className="p-8 text-center text-slate-400 text-xs">
                    Không có danh sách mặt hàng chi tiết.
                  </div>
                )}
              </div>
            </>
          )}
        </div>

        {/* Footer Actions */}
        <div className="px-6 py-4 bg-slate-50 border-t border-slate-200 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={loading || !invoice}
              onClick={() => {
                if (invoice) onOpenReprint(invoice.id, invoice.number);
              }}
              className="inline-flex items-center gap-1.5 px-4 py-2 bg-indigo-600 hover:bg-indigo-700 disabled:bg-slate-300 text-white font-semibold rounded-xl text-xs shadow-xs transition-colors"
            >
              <Printer className="w-4 h-4" />
              <span>In Lại Hóa Đơn</span>
            </button>
            <button
              type="button"
              disabled={loading || !invoice}
              onClick={() => {
                if (invoice) onOpenTrace(invoice.id, invoice.number);
              }}
              className="inline-flex items-center gap-1.5 px-4 py-2 bg-emerald-600 hover:bg-emerald-700 disabled:bg-slate-300 text-white font-semibold rounded-xl text-xs shadow-xs transition-colors"
            >
              <GitBranch className="w-4 h-4" />
              <span>Truy Vết Xuất Kho (Trace)</span>
            </button>
          </div>

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
