import React from 'react';
import type { StockTransfer } from '../../types/stockTransfer';
import { PrintDocumentFooter } from './PrintDocumentFooter';
import { formatPrintCurrency, formatPrintDateTimeVn, formatPrintNumber } from './printFormatters';

interface StockTransferInPrintPaperProps {
  transfer: StockTransfer;
  tenantName?: string;
  printedByName?: string | null;
  pageBreakAfter?: boolean;
}

export const StockTransferInPrintPaper: React.FC<StockTransferInPrintPaperProps> = ({
  transfer,
  tenantName,
  printedByName,
  pageBreakAfter = false,
}) => {
  const showCost = transfer.items.some((i) => i.unitCost !== null && i.unitCost !== undefined);
  const totalQty = transfer.items.reduce((s, i) => s + (i.quantity || 0), 0);
  const totalCost = showCost
    ? transfer.items.reduce((s, i) => s + (i.quantity || 0) * (i.unitCost || 0), 0)
    : null;

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
            Chi nhánh nhận:{' '}
            <span className="font-semibold text-slate-800">{transfer.toBranchName || '—'}</span>
          </div>
          <div className="text-xs text-slate-600">
            Kho nhận:{' '}
            <span className="font-semibold text-slate-800">{transfer.toWarehouseName || '—'}</span>
          </div>
        </div>
        <div className="text-right text-xs">
          <div className="font-bold text-slate-900">Phiếu nhập chuyển kho</div>
          <div className="text-[11px] text-slate-500">Xác nhận nhập kho đích</div>
        </div>
      </div>

      <div className="text-center py-4">
        <h2 className="text-xl font-black uppercase tracking-wide text-slate-900 m-0">
          PHIẾU NHẬP KHO (NHẬN CHUYỂN KHO)
        </h2>
        <div className="text-xs text-slate-600 mt-1">
          Ngày nhận:{' '}
          <span className="font-semibold">
            {formatPrintDateTimeVn(transfer.receivedAt || transfer.createdAt)}
          </span>
        </div>
        <div className="text-xs text-slate-700 mt-0.5">
          Số phiếu:{' '}
          <span className="font-mono font-bold text-slate-900">
            {transfer.number || transfer.transferNumber || transfer.id}
          </span>
          <span className="ml-2 text-slate-500">({transfer.status})</span>
        </div>
      </div>

      <div className="space-y-1.5 text-xs pb-4 border-b border-dashed border-slate-300">
        <div className="grid grid-cols-2 gap-2">
          <div>
            <span className="text-slate-500">- Nhập từ kho nguồn: </span>
            <span className="font-bold text-slate-900">
              {transfer.fromBranchName} / {transfer.fromWarehouseName}
            </span>
          </div>
          <div>
            <span className="text-slate-500">- Nhập vào kho đích: </span>
            <span className="font-bold text-slate-900">
              {transfer.toBranchName} / {transfer.toWarehouseName}
            </span>
          </div>
        </div>
        {transfer.note && (
          <div>
            <span className="text-slate-500">- Ghi chú: </span>
            <span className="text-slate-800">{transfer.note}</span>
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
              <th className="border border-slate-300 py-2 px-2 text-center">ĐVT gốc</th>
              <th className="border border-slate-300 py-2 px-2 text-right">SL thực nhận</th>
              {showCost && (
                <>
                  <th className="border border-slate-300 py-2 px-2 text-right">Đơn giá vốn</th>
                  <th className="border border-slate-300 py-2 px-2 text-right">Thành tiền</th>
                </>
              )}
            </tr>
          </thead>
          <tbody>
            {transfer.items.map((item, idx) => (
              <tr key={item.id || item.skuId}>
                <td className="border border-slate-300 py-1.5 px-2 text-center">{idx + 1}</td>
                <td className="border border-slate-300 py-1.5 px-2 font-mono font-semibold">
                  {item.skuCode}
                </td>
                <td className="border border-slate-300 py-1.5 px-2 font-medium text-slate-900">
                  {item.productName || item.skuName || '—'}
                </td>
                <td className="border border-slate-300 py-1.5 px-2 text-center">
                  {item.baseUnitName || '—'}
                </td>
                <td className="border border-slate-300 py-1.5 px-2 text-right font-semibold">
                  {formatPrintNumber(item.quantity)}
                </td>
                {showCost && (
                  <>
                    <td className="border border-slate-300 py-1.5 px-2 text-right font-mono">
                      {formatPrintCurrency(item.unitCost)}
                    </td>
                    <td className="border border-slate-300 py-1.5 px-2 text-right font-mono font-semibold">
                      {item.unitCost != null ? formatPrintCurrency(item.quantity * item.unitCost) : '—'}
                    </td>
                  </>
                )}
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="bg-slate-50 font-bold text-slate-900">
              <td colSpan={4} className="border border-slate-300 py-2 px-2 text-right uppercase">
                Cộng nhập chuyển kho:
              </td>
              <td className="border border-slate-300 py-2 px-2 text-right">
                {formatPrintNumber(totalQty)}
              </td>
              {showCost && (
                <>
                  <td className="border border-slate-300 py-2 px-2 text-right">—</td>
                  <td className="border border-slate-300 py-2 px-2 text-right font-mono">
                    {formatPrintCurrency(totalCost)}
                  </td>
                </>
              )}
            </tr>
          </tfoot>
        </table>
      </div>

      <div className="pt-6 grid grid-cols-3 gap-6 text-center text-xs text-slate-700">
        <div>
          <div className="font-bold text-slate-900">Người lập phiếu</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">{transfer.createdByName || ''}</div>
        </div>
        <div>
          <div className="font-bold text-slate-900">Người giao (Kho xuất)</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">{transfer.dispatchedByName || ''}</div>
        </div>
        <div>
          <div className="font-bold text-slate-900">Người nhận hàng</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">{transfer.receivedByName || ''}</div>
        </div>
      </div>

      <PrintDocumentFooter printedByName={printedByName} />
    </div>
  );
};
