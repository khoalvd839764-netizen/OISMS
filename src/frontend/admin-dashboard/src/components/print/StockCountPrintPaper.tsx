import React from 'react';
import type { StockCount } from '../../types/stockCount';
import { PrintDocumentFooter } from './PrintDocumentFooter';
import { formatPrintCurrency, formatPrintDateTimeVn, formatPrintNumber } from './printFormatters';

interface StockCountPrintPaperProps {
  stockCount: StockCount;
  highlightSkuId?: string | null;
  tenantName?: string;
  printedByName?: string | null;
  pageBreakAfter?: boolean;
}

export const StockCountPrintPaper: React.FC<StockCountPrintPaperProps> = ({
  stockCount,
  highlightSkuId,
  tenantName,
  printedByName,
  pageBreakAfter = false,
}) => {
  const showCost = stockCount.items.some(
    (i) => i.adjustmentUnitCost !== null && i.adjustmentUnitCost !== undefined
  );

  const totalExpected = stockCount.items.reduce((s, i) => s + (i.expectedQuantity || 0), 0);
  const totalCounted = stockCount.items.reduce((s, i) => s + (i.countedQuantity ?? 0), 0);

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
            <span className="font-semibold text-slate-800">{stockCount.branchName || '—'}</span>
          </div>
          <div className="text-xs text-slate-600">
            Kho kiểm kê:{' '}
            <span className="font-semibold text-slate-800">{stockCount.warehouseName || '—'}</span>
          </div>
        </div>
        <div className="text-right text-xs">
          <div className="font-bold text-slate-900">Mẫu số: 05-VT</div>
          <div className="text-[11px] text-slate-500">(Ban hành theo Thông tư số 200/2014/TT-BTC</div>
          <div className="text-[11px] text-slate-500">ngày 22/12/2014 của Bộ Tài chính)</div>
        </div>
      </div>

      <div className="text-center py-4">
        <h2 className="text-xl font-black uppercase tracking-wide text-slate-900 m-0">
          BIÊN BẢN KIỂM KÊ VẬT TƯ, HÀNG HÓA
        </h2>
        <div className="text-xs text-slate-600 mt-1">
          Ngày lập:{' '}
          <span className="font-semibold">
            {formatPrintDateTimeVn(stockCount.confirmedAt || stockCount.createdAt)}
          </span>
        </div>
        <div className="text-xs text-slate-700 mt-0.5">
          Số phiếu: <span className="font-mono font-bold text-slate-900">{stockCount.number}</span>
          <span className="ml-2 text-slate-500">({stockCount.status})</span>
        </div>
      </div>

      <div className="space-y-1.5 text-xs pb-4 border-b border-dashed border-slate-300">
        <div className="grid grid-cols-2 gap-2">
          <div>
            <span className="text-slate-500">- Loại kiểm kê: </span>
            <span className="font-semibold text-slate-900">
              {stockCount.countType === 'Scheduled' ? 'Định kỳ theo lịch' : 'Đột xuất'}
            </span>
          </div>
          <div>
            <span className="text-slate-500">- Phiếu điều chỉnh phát sinh: </span>
            <span className="font-mono font-semibold text-slate-900">
              {stockCount.generatedAdjustmentNumber || '—'}
            </span>
          </div>
        </div>
        {(stockCount.adHocReason || stockCount.note) && (
          <div>
            <span className="text-slate-500">- Lý do / Ghi chú: </span>
            <span className="text-slate-800">{stockCount.adHocReason || stockCount.note}</span>
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
              <th className="border border-slate-300 py-2 px-2 text-right">Tồn sổ</th>
              <th className="border border-slate-300 py-2 px-2 text-right">Tồn thực tế</th>
              <th className="border border-slate-300 py-2 px-2 text-right">Chênh lệch</th>
              {showCost && (
                <th className="border border-slate-300 py-2 px-2 text-right">Đơn giá ĐC</th>
              )}
            </tr>
          </thead>
          <tbody>
            {stockCount.items.map((item, idx) => {
              const isHighlighted = Boolean(highlightSkuId && item.skuId === highlightSkuId);
              const diff =
                item.differenceQuantity !== null && item.differenceQuantity !== undefined
                  ? item.differenceQuantity
                  : item.countedQuantity !== null && item.countedQuantity !== undefined
                    ? item.countedQuantity - item.expectedQuantity
                    : null;

              return (
                <tr
                  key={item.id}
                  className={isHighlighted ? 'bg-amber-50 ring-1 ring-inset ring-amber-400' : ''}
                >
                  <td className="border border-slate-300 py-1.5 px-2 text-center">{idx + 1}</td>
                  <td className="border border-slate-300 py-1.5 px-2 font-mono font-semibold">
                    {item.skuCode}
                    {isHighlighted && (
                      <span className="ml-1.5 inline-block px-1.5 py-0.5 text-[10px] font-sans font-bold bg-amber-200 text-amber-900 rounded">
                        Đang xem
                      </span>
                    )}
                  </td>
                  <td className="border border-slate-300 py-1.5 px-2 font-medium text-slate-900">
                    {item.productName}
                  </td>
                  <td className="border border-slate-300 py-1.5 px-2 text-center">
                    {item.baseUnitName || '—'}
                  </td>
                  <td className="border border-slate-300 py-1.5 px-2 text-right">
                    {formatPrintNumber(item.expectedQuantity)}
                  </td>
                  <td className="border border-slate-300 py-1.5 px-2 text-right font-semibold">
                    {formatPrintNumber(item.countedQuantity)}
                  </td>
                  <td
                    className={`border border-slate-300 py-1.5 px-2 text-right font-bold ${
                      diff === null
                        ? 'text-slate-400'
                        : diff > 0
                          ? 'text-emerald-700'
                          : diff < 0
                            ? 'text-rose-700'
                            : 'text-slate-700'
                    }`}
                  >
                    {diff === null ? '—' : `${diff > 0 ? '+' : ''}${formatPrintNumber(diff)}`}
                  </td>
                  {showCost && (
                    <td className="border border-slate-300 py-1.5 px-2 text-right font-mono">
                      {formatPrintCurrency(item.adjustmentUnitCost)}
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
          <tfoot>
            <tr className="bg-slate-50 font-bold text-slate-900">
              <td colSpan={4} className="border border-slate-300 py-2 px-2 text-right uppercase">
                Tổng cộng:
              </td>
              <td className="border border-slate-300 py-2 px-2 text-right">
                {formatPrintNumber(totalExpected)}
              </td>
              <td className="border border-slate-300 py-2 px-2 text-right">
                {formatPrintNumber(totalCounted)}
              </td>
              <td className="border border-slate-300 py-2 px-2 text-right">
                {formatPrintNumber(totalCounted - totalExpected)}
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
          <div className="mt-14 font-semibold text-slate-800">{stockCount.createdByName || ''}</div>
        </div>
        <div>
          <div className="font-bold text-slate-900">Người kiểm đếm / Nộp</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">
            {stockCount.submittedByName || stockCount.items.find((i) => i.countedByName)?.countedByName || ''}
          </div>
        </div>
        <div>
          <div className="font-bold text-slate-900">Người duyệt kiểm kê</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">{stockCount.confirmedByName || ''}</div>
        </div>
      </div>

      <PrintDocumentFooter printedByName={printedByName} />
    </div>
  );
};
