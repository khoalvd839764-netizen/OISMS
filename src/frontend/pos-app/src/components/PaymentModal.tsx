import React, { useState } from 'react';
import { X, Banknote, QrCode, Printer, AlertCircle, ShieldCheck } from 'lucide-react';
import type { CartItem, PosCheckoutRequest, ReceiptData } from '../types/pos';
import { checkoutPosOrder } from '../services/api';

interface PaymentModalProps {
  isOpen: boolean;
  onClose: () => void;
  items: CartItem[];
  branchId: string;
  branchName: string;
  branchAddress?: string;
  branchPhone?: string;
  tenantName: string;
  cashierName: string;
  onSuccessAndPrint: (receipt: ReceiptData) => void;
  onPreviewReceipt: (receipt: ReceiptData) => void;
}

export const PaymentModal: React.FC<PaymentModalProps> = ({
  isOpen,
  onClose,
  items,
  branchId,
  branchName,
  branchAddress,
  branchPhone,
  tenantName,
  cashierName,
  onSuccessAndPrint,
  onPreviewReceipt,
}) => {
  const [paymentMethod, setPaymentMethod] = useState<'CASH' | 'BANK_TRANSFER_QR'>('CASH');
  const [customerName, setCustomerName] = useState('');
  const [customerPhone, setCustomerPhone] = useState('');
  const [userGivenAmount, setUserGivenAmount] = useState<number | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [apiNotice, setApiNotice] = useState<{ type: 'warning' | 'error' | 'success'; text: string; details?: any } | null>(null);

  const totalAmount = items.reduce((sum, item) => sum + item.retailPrice * item.quantity, 0);
  const amountGiven = userGivenAmount !== null ? userGivenAmount : totalAmount;

  if (!isOpen) return null;

  const changeDue = Math.max(0, amountGiven - totalAmount);
  const isShortOfCash = paymentMethod === 'CASH' && amountGiven < totalAmount;

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
  };

  const quickCashOptions = [
    { label: 'Đúng số tiền', value: totalAmount },
    { label: '50.000 đ', value: 50000 },
    { label: '100.000 đ', value: 100000 },
    { label: '200.000 đ', value: 200000 },
    { label: '500.000 đ', value: 500000 },
    { label: '1.000.000 đ', value: 1000000 },
  ];

  const handleCheckout = async (forcePrint = false) => {
    if (isShortOfCash && !forcePrint) {
      setApiNotice({
        type: 'error',
        text: 'Tiền khách đưa chưa đủ để thanh toán đơn hàng.',
      });
      return;
    }

    setIsSubmitting(true);
    setApiNotice(null);

    const orderNumber = `ORD-POS-${Date.now().toString().slice(-6)}`;
    const checkoutRequest: PosCheckoutRequest = {
      branchId: branchId || '00000000-0000-0000-0000-000000000000',
      channel: 'POS',
      customerName: customerName.trim() || undefined,
      customerPhone: customerPhone.trim() || undefined,
      paymentMethod,
      amountGiven: paymentMethod === 'CASH' ? amountGiven : totalAmount,
      changeDue: paymentMethod === 'CASH' ? changeDue : 0,
      requestKey: crypto.randomUUID ? crypto.randomUUID() : `req-${Date.now()}`,
      items: items.map((i) => ({
        skuId: i.variantId,
        skuCode: i.sku,
        productName: i.productName,
        variantName: i.variantName,
        quantity: i.quantity,
        unitPrice: i.retailPrice,
      })),
      totalAmount,
    };

    // Chuẩn bị dữ liệu phiếu in hóa đơn
    const receiptData: ReceiptData = {
      receiptNumber: orderNumber,
      createdAt: new Date().toLocaleString('vi-VN'),
      tenantName: tenantName || 'CỬA HÀNG OISMS',
      branchName: branchName || 'Chi nhánh mặc định',
      branchAddress: branchAddress || 'Chi nhánh bán lẻ POS',
      branchPhone: branchPhone || '1900 6868',
      cashierName: cashierName || 'Thu ngân',
      customerName: customerName.trim() || undefined,
      customerPhone: customerPhone.trim() || undefined,
      items: items.map((i) => ({
        name: i.productName + (i.variantName ? ` - ${i.variantName}` : ''),
        sku: i.sku,
        quantity: i.quantity,
        unitPrice: i.retailPrice,
        subtotal: i.retailPrice * i.quantity,
      })),
      totalAmount,
      paymentMethod,
      amountGiven: paymentMethod === 'CASH' ? amountGiven : totalAmount,
      changeDue: paymentMethod === 'CASH' ? changeDue : 0,
      isDraft: false,
    };

    try {
      const result = await checkoutPosOrder(checkoutRequest);

      if (result.success) {
        receiptData.receiptNumber = result.orderNumber || (result.receipt && result.receipt.receiptNumber) || orderNumber;
        if (result.receipt) {
          receiptData.createdAt = new Date(result.receipt.createdAt || Date.now()).toLocaleString('vi-VN');
          if (result.receipt.cashierName) receiptData.cashierName = result.receipt.cashierName;
          if (result.receipt.branchName) receiptData.branchName = result.receipt.branchName;
        }
        onSuccessAndPrint(receiptData);
        onClose();
      } else {
        // Checkout thất bại hoặc API chưa sẵn sàng: Giữ nguyên giỏ hàng, thông báo rõ ràng
        setApiNotice({
          type: 'warning',
          text: result.message,
          details: checkoutRequest,
        });
      }
    } catch (err: any) {
      setApiNotice({
        type: 'error',
        text: `Lỗi gọi API: ${err.message}`,
      });
    } finally {
      setIsSubmitting(false);
    }
  };

  const handlePrintPreview = () => {
    // Phiếu xem trước / tạm tính: tách biệt hoàn toàn khỏi thanh toán thành công, không xóa giỏ
    const draftReceipt: ReceiptData = {
      receiptNumber: `DRAFT-${Date.now().toString().slice(-6)}`,
      createdAt: new Date().toLocaleString('vi-VN'),
      tenantName: tenantName || 'CỬA HÀNG OISMS',
      branchName: branchName || 'Chi nhánh mặc định',
      branchAddress: branchAddress || 'Chi nhánh bán lẻ POS',
      branchPhone: branchPhone || '1900 6868',
      cashierName: cashierName || 'Thu ngân',
      customerName: customerName.trim() || undefined,
      customerPhone: customerPhone.trim() || undefined,
      items: items.map((i) => ({
        name: i.productName + (i.variantName ? ` - ${i.variantName}` : ''),
        sku: i.sku,
        quantity: i.quantity,
        unitPrice: i.retailPrice,
        subtotal: i.retailPrice * i.quantity,
      })),
      totalAmount,
      paymentMethod,
      amountGiven: paymentMethod === 'CASH' ? amountGiven : totalAmount,
      changeDue: paymentMethod === 'CASH' ? changeDue : 0,
      isDraft: true,
      paymentStatusTitle: 'CHƯA THANH TOÁN (PHIẾU TẠM TÍNH)',
    };
    onPreviewReceipt(draftReceipt);
    onClose();
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-fade-in no-print">
      <div className="bg-white rounded-3xl shadow-2xl border border-slate-100 max-w-xl w-full overflow-hidden flex flex-col max-h-[90vh]">
        {/* Modal Header */}
        <div className="px-6 py-4 bg-slate-900 text-white flex items-center justify-between border-b border-slate-800">
          <div>
            <h3 className="text-lg font-bold tracking-tight text-white m-0">Thanh Toán Đơn Hàng</h3>
            <p className="text-xs text-slate-400 m-0">
              Tổng số tiền cần thanh toán: <span className="font-bold text-indigo-400 text-sm">{formatCurrency(totalAmount)}</span>
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Modal Content */}
        <div className="p-6 overflow-y-auto space-y-6">
          {/* Method Selection */}
          <div>
            <label className="text-xs font-bold text-slate-500 uppercase tracking-wider mb-2 block">
              Phương thức thanh toán
            </label>
            <div className="grid grid-cols-2 gap-3">
              <button
                type="button"
                onClick={() => setPaymentMethod('CASH')}
                className={`flex items-center justify-center gap-2.5 p-3.5 rounded-2xl border-2 font-bold text-sm transition-all ${
                  paymentMethod === 'CASH'
                    ? 'border-indigo-600 bg-indigo-50/50 text-indigo-700 shadow-sm'
                    : 'border-slate-200 text-slate-600 hover:bg-slate-50'
                }`}
              >
                <Banknote className="w-5 h-5" />
                <span>Tiền mặt</span>
              </button>

              <button
                type="button"
                onClick={() => setPaymentMethod('BANK_TRANSFER_QR')}
                className={`flex items-center justify-center gap-2.5 p-3.5 rounded-2xl border-2 font-bold text-sm transition-all ${
                  paymentMethod === 'BANK_TRANSFER_QR'
                    ? 'border-indigo-600 bg-indigo-50/50 text-indigo-700 shadow-sm'
                    : 'border-slate-200 text-slate-600 hover:bg-slate-50'
                }`}
              >
                <QrCode className="w-5 h-5" />
                <span>Chuyển khoản QR</span>
              </button>
            </div>
          </div>

          {/* CASH SECTION */}
          {paymentMethod === 'CASH' && (
            <div className="space-y-4 bg-slate-50 p-4 rounded-2xl border border-slate-200/60">
              <div>
                <label className="text-xs font-semibold text-slate-600 mb-1.5 block">
                  Tiền khách đưa:
                </label>
                <div className="relative">
                  <input
                    type="number"
                    value={amountGiven || ''}
                    onChange={(e) => setUserGivenAmount(parseFloat(e.target.value) || 0)}
                    placeholder="Nhập số tiền khách đưa..."
                    className="w-full text-xl font-bold text-slate-900 px-4 py-3 bg-white border border-slate-300 rounded-xl focus:outline-none focus:border-indigo-600 focus:ring-4 focus:ring-indigo-100"
                  />
                  <span className="absolute right-4 top-1/2 -translate-y-1/2 text-sm font-bold text-slate-400">
                    VND
                  </span>
                </div>
              </div>

              {/* Quick Cash Buttons */}
              <div className="grid grid-cols-3 gap-2">
                {quickCashOptions.map((opt, i) => (
                  <button
                    key={i}
                    type="button"
                    onClick={() => setUserGivenAmount(opt.value)}
                    className="py-1.5 px-2 bg-white hover:bg-slate-100 text-slate-700 border border-slate-200 rounded-lg text-xs font-semibold transition-colors"
                  >
                    {opt.label}
                  </button>
                ))}
              </div>

              {/* Change Calculation */}
              <div className="pt-2 border-t border-slate-200 flex items-center justify-between">
                <span className="text-sm font-medium text-slate-600">Tiền thừa trả khách:</span>
                <span
                  className={`text-lg font-extrabold ${
                    isShortOfCash ? 'text-rose-600' : 'text-emerald-600'
                  }`}
                >
                  {isShortOfCash
                    ? `Khách thiếu: ${formatCurrency(totalAmount - amountGiven)}`
                    : formatCurrency(changeDue)}
                </span>
              </div>
            </div>
          )}

          {/* QR CODE SECTION */}
          {paymentMethod === 'BANK_TRANSFER_QR' && (
            <div className="bg-slate-50 p-5 rounded-2xl border border-slate-200/60 flex flex-col items-center text-center">
              <div className="bg-white p-3 rounded-2xl shadow-md border border-slate-200 mb-3">
                {/* Simulated VietQR SVG Code */}
                <svg
                  className="w-48 h-48"
                  viewBox="0 0 200 200"
                  xmlns="http://www.w3.org/2000/svg"
                >
                  <rect width="200" height="200" fill="#ffffff" />
                  {/* Outer QR Border & Pattern simulation */}
                  <rect x="20" y="20" width="40" height="40" fill="#0f172a" />
                  <rect x="25" y="25" width="30" height="30" fill="#ffffff" />
                  <rect x="30" y="30" width="20" height="20" fill="#4f46e5" />

                  <rect x="140" y="20" width="40" height="40" fill="#0f172a" />
                  <rect x="145" y="25" width="30" height="30" fill="#ffffff" />
                  <rect x="150" y="30" width="20" height="20" fill="#4f46e5" />

                  <rect x="20" y="140" width="40" height="40" fill="#0f172a" />
                  <rect x="25" y="145" width="30" height="30" fill="#ffffff" />
                  <rect x="30" y="150" width="20" height="20" fill="#4f46e5" />

                  {/* QR Dots Matrix */}
                  <g fill="#1e293b">
                    <rect x="70" y="25" width="10" height="10" />
                    <rect x="90" y="25" width="10" height="10" />
                    <rect x="110" y="25" width="10" height="10" />
                    <rect x="70" y="45" width="10" height="10" />
                    <rect x="100" y="45" width="20" height="10" />

                    <rect x="30" y="75" width="20" height="10" />
                    <rect x="60" y="75" width="30" height="10" />
                    <rect x="100" y="75" width="10" height="20" />
                    <rect x="120" y="75" width="20" height="10" />
                    <rect x="150" y="75" width="20" height="20" />

                    <rect x="25" y="95" width="20" height="10" />
                    <rect x="60" y="95" width="20" height="20" />
                    <rect x="90" y="105" width="20" height="10" />
                    <rect x="130" y="105" width="10" height="20" />

                    <rect x="75" y="125" width="20" height="10" />
                    <rect x="110" y="125" width="30" height="10" />
                    <rect x="150" y="125" width="20" height="10" />

                    <rect x="75" y="145" width="10" height="20" />
                    <rect x="95" y="145" width="30" height="10" />
                    <rect x="140" y="145" width="20" height="20" />
                    <rect x="80" y="170" width="30" height="10" />
                    <rect x="120" y="170" width="40" height="10" />
                  </g>

                  {/* Center Brand Badge */}
                  <rect x="85" y="85" width="30" height="30" rx="6" fill="#4f46e5" />
                  <text
                    x="100"
                    y="104"
                    fill="#ffffff"
                    fontSize="11"
                    fontWeight="bold"
                    textAnchor="middle"
                  >
                    POS
                  </text>
                </svg>
              </div>

              <div className="space-y-2 text-xs text-slate-600 max-w-sm">
                <div className="p-2.5 bg-amber-50 border border-amber-200 rounded-xl text-amber-900 text-left">
                  <div className="font-bold flex items-center gap-1.5 text-xs text-amber-800 mb-1">
                    <AlertCircle className="w-4 h-4 shrink-0 text-amber-600" />
                    <span>Mã QR mô phỏng (Tính năng đang phát triển)</span>
                  </div>
                  <p className="text-[11px] leading-relaxed text-amber-700 m-0">
                    Cổng VietQR động và Webhook đối soát ngân hàng đang được đội Backend hoàn thiện. <strong>Vui lòng KHÔNG chuyển tiền thật</strong> vì hệ thống chưa thể tự động ghi nhận thanh toán.
                  </p>
                </div>
                <div className="pt-1 text-slate-500 text-[11px]">
                  Số tiền cần thanh toán: <span className="font-bold text-slate-800 text-sm">{formatCurrency(totalAmount)}</span>
                </div>
              </div>
            </div>
          )}

          {/* Customer Optional Info */}
          <div className="grid grid-cols-2 gap-3 pt-2">
            <div>
              <label className="text-xs font-semibold text-slate-600 mb-1 block">
                Tên khách hàng (tùy chọn)
              </label>
              <input
                type="text"
                value={customerName}
                onChange={(e) => setCustomerName(e.target.value)}
                placeholder="Khách lẻ..."
                className="w-full text-xs px-3 py-2 border border-slate-300 rounded-lg focus:outline-none focus:border-indigo-600"
              />
            </div>
            <div>
              <label className="text-xs font-semibold text-slate-600 mb-1 block">
                Số điện thoại (tùy chọn)
              </label>
              <input
                type="text"
                value={customerPhone}
                onChange={(e) => setCustomerPhone(e.target.value)}
                placeholder="09..."
                className="w-full text-xs px-3 py-2 border border-slate-300 rounded-lg focus:outline-none focus:border-indigo-600"
              />
            </div>
          </div>

          {/* API Notice / DoD Alert */}
          {apiNotice && (
            <div
              className={`p-4 rounded-xl text-xs space-y-2 border ${
                apiNotice.type === 'warning'
                  ? 'bg-amber-50 text-amber-900 border-amber-200'
                  : 'bg-rose-50 text-rose-900 border-rose-200'
              }`}
            >
              <div className="flex items-start gap-2">
                <AlertCircle className="w-4 h-4 text-amber-600 shrink-0 mt-0.5" />
                <div className="font-semibold">{apiNotice.text}</div>
              </div>
              <p className="text-[11px] text-slate-600">
                (Đề xuất DTO Checkout POS: Đang chờ Backend thống nhất hợp đồng API Swagger. Trong lúc này, giỏ hàng vẫn được giữ nguyên).
              </p>
              <div className="pt-2 flex gap-2">
                <button
                  type="button"
                  onClick={handlePrintPreview}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-amber-600 hover:bg-amber-700 text-white rounded-lg font-semibold text-xs transition-colors"
                >
                  <Printer className="w-3.5 h-3.5" />
                  <span>Xem phiếu tạm tính (Chưa thanh toán - Giữ nguyên giỏ)</span>
                </button>
              </div>
            </div>
          )}
        </div>

        {/* Modal Footer */}
        <div className="px-6 py-4 bg-slate-50 border-t border-slate-100 flex items-center justify-between gap-3">
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 text-xs font-semibold text-slate-600 hover:bg-slate-200 rounded-xl transition-colors"
          >
            Đóng lại (Esc)
          </button>

          <button
            type="button"
            disabled={isSubmitting || isShortOfCash}
            onClick={() => handleCheckout(false)}
            className={`inline-flex items-center gap-2 px-6 py-2.5 rounded-xl font-bold text-sm shadow-md transition-all ${
              isSubmitting || isShortOfCash
                ? 'bg-slate-300 text-slate-500 cursor-not-allowed shadow-none'
                : 'bg-indigo-600 hover:bg-indigo-700 text-white shadow-indigo-600/30'
            }`}
          >
            {isSubmitting ? (
              <span>Đang gửi Checkout API...</span>
            ) : (
              <>
                <ShieldCheck className="w-4 h-4" />
                <span>Gửi Checkout đơn hàng (F4)</span>
              </>
            )}
          </button>
        </div>
      </div>
    </div>
  );
};
