import React from 'react';
import type { GoodsReceiptResponse } from '../../types/goodsReceipt';
import { PrintDocumentFooter } from './PrintDocumentFooter';
import { formatPrintCurrency, formatPrintDateTimeVn, formatPrintNumber } from './printFormatters';

interface GoodsReceiptPrintPaperProps {
  receipt: GoodsReceiptResponse;
  tenantName?: string;
  printedByName?: string | null;
  onOpenPurchaseOrder?: (purchaseOrderId: string) => void;
  pageBreakAfter?: boolean;
}

export const GoodsReceiptPrintPaper: React.FC<GoodsReceiptPrintPaperProps> = ({
  receipt,
  tenantName,
  printedByName,
  onOpenPurchaseOrder,
  pageBreakAfter = false,
}) => {
  // Chỉ hiện cột giá khi API trả về giá trị (Owner); Staff nhận null sẽ ẩn hoàn toàn (AC-04 / Q7)
  const showCost =
    receipt.totalAmount !== null ||
    receipt.items.some((i) => i.unitPrice !== null && i.unitPrice !== undefined);

  const totalQuantity = receipt.items.reduce((s, i) => s + (i.quantity || 0), 0);
  const totalBaseQuantity = receipt.items.reduce((s, i) => s + (i.baseQuantity || 0), 0);

  return (
    <div
      className={`doc-print-paper bg-white border border-slate-300 text-slate-900 font-sans shadow-sm p-8 text-sm ${
        pageBreakAfter ? 'doc-print-page-break' : ''
      }`}
    >
      {/* Header 01-VT */}
      <div className="flex justify-between items-start pb-4 border-b border-slate-300">
        <div>
          <div className="font-extrabold uppercase text-base text-slate-900">
            {tenantName || 'CỬA HÀNG OISMS'}
          </div>
          <div className="text-xs text-slate-600 mt-0.5">
            Chi nhánh: <span className="font-semibold text-slate-800">{receipt.branchName}</span>
          </div>
          <div className="text-xs text-slate-600">
            Kho nhập: <span className="font-semibold text-slate-800">{receipt.warehouseName}</span>
          </div>
        </div>
        <div className="text-right text-xs">
          <div className="font-bold text-slate-900">Mẫu số: 01-VT</div>
          <div className="text-[11px] text-slate-500">(Ban hành theo Thông tư số 200/2014/TT-BTC</div>
          <div className="text-[11px] text-slate-500">ngày 22/12/2014 của Bộ Tài chính)</div>
        </div>
      </div>

      {/* Title */}
      <div className="text-center py-4">
        <h2 className="text-xl font-black uppercase tracking-wide text-slate-900 m-0">
          PHIẾU NHẬP KHO
        </h2>
        <div className="text-xs text-slate-600 mt-1">
          Ngày chứng từ:{' '}
          <span className="font-semibold">
            {formatPrintDateTimeVn(receipt.confirmedAt || receipt.createdAt)}
          </span>
        </div>
        <div className="text-xs text-slate-700 mt-0.5">
          Số phiếu: <span className="font-mono font-bold text-slate-900">{receipt.number}</span>
          <span className="ml-2 text-slate-500">({receipt.status})</span>
        </div>
      </div>

      {/* Metadata */}
      <div className="space-y-1.5 text-xs pb-4 border-b border-dashed border-slate-300">
        <div>
          <span className="text-slate-500">- Nhà cung cấp: </span>
          <span className="font-semibold text-slate-900">{receipt.supplierName || '—'}</span>
        </div>
        <div className="grid grid-cols-2 gap-2">
          <div>
            <span className="text-slate-500">- Nhập tại kho: </span>
            <span className="font-bold text-slate-900">{receipt.warehouseName}</span>
          </div>
          <div>
            <span className="text-slate-500">- Đơn mua hàng liên quan: </span>
            {receipt.purchaseOrderNumber ? (
              receipt.purchaseOrderId && onOpenPurchaseOrder ? (
                <button
                  type="button"
                  onClick={() => onOpenPurchaseOrder(receipt.purchaseOrderId!)}
                  className="font-mono font-semibold text-indigo-600 hover:underline no-print-link"
                >
                  {receipt.purchaseOrderNumber}
                </button>
              ) : (
                <span className="font-mono font-semibold text-slate-900">
                  {receipt.purchaseOrderNumber}
                </span>
              )
            ) : (
              <span className="text-slate-500">—</span>
            )}
          </div>
        </div>
        {receipt.note && (
          <div>
            <span className="text-slate-500">- Ghi chú: </span>
            <span className="text-slate-800">{receipt.note}</span>
          </div>
        )}
      </div>

      {/* Items Table */}
      <div className="py-4">
        <table className="w-full text-xs border-collapse border border-slate-300">
          <thead>
            <tr className="bg-slate-100 text-slate-700 font-bold">
              <th className="border border-slate-300 py-2 px-2 text-center w-10">STT</th>
              <th className="border border-slate-300 py-2 px-2 text-left">Mã SKU</th>
              <th className="border border-slate-300 py-2 px-2 text-left">Tên hàng hóa</th>
              <th className="border border-slate-300 py-2 px-2 text-center">ĐVT</th>
              <th className="border border-slate-300 py-2 px-2 text-right">SL nhập</th>
              <th className="border border-slate-300 py-2 px-2 text-right">SL quy đổi (ĐV gốc)</th>
              {showCost && (
                <>
                  <th className="border border-slate-300 py-2 px-2 text-right">Đơn giá nhập</th>
                  <th className="border border-slate-300 py-2 px-2 text-right">Thành tiền</th>
                </>
              )}
            </tr>
          </thead>
          <tbody>
            {receipt.items.map((item, idx) => (
              <tr key={item.id}>
                <td className="border border-slate-300 py-1.5 px-2 text-center">{idx + 1}</td>
                <td className="border border-slate-300 py-1.5 px-2 font-mono font-semibold">
                  {item.skuCode}
                </td>
                <td className="border border-slate-300 py-1.5 px-2 font-medium text-slate-900">
                  {item.productName}
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
                {showCost && (
                  <>
                    <td className="border border-slate-300 py-1.5 px-2 text-right font-mono">
                      {formatPrintCurrency(item.unitPrice)}
                    </td>
                    <td className="border border-slate-300 py-1.5 px-2 text-right font-mono font-semibold">
                      {formatPrintCurrency(item.lineTotal)}
                    </td>
                  </>
                )}
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="bg-slate-50 font-bold text-slate-900">
              <td colSpan={4} className="border border-slate-300 py-2 px-2 text-right uppercase">
                Cộng phiếu nhập:
              </td>
              <td className="border border-slate-300 py-2 px-2 text-right">
                {formatPrintNumber(totalQuantity)}
              </td>
              <td className="border border-slate-300 py-2 px-2 text-right">
                {formatPrintNumber(totalBaseQuantity)}
              </td>
              {showCost && (
                <>
                  <td className="border border-slate-300 py-2 px-2 text-right">—</td>
                  <td className="border border-slate-300 py-2 px-2 text-right font-mono">
                    {formatPrintCurrency(receipt.totalAmount)}
                  </td>
                </>
              )}
            </tr>
          </tfoot>
        </table>
      </div>

      {/* Signatures */}
      <div className="pt-6 grid grid-cols-3 gap-6 text-center text-xs text-slate-700">
        <div>
          <div className="font-bold text-slate-900">Người lập phiếu</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">{receipt.createdByName || ''}</div>
        </div>
        <div>
          <div className="font-bold text-slate-900">Thủ kho</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">&nbsp;</div>
        </div>
        <div>
          <div className="font-bold text-slate-900">Người duyệt nhập kho</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">{receipt.confirmedByName || ''}</div>
        </div>
      </div>

      <PrintDocumentFooter printedByName={printedByName} />
    </div>
  );
};
