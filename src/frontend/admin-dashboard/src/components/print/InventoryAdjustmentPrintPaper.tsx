import React from 'react';
import type { InventoryAdjustment } from '../../types/inventoryAdjustment';
import { PrintDocumentFooter } from './PrintDocumentFooter';
import { formatPrintCurrency, formatPrintDateTimeVn, formatPrintNumber } from './printFormatters';

interface InventoryAdjustmentPrintPaperProps {
  adjustment: InventoryAdjustment;
  tenantName?: string;
  printedByName?: string | null;
  pageBreakAfter?: boolean;
}

export const InventoryAdjustmentPrintPaper: React.FC<InventoryAdjustmentPrintPaperProps> = ({
  adjustment,
  tenantName,
  printedByName,
  pageBreakAfter = false,
}) => {
  const showCost =
    adjustment.totalCost !== null ||
    adjustment.items.some((i) => i.unitCost !== null && i.unitCost !== undefined);

  const title =
    adjustment.direction === 'OUT'
      ? 'PHIẾU XUẤT ĐIỀU CHỈNH KHO'
      : adjustment.direction === 'IN'
        ? 'PHIẾU NHẬP ĐIỀU CHỈNH KHO'
        : 'PHIẾU ĐIỀU CHỈNH CÂN BẰNG KHO';

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
            <span className="font-semibold text-slate-800">{adjustment.branchName}</span>
          </div>
          <div className="text-xs text-slate-600">
            Kho:{' '}
            <span className="font-semibold text-slate-800">{adjustment.warehouseName}</span>
          </div>
        </div>
        <div className="text-right text-xs">
          <div className="font-bold text-slate-900">Phiếu điều chỉnh ({adjustment.direction})</div>
          <div className="text-[11px] text-slate-500">Cân bằng sai lệch tồn kho</div>
        </div>
      </div>

      <div className="text-center py-4">
        <h2 className="text-xl font-black uppercase tracking-wide text-slate-900 m-0">{title}</h2>
        <div className="text-xs text-slate-600 mt-1">
          Ngày chứng từ:{' '}
          <span className="font-semibold">
            {formatPrintDateTimeVn(adjustment.confirmedAt || adjustment.createdAt)}
          </span>
        </div>
        <div className="text-xs text-slate-700 mt-0.5">
          Số phiếu: <span className="font-mono font-bold text-slate-900">{adjustment.number}</span>
          <span className="ml-2 text-slate-500">({adjustment.status})</span>
        </div>
      </div>

      <div className="space-y-1.5 text-xs pb-4 border-b border-dashed border-slate-300">
        <div className="grid grid-cols-2 gap-2">
          <div>
            <span className="text-slate-500">- Chiều điều chỉnh: </span>
            <span className="font-bold text-slate-900">{adjustment.direction}</span>
          </div>
          <div>
            <span className="text-slate-500">- Phiếu kiểm kê nguồn: </span>
            <span className="font-mono font-semibold text-slate-900">
              {adjustment.stockCountNumber || '—'}
            </span>
          </div>
        </div>
        <div>
          <span className="text-slate-500">- Lý do điều chỉnh: </span>
          <span className="font-semibold text-slate-900">{adjustment.reason}</span>
        </div>
      </div>

      <div className="py-4">
        <table className="w-full text-xs border-collapse border border-slate-300">
          <thead>
            <tr className="bg-slate-100 text-slate-700 font-bold">
              <th className="border border-slate-300 py-2 px-2 text-center w-10">STT</th>
              <th className="border border-slate-300 py-2 px-2 text-left">Mã SKU</th>
              <th className="border border-slate-300 py-2 px-2 text-left">Tên hàng hóa</th>
              <th className="border border-slate-300 py-2 px-2 text-center">ĐVT gốc</th>
              <th className="border border-slate-300 py-2 px-2 text-center">Chiều</th>
              <th className="border border-slate-300 py-2 px-2 text-right">SL điều chỉnh</th>
              {showCost && (
                <>
                  <th className="border border-slate-300 py-2 px-2 text-right">Đơn giá vốn</th>
                  <th className="border border-slate-300 py-2 px-2 text-right">Thành tiền</th>
                </>
              )}
            </tr>
          </thead>
          <tbody>
            {adjustment.items.map((item, idx) => (
              <tr key={item.id}>
                <td className="border border-slate-300 py-1.5 px-2 text-center">{idx + 1}</td>
                <td className="border border-slate-300 py-1.5 px-2 font-mono font-semibold">
                  {item.skuCode}
                </td>
                <td className="border border-slate-300 py-1.5 px-2 font-medium text-slate-900">
                  {item.skuName}
                </td>
                <td className="border border-slate-300 py-1.5 px-2 text-center">
                  {item.baseUnitName || '—'}
                </td>
                <td className="border border-slate-300 py-1.5 px-2 text-center font-semibold">
                  {item.direction}
                </td>
                <td
                  className={`border border-slate-300 py-1.5 px-2 text-right font-bold ${
                    item.quantityDelta >= 0 ? 'text-emerald-700' : 'text-rose-700'
                  }`}
                >
                  {item.quantityDelta > 0 ? '+' : ''}
                  {formatPrintNumber(item.quantityDelta)}
                </td>
                {showCost && (
                  <>
                    <td className="border border-slate-300 py-1.5 px-2 text-right font-mono">
                      {formatPrintCurrency(item.unitCost)}
                    </td>
                    <td className="border border-slate-300 py-1.5 px-2 text-right font-mono font-semibold">
                      {formatPrintCurrency(item.totalCost)}
                    </td>
                  </>
                )}
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="bg-slate-50 font-bold text-slate-900">
              <td colSpan={5} className="border border-slate-300 py-2 px-2 text-right uppercase">
                Tổng điều chỉnh:
              </td>
              <td className="border border-slate-300 py-2 px-2 text-right">
                {adjustment.totalQuantityDelta > 0 ? '+' : ''}
                {formatPrintNumber(adjustment.totalQuantityDelta)}
              </td>
              {showCost && (
                <>
                  <td className="border border-slate-300 py-2 px-2 text-right">—</td>
                  <td className="border border-slate-300 py-2 px-2 text-right font-mono">
                    {formatPrintCurrency(adjustment.totalCost)}
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
          <div className="mt-14 font-semibold text-slate-800">{adjustment.createdByName || ''}</div>
        </div>
        <div>
          <div className="font-bold text-slate-900">Thủ kho</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">&nbsp;</div>
        </div>
        <div>
          <div className="font-bold text-slate-900">Người duyệt</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">
            {adjustment.confirmedByName || ''}
          </div>
        </div>
      </div>

      <PrintDocumentFooter printedByName={printedByName} />
    </div>
  );
};
