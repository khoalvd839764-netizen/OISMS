import React from 'react';
import type { OrderDetail } from '../../types/ordersCenter';
import type { SalesInvoiceReceipt } from '../../types/salesInvoices';
import { PrintDocumentFooter } from './PrintDocumentFooter';
import { formatPrintCurrency, formatPrintDateTimeVn, formatPrintNumber } from './printFormatters';

interface LegacyOrderPrintPaperProps {
  receipt?: SalesInvoiceReceipt | null;
  order?: OrderDetail | null;
  note?: string;
  title?: string;
  tenantName?: string;
  printedByName?: string | null;
  pageBreakAfter?: boolean;
}

export const LegacyOrderPrintPaper: React.FC<LegacyOrderPrintPaperProps> = ({
  receipt,
  order,
  note,
  title,
  tenantName,
  printedByName,
  pageBreakAfter = false,
}) => {
  const docTitle =
    title ||
    (receipt ? 'HÓA ĐƠN BÁN HÀNG' : order && order.channel !== 'POS' ? 'PHIẾU GIAO HÀNG' : 'ĐƠN HÀNG BÁN LẺ');
  const docNumber = receipt?.receiptNumber || order?.number || '—';
  const createdAt = receipt?.createdAt || order?.createdAt;
  const branchName = receipt?.branchName || order?.branchName || 'Chi nhánh hệ thống';
  const customerName = receipt?.customerName || order?.customerName || 'Khách lẻ';
  const customerPhone = receipt?.customerPhone || order?.customerPhone;
  const totalAmount = receipt?.totalAmount ?? order?.totalAmount ?? 0;

  return (
    <div
      className={`doc-print-paper bg-white border border-slate-300 text-slate-900 font-sans shadow-sm p-8 text-sm ${
        pageBreakAfter ? 'doc-print-page-break' : ''
      }`}
    >
      <div className="flex justify-between items-start pb-4 border-b border-slate-300">
        <div>
          <div className="font-extrabold uppercase text-base text-slate-900">
            {tenantName || 'CỬA HÀNG OISMS'}
          </div>
          <div className="text-xs text-slate-600 mt-0.5">
            Chi nhánh: <span className="font-semibold text-slate-800">{branchName}</span>
          </div>
        </div>
        <div className="text-right text-xs">
          <div className="font-bold text-slate-900">{docTitle}</div>
          {order?.channel && <div className="text-[11px] text-slate-500">Kênh: {order.channel}</div>}
        </div>
      </div>

      <div className="text-center py-4">
        <h2 className="text-xl font-black uppercase tracking-wide text-slate-900 m-0">{docTitle}</h2>
        <div className="text-xs text-slate-600 mt-1">
          Ngày lập: <span className="font-semibold">{formatPrintDateTimeVn(createdAt)}</span>
        </div>
        <div className="text-xs text-slate-700 mt-0.5">
          Số chứng từ: <span className="font-mono font-bold text-slate-900">{docNumber}</span>
        </div>
        {note && (
          <div className="mt-2 inline-block px-3 py-1 bg-amber-50 border border-amber-200 text-amber-800 rounded text-xs font-medium">
            {note}
          </div>
        )}
      </div>

      <div className="space-y-1.5 text-xs pb-4 border-b border-dashed border-slate-300">
        <div>
          <span className="text-slate-500">- Khách hàng: </span>
          <span className="font-semibold text-slate-900">
            {customerName}
            {customerPhone ? ` (${customerPhone})` : ''}
          </span>
        </div>
        {order?.shippingAddress && (
          <div>
            <span className="text-slate-500">- Địa chỉ giao hàng: </span>
            <span className="text-slate-800">{order.shippingAddress}</span>
          </div>
        )}
        {receipt?.orderNumber && (
          <div>
            <span className="text-slate-500">- Đơn hàng liên quan: </span>
            <span className="font-mono font-semibold text-slate-900">{receipt.orderNumber}</span>
          </div>
        )}
      </div>

      <div className="py-4">
        <table className="w-full text-xs border-collapse border border-slate-300">
          <thead>
            <tr className="bg-slate-100 text-slate-700 font-bold">
              <th className="border border-slate-300 py-2 px-2 text-center w-10">STT</th>
              <th className="border border-slate-300 py-2 px-2 text-left">Mã SKU</th>
              <th className="border border-slate-300 py-2 px-2 text-left">Tên hàng hóa</th>
              <th className="border border-slate-300 py-2 px-2 text-center">ĐVT</th>
              <th className="border border-slate-300 py-2 px-2 text-right">Số lượng</th>
              <th className="border border-slate-300 py-2 px-2 text-right">Đơn giá bán</th>
              <th className="border border-slate-300 py-2 px-2 text-right">Thành tiền</th>
            </tr>
          </thead>
          <tbody>
            {receipt
              ? receipt.items.map((item, idx) => (
                  <tr key={`${item.skuId}-${idx}`}>
                    <td className="border border-slate-300 py-1.5 px-2 text-center">{idx + 1}</td>
                    <td className="border border-slate-300 py-1.5 px-2 font-mono font-semibold">
                      {item.skuCode}
                    </td>
                    <td className="border border-slate-300 py-1.5 px-2 font-medium text-slate-900">
                      {item.productName}
                    </td>
                    <td className="border border-slate-300 py-1.5 px-2 text-center">
                      {item.unitName || '—'}
                    </td>
                    <td className="border border-slate-300 py-1.5 px-2 text-right font-semibold">
                      {formatPrintNumber(item.quantity)}
                    </td>
                    <td className="border border-slate-300 py-1.5 px-2 text-right font-mono">
                      {formatPrintCurrency(item.unitPrice)}
                    </td>
                    <td className="border border-slate-300 py-1.5 px-2 text-right font-mono font-semibold">
                      {formatPrintCurrency(item.subtotal)}
                    </td>
                  </tr>
                ))
              : (order?.items || []).map((item, idx) => (
                  <tr key={item.id || idx}>
                    <td className="border border-slate-300 py-1.5 px-2 text-center">{idx + 1}</td>
                    <td className="border border-slate-300 py-1.5 px-2 font-mono font-semibold">
                      {item.skuCode}
                    </td>
                    <td className="border border-slate-300 py-1.5 px-2 font-medium text-slate-900">
                      {item.skuName || '—'}
                    </td>
                    <td className="border border-slate-300 py-1.5 px-2 text-center">
                      {item.unitName || '—'}
                    </td>
                    <td className="border border-slate-300 py-1.5 px-2 text-right font-semibold">
                      {formatPrintNumber(item.quantity)}
                    </td>
                    <td className="border border-slate-300 py-1.5 px-2 text-right font-mono">
                      {formatPrintCurrency(item.unitPrice)}
                    </td>
                    <td className="border border-slate-300 py-1.5 px-2 text-right font-mono font-semibold">
                      {formatPrintCurrency(item.quantity * item.unitPrice)}
                    </td>
                  </tr>
                ))}
          </tbody>
          <tfoot>
            <tr className="bg-slate-50 font-bold text-slate-900">
              <td colSpan={6} className="border border-slate-300 py-2 px-2 text-right uppercase">
                Tổng thanh toán:
              </td>
              <td className="border border-slate-300 py-2 px-2 text-right font-mono">
                {formatPrintCurrency(totalAmount)}
              </td>
            </tr>
          </tfoot>
        </table>
      </div>

      <PrintDocumentFooter printedByName={printedByName} />
    </div>
  );
};
