import React, { useState } from 'react';
import { Printer, X, CheckCircle2 } from 'lucide-react';
import type { ReceiptData } from '../types/pos';

interface ReceiptPrintModalProps {
  receipt: ReceiptData | null;
  onClose: () => void;
  defaultPaperWidth?: '80mm' | '58mm';
}

export const ReceiptPrintModal: React.FC<ReceiptPrintModalProps> = ({ receipt, onClose, defaultPaperWidth }) => {
  if (!receipt) return null;

  // T05-AC-02: Hỗ trợ chọn khổ in 80mm (mặc định) và 58mm, lưu lựa chọn vào localStorage
  const [paperWidth, setPaperWidth] = useState<'80mm' | '58mm'>(() => {
    if (defaultPaperWidth === '58mm' || defaultPaperWidth === '80mm') {
      return defaultPaperWidth;
    }
    if (receipt.paperWidth === '58mm' || receipt.paperWidth === '80mm') {
      return receipt.paperWidth;
    }
    try {
      const saved = localStorage.getItem('pos_receipt_paper_width');
      if (saved === '58mm' || saved === '80mm') return saved;
    } catch {
      // Ignore localStorage read issues
    }
    return '80mm';
  });

  const handleSetPaperWidth = (width: '80mm' | '58mm') => {
    setPaperWidth(width);
    try {
      localStorage.setItem('pos_receipt_paper_width', width);
    } catch {
      // Ignore localStorage write issues
    }
  };

  const handlePrint = () => {
    window.print();
  };

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
  };

  const formatPaymentMethod = (method: string) => {
    const m = (method || '').toUpperCase();
    if (m === 'CASH' || m === 'TIỀN MẶT') return 'Tiền mặt';
    if (m === 'BANK_TRANSFER_QR' || m === 'VIETQR' || m === 'CHUYỂN KHOẢN QR' || m === 'TRANSFER') return 'Chuyển khoản QR';
    if (m === 'CARD' || m === 'THẺ') return 'Thẻ ngân hàng';
    return method || 'Tiền mặt';
  };

  const is58 = paperWidth === '58mm';
  const isCash = (receipt.paymentMethod || '').toUpperCase() === 'CASH' || (receipt.paymentMethod || '').toUpperCase() === 'TIỀN MẶT';

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-fade-in no-print-bg">
      {/* Dynamic @page print style injection matching chosen paper width (B01-F01 & B01-F03) */}
      <style>
        {`@media print {
          @page {
            size: ${paperWidth} 200mm;
            margin: 0;
          }
          html, body {
            width: ${paperWidth} !important;
            margin: 0 !important;
            padding: 0 !important;
            background: #ffffff !important;
            color: #000000 !important;
          }
          .no-print-bg {
            display: block !important;
            position: static !important;
            background: transparent !important;
            padding: 0 !important;
            margin: 0 !important;
            border: none !important;
            box-shadow: none !important;
            width: 100% !important;
            max-height: none !important;
            overflow: visible !important;
          }
          .no-print-border {
            display: block !important;
            border: none !important;
            box-shadow: none !important;
            background: transparent !important;
            max-width: none !important;
            width: 100% !important;
            max-height: none !important;
            overflow: visible !important;
            margin: 0 !important;
            padding: 0 !important;
            border-radius: 0 !important;
          }
          .print-container {
            position: static !important;
            width: ${paperWidth} !important;
            max-width: ${paperWidth} !important;
            padding: 0 !important;
            margin: 0 !important;
            display: block !important;
            overflow: visible !important;
          }
          .receipt-paper {
            width: ${paperWidth} !important;
            max-width: ${paperWidth} !important;
            margin: 0 auto !important;
            padding: ${is58 ? '1.5mm 2mm' : '2mm 3mm'} !important;
            font-size: ${is58 ? '9.5px' : '11px'} !important;
            line-height: ${is58 ? '1.25' : '1.35'} !important;
            display: block !important;
          }
        }`}
      </style>

      <div className="bg-white rounded-3xl shadow-2xl border border-slate-200 max-w-md w-full overflow-hidden flex flex-col max-h-[95vh] no-print-border">
        {/* Header Modal Bar (hidden on print) */}
        <div className={`px-5 py-3 text-white flex items-center justify-between no-print ${receipt.isDraft ? 'bg-amber-900' : 'bg-slate-900'}`}>
          <div className="flex items-center gap-2">
            <CheckCircle2 className={`w-5 h-5 ${receipt.isDraft ? 'text-amber-400' : 'text-emerald-400'}`} />
            <span className="font-bold text-sm">
              {receipt.isDraft ? 'Phiếu Tạm Tính (Chưa thanh toán)' : `Hóa Đơn Bán Lẻ Khổ ${paperWidth}`}
            </span>
          </div>

          {/* Paper Width Selector Switcher */}
          <div className="flex items-center gap-1 bg-slate-800/90 p-1 rounded-xl border border-slate-700">
            <button
              type="button"
              onClick={() => handleSetPaperWidth('80mm')}
              className={`px-2.5 py-1 text-xs rounded-lg font-semibold transition-all ${!is58 ? 'bg-indigo-600 text-white shadow-xs' : 'text-slate-400 hover:text-white'}`}
            >
              80mm
            </button>
            <button
              type="button"
              onClick={() => handleSetPaperWidth('58mm')}
              className={`px-2.5 py-1 text-xs rounded-lg font-semibold transition-all ${is58 ? 'bg-indigo-600 text-white shadow-xs' : 'text-slate-400 hover:text-white'}`}
            >
              58mm
            </button>
          </div>

          <button
            type="button"
            onClick={onClose}
            className="p-1 rounded-lg text-slate-300 hover:text-white hover:bg-slate-800 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Action Button Strip (hidden on print) */}
        <div className={`p-3 border-b flex items-center justify-between no-print ${receipt.isDraft ? 'bg-amber-50 border-amber-200' : 'bg-indigo-50 border-indigo-100'}`}>
          <span className={`text-xs font-semibold ${receipt.isDraft ? 'text-amber-900' : 'text-indigo-900'}`}>
            {receipt.isDraft ? 'Bản in tạm tính - Đơn hàng chưa ghi nhận thanh toán' : `Bản in hóa đơn chính thức (${paperWidth})`}
          </span>
          <button
            type="button"
            onClick={handlePrint}
            className={`inline-flex items-center gap-1.5 px-4 py-1.5 text-white rounded-xl font-bold text-xs shadow-md transition-all ${receipt.isDraft ? 'bg-amber-700 hover:bg-amber-800' : 'bg-indigo-600 hover:bg-indigo-700'}`}
          >
            <Printer className="w-4 h-4" />
            <span>In Phiếu (Ctrl + P)</span>
          </button>
        </div>

        {/* Printable Receipt Paper Container */}
        <div className="p-6 overflow-y-auto bg-slate-100/50 flex justify-center print-container">
          <div className={`receipt-paper ${is58 ? 'paper-58mm max-w-[240px] text-[10px]' : 'paper-80mm max-w-[320px] text-xs'} bg-white w-full p-4 border border-dashed border-slate-300 text-slate-900 font-sans shadow-sm relative transition-all`}>
            {/* Draft Watermark banner if draft */}
            {receipt.isDraft && (
              <div className="mb-2 p-1.5 text-center bg-amber-100 border border-amber-300 text-amber-900 font-black tracking-wider text-[11px] uppercase rounded">
                *** CHƯA THANH TOÁN ***
              </div>
            )}

            {/* Store Branding Header (T02-AC-05: Địa chỉ/hotline null thì không bịa thông tin) */}
            <div className="text-center pb-3 border-b border-dashed border-slate-300">
              <h2 className={`${is58 ? 'text-sm' : 'text-base'} font-extrabold tracking-tight uppercase text-slate-900 m-0`}>
                {receipt.tenantName || 'CỬA HÀNG OISMS'}
              </h2>
              <div className={`${is58 ? 'text-[10px]' : 'text-[11px]'} text-slate-600 font-medium mt-0.5`}>
                {receipt.branchName || 'Chi nhánh POS'}
              </div>
              {receipt.branchAddress && (
                <div className={`${is58 ? 'text-[9px]' : 'text-[10px]'} text-slate-500 mt-0.5 break-words`}>
                  Đ/c: {receipt.branchAddress}
                </div>
              )}
              {receipt.branchPhone && (
                <div className={`${is58 ? 'text-[9px]' : 'text-[10px]'} text-slate-500`}>
                  Hotline: {receipt.branchPhone}
                </div>
              )}
            </div>

            {/* Receipt Title & Meta */}
            <div className="py-2.5 text-center border-b border-dashed border-slate-300">
              <div className={`${is58 ? 'text-xs' : 'text-sm'} font-black tracking-wider uppercase text-slate-900`}>
                {receipt.isDraft ? 'PHIẾU TẠM TÍNH' : 'HÓA ĐƠN BÁN HÀNG'}
              </div>
              {receipt.isDraft && (
                <div className="text-[10px] font-bold text-rose-600 uppercase mt-0.5">
                  (Chưa thanh toán - Không có giá trị khấu trừ thuế)
                </div>
              )}
              <div className="text-[10px] text-slate-500 mt-0.5">
                Số HĐ: <span className="font-mono font-bold text-slate-800">{receipt.receiptNumber}</span>
              </div>
              <div className="text-[10px] text-slate-500">
                Ngày: {receipt.createdAt}
              </div>
              <div className="text-[10px] text-slate-500">
                Thu ngân: <span className="font-semibold text-slate-700">{receipt.cashierName}</span>
              </div>
              {receipt.customerName && (
                <div className="text-[10px] text-slate-500">
                  Khách: <span className="font-semibold text-slate-700">{receipt.customerName}</span>{receipt.customerPhone ? ` (${receipt.customerPhone})` : ''}
                </div>
              )}
            </div>

            {/* Items Table (T05-AC-02: Không che/cắt cột số tiền trên cả khổ 80mm và 58mm) */}
            <div className="py-2.5 border-b border-dashed border-slate-300">
              {is58 ? (
                // Bố cục 2 dòng/món cho khổ 58mm (K58): Đảm bảo tên dài tự xuống dòng, số lượng, đơn giá và thành tiền không bao giờ bị cắt
                <div className="space-y-2 divide-y divide-slate-100">
                  {receipt.items.map((item, idx) => (
                    <div key={idx} className="pt-1.5 first:pt-0">
                      <div className="font-semibold text-slate-800 leading-tight break-words">
                        {item.name}
                      </div>
                      <div className="text-[8.5px] text-slate-400 font-mono">
                        {item.sku}
                      </div>
                      <div className="flex justify-between items-baseline mt-0.5 text-[9.5px]">
                        <span className="text-slate-600">
                          <span className="font-bold text-slate-700">{item.quantity}</span> x {item.unitPrice.toLocaleString('vi-VN')}
                        </span>
                        <span className="font-bold text-slate-900 whitespace-nowrap">
                          {item.subtotal.toLocaleString('vi-VN')}
                        </span>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                // Bố cục bảng 4 cột cho khổ 80mm (K80)
                <table className="w-full text-[11px]">
                  <thead>
                    <tr className="border-b border-slate-200 text-slate-500 font-bold">
                      <th className="text-left pb-1">Mặt hàng</th>
                      <th className="text-center pb-1 w-8">SL</th>
                      <th className="text-right pb-1">Đơn giá</th>
                      <th className="text-right pb-1">T.Tiền</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {receipt.items.map((item, idx) => (
                      <tr key={idx} className="align-top">
                        <td className="py-1 pr-1 break-words">
                          <div className="font-semibold text-slate-800 leading-tight">
                            {item.name}
                          </div>
                          <div className="text-[9px] text-slate-400 font-mono">
                            {item.sku}
                          </div>
                        </td>
                        <td className="py-1 text-center font-bold text-slate-700">
                          {item.quantity}
                        </td>
                        <td className="py-1 text-right text-slate-600 whitespace-nowrap">
                          {item.unitPrice.toLocaleString('vi-VN')}
                        </td>
                        <td className="py-1 text-right font-bold text-slate-900 whitespace-nowrap">
                          {item.subtotal.toLocaleString('vi-VN')}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>

            {/* Calculations & Totals */}
            <div className="py-2.5 space-y-1 text-[11px] border-b border-dashed border-slate-300">
              <div className="flex justify-between items-center text-sm font-black pt-1">
                <span>TỔNG CỘNG:</span>
                <span className="font-mono text-base">{formatCurrency(receipt.totalAmount)}</span>
              </div>
              <div className="flex justify-between text-slate-600 pt-1">
                <span>Hình thức:</span>
                <span className="font-bold">
                  {formatPaymentMethod(receipt.paymentMethod)}
                </span>
              </div>
              {isCash && (
                <>
                  <div className="flex justify-between text-slate-600">
                    <span>Tiền khách đưa:</span>
                    <span className="font-mono font-medium">{formatCurrency(receipt.amountGiven)}</span>
                  </div>
                  <div className="flex justify-between text-slate-900 font-bold">
                    <span>Tiền thừa trả:</span>
                    <span className="font-mono">{formatCurrency(receipt.changeDue)}</span>
                  </div>
                </>
              )}
            </div>

            {/* Footer Barcode & Thank You */}
            <div className="pt-3 text-center space-y-1">
              <div className="font-bold text-[11px] text-slate-800">
                CẢM ƠN QUÝ KHÁCH & HẸN GẶP LẠI!
              </div>
              <div className="text-[9px] text-slate-400">
                (Được in từ hệ thống OISMS POS - Phiên bản PWA)
              </div>
              {/* Simulated Barcode */}
              <div className="flex justify-center pt-2">
                <svg className="w-36 h-8" viewBox="0 0 100 25" preserveAspectRatio="none">
                  <g fill="#000000">
                    <rect x="2" y="0" width="2" height="25" />
                    <rect x="6" y="0" width="1" height="25" />
                    <rect x="9" y="0" width="3" height="25" />
                    <rect x="14" y="0" width="1" height="25" />
                    <rect x="17" y="0" width="2" height="25" />
                    <rect x="21" y="0" width="4" height="25" />
                    <rect x="27" y="0" width="1" height="25" />
                    <rect x="30" y="0" width="2" height="25" />
                    <rect x="34" y="0" width="3" height="25" />
                    <rect x="39" y="0" width="1" height="25" />
                    <rect x="42" y="0" width="2" height="25" />
                    <rect x="46" y="0" width="2" height="25" />
                    <rect x="50" y="0" width="1" height="25" />
                    <rect x="53" y="0" width="3" height="25" />
                    <rect x="58" y="0" width="2" height="25" />
                    <rect x="62" y="0" width="4" height="25" />
                    <rect x="68" y="0" width="1" height="25" />
                    <rect x="71" y="0" width="2" height="25" />
                    <rect x="75" y="0" width="3" height="25" />
                    <rect x="80" y="0" width="1" height="25" />
                    <rect x="83" y="0" width="3" height="25" />
                    <rect x="88" y="0" width="1" height="25" />
                    <rect x="91" y="0" width="2" height="25" />
                    <rect x="95" y="0" width="2" height="25" />
                  </g>
                </svg>
              </div>
              <div className="font-mono text-[9px] text-slate-500 tracking-widest">
                *{receipt.receiptNumber}*
              </div>
            </div>
          </div>
        </div>

        {/* Modal Footer (hidden on print) */}
        <div className="px-5 py-3 bg-slate-50 border-t border-slate-200 flex justify-end gap-2 no-print">
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 bg-slate-200 hover:bg-slate-300 text-slate-700 rounded-xl text-xs font-semibold transition-colors"
          >
            Đóng
          </button>
          <button
            type="button"
            onClick={handlePrint}
            className="px-5 py-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-xl text-xs font-bold transition-colors shadow-sm"
          >
            In Ngay (Enter)
          </button>
        </div>
      </div>
    </div>
  );
};
