import React from 'react';
import type { SalesReturn } from '../../types/salesReturns';
import { PrintDocumentFooter } from './PrintDocumentFooter';
import { formatPrintCurrency, formatPrintDateTimeVn, formatPrintNumber } from './printFormatters';

interface SalesReturnPrintPaperProps {
  salesReturn: SalesReturn;
  tenantName?: string;
  printedByName?: string | null;
  pageBreakAfter?: boolean;
}

export const SalesReturnPrintPaper: React.FC<SalesReturnPrintPaperProps> = ({
  salesReturn,
  tenantName,
  printedByName,
  pageBreakAfter = false,
}) => {
  // Giá vốn gốc chỉ hiển thị khi API trả về khác null (Owner); Staff nhận null sẽ ẩn cột này (Q7)
  const showCost = salesReturn.items.some(
    (i) => i.originalUnitCost !== null && i.originalUnitCost !== undefined
  );

  const totalQty = salesReturn.items.reduce((s, i) => s + (i.quantity || 0), 0);
  const totalBaseQty = salesReturn.items.reduce((s, i) => s + (i.baseQuantity || 0), 0);

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
            Chi nhánh:{' '}
            <span className="font-semibold text-slate-800">{salesReturn.branchName}</span>
          </div>
          <div className="text-xs text-slate-600">
            Kho nhận lại:{' '}
            <span className="font-semibold text-slate-800">{salesReturn.warehouseName}</span>
          </div>
        </div>
        <div className="text-right text-xs">
          <div className="font-bold text-slate-900">Phiếu nhập hàng trả lại</div>
          <div className="text-[11px] text-slate-500">Chứng từ nhập kho hàng bán bị trả</div>
        </div>
      </div>

      <div className="text-center py-4">
        <h2 className="text-xl font-black uppercase tracking-wide text-slate-900 m-0">
          PHIẾU NHẬP KHO HÀNG TRẢ LẠI
        </h2>
        <div className="text-xs text-slate-600 mt-1">
          Ngày chứng từ:{' '}
          <span className="font-semibold">
            {formatPrintDateTimeVn(salesReturn.confirmedAt || salesReturn.createdAt)}
          </span>
        </div>
        <div className="text-xs text-slate-700 mt-0.5">
          Số phiếu: <span className="font-mono font-bold text-slate-900">{salesReturn.number}</span>
          <span className="ml-2 text-slate-500">({salesReturn.status})</span>
        </div>
      </div>

      <div className="space-y-1.5 text-xs pb-4 border-b border-dashed border-slate-300">
        <div className="grid grid-cols-2 gap-2">
          <div>
            <span className="text-slate-500">- Khách hàng: </span>
            <span className="font-semibold text-slate-900">
              {salesReturn.customerName || 'Khách lẻ'}
            </span>
          </div>
          <div>
            <span className="text-slate-500">- Đơn hàng gốc: </span>
            <span className="font-mono font-semibold text-slate-900">
              {salesReturn.orderNumber} ({salesReturn.orderChannel})
            </span>
          </div>
        </div>
        <div>
          <span className="text-slate-500">- Lý do trả hàng: </span>
          <span className="font-semibold text-slate-900">{salesReturn.reason}</span>
        </div>
        {salesReturn.note && (
          <div>
            <span className="text-slate-500">- Ghi chú: </span>
            <span className="text-slate-800">{salesReturn.note}</span>
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
              <th className="border border-slate-300 py-2 px-2 text-right">SL trả</th>
              <th className="border border-slate-300 py-2 px-2 text-right">SL quy đổi (ĐV gốc)</th>
              <th className="border border-slate-300 py-2 px-2 text-right">Tiền hoàn</th>
              {showCost && (
                <th className="border border-slate-300 py-2 px-2 text-right">Giá vốn gốc</th>
              )}
            </tr>
          </thead>
          <tbody>
            {salesReturn.items.map((item, idx) => (
              <tr key={item.id}>
                <td className="border border-slate-300 py-1.5 px-2 text-center">{idx + 1}</td>
                <td className="border border-slate-300 py-1.5 px-2 font-mono font-semibold">
                  {item.skuCode}
                </td>
                <td className="border border-slate-300 py-1.5 px-2 font-medium text-slate-900">
                  {item.skuName}
                </td>
                <td className="border border-slate-300 py-1.5 px-2 text-center">
                  {item.unitName || item.baseUnitName || '—'}
                </td>
                <td className="border border-slate-300 py-1.5 px-2 text-right font-semibold">
                  {formatPrintNumber(item.quantity)}
                </td>
                <td className="border border-slate-300 py-1.5 px-2 text-right">
                  {formatPrintNumber(item.baseQuantity)}
                  {item.baseUnitName ? ` ${item.baseUnitName}` : ''}
                </td>
                <td className="border border-slate-300 py-1.5 px-2 text-right font-mono">
                  {formatPrintCurrency(item.refundAmount)}
                </td>
                {showCost && (
                  <td className="border border-slate-300 py-1.5 px-2 text-right font-mono">
                    {formatPrintCurrency(item.originalUnitCost)}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="bg-slate-50 font-bold text-slate-900">
              <td colSpan={4} className="border border-slate-300 py-2 px-2 text-right uppercase">
                Cộng hàng trả lại:
              </td>
              <td className="border border-slate-300 py-2 px-2 text-right">
                {formatPrintNumber(totalQty)}
              </td>
              <td className="border border-slate-300 py-2 px-2 text-right">
                {formatPrintNumber(totalBaseQty)}
              </td>
              <td className="border border-slate-300 py-2 px-2 text-right font-mono">
                {formatPrintCurrency(salesReturn.totalRefundAmount)}
              </td>
              {showCost && <td className="border border-slate-300 py-2 px-2 text-right">—</td>}
            </tr>
          </tfoot>
        </table>
      </div>

      <div className="pt-6 grid grid-cols-3 gap-6 text-center text-xs text-slate-700">
        <div>
          <div className="font-bold text-slate-900">Người lập phiếu</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">{salesReturn.createdByName || ''}</div>
        </div>
        <div>
          <div className="font-bold text-slate-900">Thủ kho nhận</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">{salesReturn.confirmedByName || ''}</div>
        </div>
        <div>
          <div className="font-bold text-slate-900">Khách hàng</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">{salesReturn.customerName || ''}</div>
        </div>
      </div>

      <PrintDocumentFooter printedByName={printedByName} />
    </div>
  );
};
