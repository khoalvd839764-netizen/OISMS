import React, { useEffect, useState } from 'react';
import { CheckCircle2, Loader2, Printer, X, AlertCircle } from 'lucide-react';
import { salesInvoiceService } from '../../services/salesInvoiceService';
import type { SalesInvoiceReceipt } from '../../types/salesInvoices';

interface SalesInvoiceReprintModalProps {
  invoiceId?: string;
  invoiceNumber?: string;
  onClose: () => void;
}

export const SalesInvoiceReprintModal: React.FC<SalesInvoiceReprintModalProps> = ({
  invoiceId,
  invoiceNumber,
  onClose,
}) => {
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [receipt, setReceipt] = useState<SalesInvoiceReceipt | null>(null);
  const [is58, setIs58] = useState<boolean>(false);

  useEffect(() => {
    let isMounted = true;
    const fetchReceipt = async () => {
      setLoading(true);
      setError(null);
      try {
        let data: SalesInvoiceReceipt;
        if (invoiceId) {
          data = await salesInvoiceService.getReceiptById(invoiceId);
        } else if (invoiceNumber) {
          data = await salesInvoiceService.getReceiptByNumber(invoiceNumber);
        } else {
          throw new Error('Không có mã hóa đơn hoặc số hóa đơn để tải biên lai.');
        }
        if (isMounted) {
          setReceipt(data);
        }
      } catch (err: any) {
        if (isMounted) {
          const detail = err.response?.data?.detail || err.message || 'Không thể tải dữ liệu hóa đơn.';
          setError(detail);
        }
      } finally {
        if (isMounted) {
          setLoading(false);
        }
      }
    };

    fetchReceipt();
    return () => {
      isMounted = false;
    };
  }, [invoiceId, invoiceNumber]);

  const handlePrint = () => {
    window.print();
  };

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-fade-in no-print-bg">
      <div className="bg-white rounded-3xl shadow-2xl border border-slate-200 max-w-lg w-full overflow-hidden flex flex-col max-h-[95vh] no-print-border">
        {/* Header Modal Bar */}
        <div className="px-5 py-3.5 text-white flex items-center justify-between no-print bg-slate-900">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="w-5 h-5 text-emerald-400" />
            <span className="font-bold text-sm">
              In Lại Hóa Đơn Bán Hàng ({is58 ? 'K58 - 58mm' : 'K80 - 80mm'})
            </span>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-1 rounded-lg text-slate-300 hover:text-white hover:bg-slate-800 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Action Button & Paper Size Strip */}
        <div className="p-3 border-b flex items-center justify-between no-print bg-indigo-50 border-indigo-100">
          <div className="flex items-center gap-1.5 bg-white p-1 rounded-lg border border-slate-200 text-xs font-semibold">
            <button
              type="button"
              onClick={() => setIs58(false)}
              className={`px-2.5 py-1 rounded-md transition-colors ${
                !is58 ? 'bg-indigo-600 text-white shadow-xs' : 'text-slate-600 hover:text-slate-900'
              }`}
            >
              Khổ 80mm
            </button>
            <button
              type="button"
              onClick={() => setIs58(true)}
              className={`px-2.5 py-1 rounded-md transition-colors ${
                is58 ? 'bg-indigo-600 text-white shadow-xs' : 'text-slate-600 hover:text-slate-900'
              }`}
            >
              Khổ 58mm
            </button>
          </div>

          <button
            type="button"
            onClick={handlePrint}
            disabled={loading || !receipt}
            className="inline-flex items-center gap-1.5 px-4 py-1.5 text-white bg-indigo-600 hover:bg-indigo-700 disabled:bg-slate-300 rounded-xl font-bold text-xs shadow-md transition-all"
          >
            <Printer className="w-4 h-4" />
            <span>In Phiếu (Ctrl + P)</span>
          </button>
        </div>

        {/* Body content */}
        <div className="p-6 overflow-y-auto bg-slate-100/50 flex justify-center print-container min-h-[300px]">
          {loading && (
            <div className="flex flex-col items-center justify-center p-8 text-slate-500">
              <Loader2 className="w-8 h-8 animate-spin text-indigo-600 mb-2" />
              <p className="text-sm font-medium">Đang tải hóa đơn...</p>
            </div>
          )}

          {error && (
            <div className="p-4 bg-rose-50 border border-rose-200 rounded-xl text-rose-700 flex items-start gap-2.5 max-w-sm">
              <AlertCircle className="w-5 h-5 shrink-0 mt-0.5" />
              <div>
                <div className="font-bold text-sm">Không thể tải hóa đơn</div>
                <div className="text-xs mt-0.5">{error}</div>
              </div>
            </div>
          )}

          {!loading && !error && receipt && (
            <div
              className={`receipt-paper bg-white p-4 border border-dashed border-slate-300 text-slate-900 font-sans text-xs shadow-sm relative transition-all ${
                is58 ? 'max-w-[260px] text-[10px]' : 'max-w-[340px]'
              } w-full`}
            >
              {/* Branding */}
              <div className="text-center pb-3 border-b border-dashed border-slate-300">
                <h2 className="text-base font-extrabold tracking-tight uppercase text-slate-900 m-0">
                  CỬA HÀNG OISMS
                </h2>
                <div className="text-[11px] text-slate-600 font-medium mt-0.5">
                  {receipt.branchName || 'Chi nhánh hệ thống'}
                </div>
                {receipt.branchAddress && (
                  <div className="text-[10px] text-slate-500 mt-0.5">
                    Đ/c: {receipt.branchAddress}
                  </div>
                )}
                {receipt.branchPhone && (
                  <div className="text-[10px] text-slate-500">
                    Hotline: {receipt.branchPhone}
                  </div>
                )}
              </div>

              {/* Title & Metadata */}
              <div className="py-2.5 text-center border-b border-dashed border-slate-300">
                <div className="text-sm font-black tracking-wider uppercase text-slate-900">
                  HÓA ĐƠN BÁN HÀNG
                </div>
                <div className="text-[10px] text-slate-500 mt-0.5">
                  Số HĐ: <span className="font-mono font-bold text-slate-800">{receipt.receiptNumber}</span>
                </div>
                {receipt.orderNumber && receipt.orderNumber !== receipt.receiptNumber && (
                  <div className="text-[10px] text-slate-500">
                    Số đơn hàng: <span className="font-mono text-slate-700">{receipt.orderNumber}</span>
                  </div>
                )}
                <div className="text-[10px] text-slate-500">
                  Ngày lập: {new Date(receipt.createdAt).toLocaleString('vi-VN')}
                </div>
                {receipt.cashierName && (
                  <div className="text-[10px] text-slate-500">
                    Thu ngân: <span className="font-semibold text-slate-700">{receipt.cashierName}</span>
                  </div>
                )}
                {receipt.customerName && (
                  <div className="text-[10px] text-slate-500">
                    Khách: {receipt.customerName} {receipt.customerPhone ? `(${receipt.customerPhone})` : ''}
                  </div>
                )}
              </div>

              {/* Items Table */}
              <div className="py-2.5 border-b border-dashed border-slate-300">
                <table className="w-full text-[11px]">
                  <thead>
                    <tr className="border-b border-slate-200 text-slate-500 font-bold">
                      <th className="text-left pb-1">Mặt hàng</th>
                      <th className="text-center pb-1 w-8">ĐVT</th>
                      <th className="text-center pb-1 w-8">SL</th>
                      <th className="text-right pb-1">Đơn giá</th>
                      <th className="text-right pb-1">T.Tiền</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {receipt.items.map((item, idx) => (
                      <tr key={idx} className="align-top">
                        <td className="py-1 pr-1">
                          <div className="font-semibold text-slate-800 leading-tight">
                            {item.productName}{item.variantName ? ` - ${item.variantName}` : ''}
                          </div>
                          <div className="text-[9px] text-slate-400 font-mono">
                            {item.skuCode}
                          </div>
                        </td>
                        <td className="py-1 text-center text-slate-500 text-[10px]">
                          {item.unitName || '-'}
                        </td>
                        <td className="py-1 text-center font-bold text-slate-700">
                          {item.quantity}
                        </td>
                        <td className="py-1 text-right text-slate-600 whitespace-nowrap">
                          {item.unitPrice.toLocaleString('vi-VN')}
                        </td>
                        <td className="py-1 text-right font-semibold text-slate-900 whitespace-nowrap">
                          {item.subtotal.toLocaleString('vi-VN')}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {/* Totals */}
              <div className="py-2.5 space-y-1 text-[11px] border-b border-dashed border-slate-300">
                <div className="flex justify-between items-center text-sm font-black pt-1">
                  <span>TỔNG CỘNG:</span>
                  <span className="font-mono text-base">{formatCurrency(receipt.totalAmount)}</span>
                </div>
                {receipt.paymentMethod && (
                  <div className="flex justify-between text-slate-600 pt-1">
                    <span>Hình thức:</span>
                    <span className="font-bold">
                      {receipt.paymentMethod === 'CASH' ? 'Tiền mặt' : receipt.paymentMethod}
                    </span>
                  </div>
                )}
                {receipt.amountGiven != null && (
                  <div className="flex justify-between text-slate-600">
                    <span>Tiền khách đưa:</span>
                    <span className="font-mono font-medium">{formatCurrency(receipt.amountGiven)}</span>
                  </div>
                )}
                {receipt.changeDue != null && (
                  <div className="flex justify-between text-slate-900 font-bold">
                    <span>Tiền thừa trả:</span>
                    <span className="font-mono">{formatCurrency(receipt.changeDue)}</span>
                  </div>
                )}
              </div>

              {/* Footer */}
              <div className="pt-3 text-center space-y-1">
                <div className="font-bold text-[11px] text-slate-800">
                  CẢM ƠN QUÝ KHÁCH & HẸN GẶP LẠI!
                </div>
                <div className="text-[9px] text-slate-400">
                  (Bản in lại từ hệ thống OISMS)
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
