import React from 'react';
import type { GoodsIssue } from '../../types/goodsIssues';
import { PrintDocumentFooter } from './PrintDocumentFooter';
import { formatPrintCurrency, formatPrintDateTimeVn, formatPrintNumber } from './printFormatters';

interface GoodsIssuePrintPaperProps {
  issue: GoodsIssue;
  tenantName?: string;
  printedByName?: string | null;
  pageBreakAfter?: boolean;
}

export const GoodsIssuePrintPaper: React.FC<GoodsIssuePrintPaperProps> = ({
  issue,
  tenantName,
  printedByName,
  pageBreakAfter = false,
}) => {
  const showCost =
    issue.totalCost !== null ||
    issue.items.some((i) => i.unitCost !== null && i.unitCost !== undefined);

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
            Chi nhánh: <span className="font-semibold text-slate-800">{issue.branchName}</span>
          </div>
          <div className="text-xs text-slate-600">
            Kho xuất: <span className="font-semibold text-slate-800">{issue.warehouseName}</span>
          </div>
        </div>
        <div className="text-right text-xs">
          <div className="font-bold text-slate-900">Mẫu số: 02-VT</div>
          <div className="text-[11px] text-slate-500">(Ban hành theo Thông tư số 200/2014/TT-BTC</div>
          <div className="text-[11px] text-slate-500">ngày 22/12/2014 của Bộ Tài chính)</div>
        </div>
      </div>

      <div className="text-center py-4">
        <h2 className="text-xl font-black uppercase tracking-wide text-slate-900 m-0">
          PHIẾU XUẤT KHO
        </h2>
        <div className="text-xs text-slate-600 mt-1">
          Ngày xuất: <span className="font-semibold">{formatPrintDateTimeVn(issue.issuedAt)}</span>
        </div>
        <div className="text-xs text-slate-700 mt-0.5">
          Số phiếu: <span className="font-mono font-bold text-slate-900">{issue.number}</span>
        </div>
      </div>

      <div className="space-y-1.5 text-xs pb-4 border-b border-dashed border-slate-300">
        <div>
          <span className="text-slate-500">- Họ và tên người nhận hàng: </span>
          <span className="font-semibold text-slate-900">{issue.customerName || 'Khách lẻ'}</span>
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

      <div className="py-4">
        <table className="w-full text-xs border-collapse border border-slate-300">
          <thead>
            <tr className="bg-slate-100 text-slate-700 font-bold">
              <th className="border border-slate-300 py-2 px-2 text-center w-10">STT</th>
              <th className="border border-slate-300 py-2 px-2 text-left">Mã SKU</th>
              <th className="border border-slate-300 py-2 px-2 text-left">
                Tên, nhãn hiệu, quy cách vật tư, hàng hóa
              </th>
              <th className="border border-slate-300 py-2 px-2 text-center">ĐVT</th>
              <th className="border border-slate-300 py-2 px-2 text-right">SL xuất</th>
              <th className="border border-slate-300 py-2 px-2 text-right">SL quy đổi (ĐV gốc)</th>
              {showCost && (
                <>
                  <th className="border border-slate-300 py-2 px-2 text-right">Đơn giá vốn</th>
                  <th className="border border-slate-300 py-2 px-2 text-right">Thành tiền</th>
                </>
              )}
            </tr>
          </thead>
          <tbody>
            {issue.items.map((item, idx) => (
              <tr key={item.id}>
                <td className="border border-slate-300 py-1.5 px-2 text-center">{idx + 1}</td>
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
                  {formatPrintNumber(item.quantity)}
                </td>
                <td className="border border-slate-300 py-1.5 px-2 text-right">
                  {formatPrintNumber(item.baseQuantity)}
                  {item.baseUnitName ? ` ${item.baseUnitName}` : ''}
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
              <td colSpan={4} className="border border-slate-300 py-2 px-2 text-right uppercase">
                Cộng phiếu xuất:
              </td>
              <td className="border border-slate-300 py-2 px-2 text-right">
                {formatPrintNumber(issue.totalQuantity)}
              </td>
              <td className="border border-slate-300 py-2 px-2 text-right">
                {formatPrintNumber(issue.totalBaseQuantity)}
              </td>
              {showCost && (
                <>
                  <td className="border border-slate-300 py-2 px-2 text-right">—</td>
                  <td className="border border-slate-300 py-2 px-2 text-right font-mono">
                    {formatPrintCurrency(issue.totalCost)}
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
          <div className="mt-14 font-semibold text-slate-800">{issue.issuedByName || ''}</div>
        </div>
        <div>
          <div className="font-bold text-slate-900">Thủ kho</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">&nbsp;</div>
        </div>
        <div>
          <div className="font-bold text-slate-900">Người nhận hàng</div>
          <div className="text-[11px] text-slate-500 mt-0.5">(Ký, họ tên)</div>
          <div className="mt-14 font-semibold text-slate-800">{issue.customerName || ''}</div>
        </div>
      </div>

      <PrintDocumentFooter printedByName={printedByName} />
    </div>
  );
};
