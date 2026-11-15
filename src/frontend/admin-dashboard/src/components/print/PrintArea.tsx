import React from 'react';
import { createPortal } from 'react-dom';

/**
 * Cô lập vùng in (CS-06): hiển thị nội dung trong modal để xem trước, đồng thời đưa một bản sao
 * ra thẳng <body>. Khi in, CSS ẩn #root và chỉ in bản sao này, nên trang nền, drawer và
 * lớp phủ modal không lọt vào bản in.
 */
export const PrintArea: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <>
    {children}
    {createPortal(<div className="print-portal print-container">{children}</div>, document.body)}
  </>
);
