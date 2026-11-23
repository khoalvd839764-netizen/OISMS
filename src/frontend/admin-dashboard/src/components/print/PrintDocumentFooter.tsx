import React from 'react';
import { formatPrintDateTimeVn } from './printFormatters';

interface PrintDocumentFooterProps {
  printedByName?: string | null;
  printedAt?: Date;
}

/**
 * Chân trang chuẩn cho mọi mẫu in Upgrade 14 (Q10):
 * Không ghi log in, không đánh dấu "bản in lại"; chỉ hiển thị Ngày in (UTC+07:00) và tên người in.
 */
export const PrintDocumentFooter: React.FC<PrintDocumentFooterProps> = ({
  printedByName,
  printedAt,
}) => {
  const displayTime = formatPrintDateTimeVn(printedAt ?? new Date());
  return (
    <div className="mt-6 pt-3 border-t border-dashed border-slate-300 flex flex-wrap justify-between items-center text-[11px] text-slate-600">
      <div>
        Ngày in: <span className="font-semibold text-slate-800">{displayTime} (UTC+07:00)</span>
      </div>
      <div>
        Người in: <span className="font-semibold text-slate-800">{printedByName || 'Người dùng hệ thống'}</span>
      </div>
    </div>
  );
};
