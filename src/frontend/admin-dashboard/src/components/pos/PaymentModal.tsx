import React, { useRef, useState, useEffect } from 'react';
import { X, Banknote, QrCode, Printer, AlertCircle, ShieldCheck, CheckCircle2 } from 'lucide-react';
import type { CartItem, PosCheckoutRequest, ReceiptData } from '../../types/pos';
import type { BankConfig, PaymentAccountOption, VietQrResult } from '../../types/qr';
import type { CurrentPriceItem } from '../../services/posCartUnits';
import { checkoutPosOrder } from '../../services/posApi';
import {
  generateVietQrPayload,
  getPosBankConfig,
  getPaymentAccountsSync,
  loadPaymentAccounts,
  toBankConfig,
  getOrCreatePosQrIntent,
  clearPosQrIntent,
  isPosQrIntentFor,
  extractShortReference,
  generateUuidV4,
} from '../../services/qrService';

interface PaymentModalProps {
  embedded?: boolean;
  onBusyChange?: (busy: boolean) => void;
  submitRef?: React.RefObject<HTMLButtonElement | null>;
  isOpen: boolean;
  onClose: () => void;
  items: CartItem[];
  branchId: string;
  branchName: string;
  branchAddress?: string;
  branchPhone?: string;
  tenantName: string;
  cashierName: string;
  currentUser?: {
    id?: string;
    tenantId?: string;
    fullName?: string;
    email?: string;
    tenantName?: string;
  };
  onSuccessAndPrint: (receipt: ReceiptData) => void;
  onPreviewReceipt: (receipt: ReceiptData) => void;
  onApplyCurrentPrices?: (prices: CurrentPriceItem[]) => void;
}

export const PaymentModal: React.FC<PaymentModalProps> = ({
  embedded = false,
  onBusyChange,
  submitRef,
  isOpen,
  onClose,
  items,
  branchId,
  branchName,
  branchAddress,
  branchPhone,
  tenantName,
  cashierName,
  currentUser,
  onApplyCurrentPrices,
  onSuccessAndPrint,
  onPreviewReceipt,
}) => {
  const submittingRef = useRef(false);
  const activeUser = currentUser || {
    id: undefined,
    tenantId: undefined,
    fullName: cashierName,
    email: undefined,
    tenantName,
  };

  const [paymentMethod, setPaymentMethod] = useState<'CASH' | 'BANK_TRANSFER_QR'>('CASH');
  const [customerName, setCustomerName] = useState('');
  const [customerPhone, setCustomerPhone] = useState('');
  const [userGivenAmount, setUserGivenAmount] = useState<number | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [apiNotice, setApiNotice] = useState<{ type: 'warning' | 'error' | 'success'; text: string; details?: any } | null>(null);
  const [priceUpdates, setPriceUpdates] = useState<CurrentPriceItem[] | null>(null);

  const [asyncAccounts, setAsyncAccounts] = useState<{ tenantId?: string; accounts: PaymentAccountOption[] }>({
    tenantId: undefined,
    accounts: [],
  });
  const [selectedAccountId, setSelectedAccountId] = useState<string>('');

  const syncAccounts = getPaymentAccountsSync(activeUser?.tenantId, branchId);
  const activeAccounts = syncAccounts.length > 0
    ? syncAccounts
    : (asyncAccounts.tenantId === activeUser?.tenantId ? asyncAccounts.accounts : []);

  const selectedAccount = activeAccounts.find((a) => a.id === selectedAccountId) ||
    activeAccounts.find((a) => a.isDefault) ||
    activeAccounts[0] ||
    null;

  const currentBankConfig: BankConfig | null = selectedAccount
    ? toBankConfig(selectedAccount)
    : getPosBankConfig(branchId, activeUser?.tenantId);

  useEffect(() => {
    if (isOpen && paymentMethod === 'BANK_TRANSFER_QR' && activeUser?.tenantId && syncAccounts.length === 0) {
      loadPaymentAccounts(activeUser.tenantId, branchId).then((accounts) => {
        if (accounts.length > 0) {
          setAsyncAccounts({ tenantId: activeUser.tenantId, accounts });
          setSelectedAccountId((prev) => (prev && accounts.some((a) => a.id === prev) ? prev : (accounts.find((a) => a.isDefault)?.id || accounts[0].id)));
        }
      });
    }
  }, [isOpen, paymentMethod, activeUser?.tenantId, branchId, syncAccounts.length]);

  const totalAmount = items.reduce((sum, item) => sum + item.retailPrice * item.quantity, 0);
  const amountGiven = userGivenAmount !== null ? userGivenAmount : totalAmount;

  const cartFingerprint = JSON.stringify(items.map((i) => [i.variantId, i.unitId || '', i.sku, i.quantity, i.retailPrice]));

  const intent = paymentMethod === 'BANK_TRANSFER_QR'
    ? getOrCreatePosQrIntent(activeUser, branchId, cartFingerprint, totalAmount, currentBankConfig, generateUuidV4, selectedAccount?.id)
    : null;

  const qrIntent = isPosQrIntentFor(intent, activeUser, branchId) ? intent : null;
  const activeRequestKey = qrIntent?.requestKey || generateUuidV4();
  const bankConfig: BankConfig | null = qrIntent?.bankConfig || currentBankConfig;

  let qrResult: VietQrResult | null = null;
  let qrError: string | null = null;

  if (paymentMethod === 'BANK_TRANSFER_QR') {
    if (!bankConfig) {
      qrError = 'Chưa cấu hình tài khoản ngân hàng thụ hưởng nhận tiền.';
    } else {
      try {
        qrResult = generateVietQrPayload(bankConfig, {
          amount: totalAmount,
          requestKey: activeRequestKey,
          branchId,
          orderDescription: `OISMS POS ${extractShortReference(activeRequestKey)}`,
        });
      } catch (err: any) {
        qrError = err.message || 'Lỗi khi tạo mã VietQR.';
      }
    }
  }

  const isBankConfigInvalid = paymentMethod === 'BANK_TRANSFER_QR' && (!bankConfig || Boolean(qrError));

  const handleAccountChange = (newAccountId: string) => {
    if (newAccountId === selectedAccountId) return;
    const ok = typeof window !== 'undefined' && typeof window.confirm === 'function'
      ? window.confirm('Khách hàng đã quét mã QR của tài khoản trước đó chưa? Việc đổi tài khoản sẽ tạo mã QR mới.')
      : true;
    if (ok) {
      setSelectedAccountId(newAccountId);
      clearPosQrIntent(activeUser, branchId);
    }
  };

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
    if (submittingRef.current || items.length === 0 || !branchId) return;

    if (isShortOfCash && !forcePrint) {
      setApiNotice({
        type: 'error',
        text: 'Tiền khách đưa chưa đủ để thanh toán đơn hàng.',
      });
      return;
    }

    if (paymentMethod === 'BANK_TRANSFER_QR' && isBankConfigInvalid) {
      setApiNotice({
        type: 'error',
        text: qrError || 'Chưa cấu hình tài khoản ngân hàng thụ hưởng hợp lệ để thanh toán QR.',
      });
      return;
    }

    submittingRef.current = true;
    setIsSubmitting(true);
    onBusyChange?.(true);
    setApiNotice(null);

    const orderNumber = `ORD-POS-${Date.now().toString().slice(-6)}`;
    const requestKey = paymentMethod === 'BANK_TRANSFER_QR' ? activeRequestKey : generateUuidV4();

    const checkoutRequest: PosCheckoutRequest = {
      branchId: branchId || '00000000-0000-0000-0000-000000000000',
      channel: 'POS',
      customerName: customerName.trim() || undefined,
      customerPhone: customerPhone.trim() || undefined,
      paymentMethod,
      amountGiven: paymentMethod === 'CASH' ? amountGiven : totalAmount,
      changeDue: paymentMethod === 'CASH' ? changeDue : 0,
      paymentAccountId: paymentMethod === 'BANK_TRANSFER_QR' && selectedAccount && !selectedAccount.id.startsWith('legacy-')
        ? selectedAccount.id
        : undefined,
      transferReference: paymentMethod === 'BANK_TRANSFER_QR' ? (qrResult?.transferContent) : undefined,
      requestKey,
      items: items.map((i) => ({
        skuId: i.variantId,
        skuCode: i.sku,
        productName: i.productName,
        variantName: i.variantName,
        unitId: i.unitId || undefined,
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
        unitName: i.unitName,
        quantity: i.quantity,
        unitPrice: i.retailPrice,
        subtotal: i.retailPrice * i.quantity,
      })),
      totalAmount,
      paymentMethod,
      amountGiven: paymentMethod === 'CASH' ? amountGiven : totalAmount,
      changeDue: paymentMethod === 'CASH' ? changeDue : 0,
      paymentAccountId: checkoutRequest.paymentAccountId,
      transferReference: checkoutRequest.transferReference,
      receiverBankBin: bankConfig?.bankBin,
      receiverBankName: bankConfig?.bankName,
      receiverAccountNumber: bankConfig?.accountNumber,
      receiverAccountName: bankConfig?.accountName,
      isDraft: false,
    };

    try {
      const result = await checkoutPosOrder(checkoutRequest);

      // [BF-019] Chỉ xác nhận thành công khi server trả về mã đơn hàng hợp lệ
      if (result.success && result.orderNumber) {
        clearPosQrIntent(activeUser, branchId);
        receiptData.receiptNumber = result.receiptNumber || result.receipt?.receiptNumber || result.orderNumber;
        receiptData.orderNumber = result.orderNumber;
        if (result.receipt) {
          if (result.receipt.receiverBankBin) receiptData.receiverBankBin = result.receipt.receiverBankBin;
          if (result.receipt.receiverBankName) receiptData.receiverBankName = result.receipt.receiverBankName;
          if (result.receipt.receiverAccountNumber) receiptData.receiverAccountNumber = result.receipt.receiverAccountNumber;
          if (result.receipt.receiverAccountName) receiptData.receiverAccountName = result.receipt.receiverAccountName;
          if (result.receipt.transferReference) receiptData.transferReference = result.receipt.transferReference;
        }
        onSuccessAndPrint(receiptData);
        onClose();
      } else {
        // Checkout thất bại hoặc API chưa sẵn sàng: Giữ nguyên giỏ hàng, thông báo rõ ràng
        if (result.errorCode === 'PRICE_MISMATCH' && result.currentPrices?.length) {
          setPriceUpdates(result.currentPrices);
        }
        setApiNotice({
          type: result.isPendingBackend ? 'warning' : 'error',
          text: result.message || 'Thanh toán không thành công. Giỏ hàng được bảo lưu.',
          details: checkoutRequest,
        });
      }
    } catch (err: any) {
      setApiNotice({
        type: 'error',
        text: `Lỗi gọi API: ${err.message}`,
      });
    } finally {
      submittingRef.current = false;
      setIsSubmitting(false);
      onBusyChange?.(false);
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

  const priceUpdateNotice = priceUpdates && (
    <div role="alert" style={{ background: '#fffbeb', border: '1px solid #f59e0b', borderRadius: '8px', padding: '10px 12px', fontSize: '12px', color: '#78350f', margin: '8px 0' }}>
      <strong>Giá niêm yết đã thay đổi. Vui lòng xác nhận giá mới trước khi thanh toán lại:</strong>
      <ul style={{ margin: '6px 0', paddingLeft: '18px' }}>
        {priceUpdates.map((p) => (
          <li key={`${p.skuId}:${p.unitId || ''}`}>{p.skuCode || p.skuId}: {p.currentPrice.toLocaleString('vi-VN')} ₫</li>
        ))}
      </ul>
      <button
        type="button"
        onClick={() => {
          onApplyCurrentPrices?.(priceUpdates);
          setPriceUpdates(null);
          setApiNotice(null);
        }}
        style={{ background: '#b45309', color: '#fff', border: 0, borderRadius: '6px', padding: '6px 10px', fontWeight: 600, cursor: 'pointer' }}
      >
        Cập nhật giá mới vào giỏ
      </button>
    </div>
  );

  if (embedded) return (
    <div className="pos-inline-payment">
      <input className="pos-customer-input" aria-label="Tên khách hàng" value={customerName} onChange={(event)=>setCustomerName(event.target.value)} placeholder="Khách hàng (không bắt buộc)" />
      <input className="pos-customer-input" type="tel" aria-label="Số điện thoại khách hàng" value={customerPhone} onChange={(event)=>setCustomerPhone(event.target.value)} placeholder="Số điện thoại (không bắt buộc)" />
      <div className="pos-payment-methods">
        <button type="button" aria-pressed={paymentMethod==='CASH'} onClick={()=>setPaymentMethod('CASH')}>
          <Banknote size={17} />Tiền mặt
        </button>
        <button type="button" aria-pressed={paymentMethod==='BANK_TRANSFER_QR'} onClick={()=>setPaymentMethod('BANK_TRANSFER_QR')}>
          <QrCode size={17} /><span>Chuyển khoản QR</span>
        </button>
      </div>
      {paymentMethod === 'CASH' ? (
        <div className="pos-cash-row">
          <label>Tiền khách đưa
            <input type="number" min="0" aria-label="Tiền khách đưa" value={amountGiven || ''} onChange={(event)=>setUserGivenAmount(Number(event.target.value))} placeholder="Nhập số tiền" />
          </label>
          <div className={isShortOfCash ? 'pos-change is-short' : 'pos-change'}>
            <small>{isShortOfCash ? 'Còn thiếu' : 'Tiền thừa'}</small>
            <strong>{formatCurrency(isShortOfCash ? totalAmount - amountGiven : changeDue)}</strong>
          </div>
        </div>
      ) : (
        <div className="pos-qr-embedded-box" style={{ padding: '12px', background: '#f8fafc', borderRadius: '10px', border: '1px solid #e2e8f0', textAlign: 'center' }}>
          {isBankConfigInvalid || !qrResult ? (
            <div style={{ padding: '10px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '8px', color: '#991b1b', fontSize: '11px', textAlign: 'left' }}>
              <strong>Chưa có tài khoản nhận tiền</strong>
              <p style={{ margin: '4px 0 0 0', fontSize: '10px', color: '#b91c1c' }}>
                {qrError || 'Cửa hàng chưa cấu hình tài khoản ngân hàng thụ hưởng. Vui lòng chọn Tiền mặt hoặc liên hệ Owner cấu hình trong mục Quản trị.'}
              </p>
            </div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '8px' }}>
              {activeAccounts.length > 1 && (
                <div style={{ width: '100%', textAlign: 'left' }}>
                  <label style={{ fontSize: '10px', fontWeight: 'bold', color: '#475569', display: 'block', marginBottom: '3px' }}>
                    Tài khoản nhận tiền:
                  </label>
                  <select
                    value={selectedAccountId}
                    disabled={isSubmitting}
                    onChange={(e) => handleAccountChange(e.target.value)}
                    style={{ width: '100%', fontSize: '11px', padding: '5px 8px', borderRadius: '6px', border: '1px solid #cbd5e1', background: '#fff' }}
                  >
                    {activeAccounts.map((acc) => (
                      <option key={acc.id} value={acc.id}>
                        {acc.bankShortName} - •••• {acc.accountNumber.slice(-4)} ({acc.accountName}) {acc.isDefault ? '★ Mặc định' : ''}
                      </option>
                    ))}
                  </select>
                </div>
              )}
              {bankConfig?.isTestFixture && (
                <div style={{ fontSize: '10px', fontWeight: 'bold', color: '#b45309', background: '#fef3c7', padding: '3px 8px', borderRadius: '4px', border: '1px solid #fde68a' }}>
                  DỮ LIỆU KIỂM THỬ — không chuyển tiền thật
                </div>
              )}
              <div style={{ background: '#fff', padding: '8px', borderRadius: '10px', border: '1px solid #e2e8f0', display: 'inline-block', boxShadow: '0 1px 3px rgba(0,0,0,0.05)' }}>
                <img
                  src={qrResult.quickLinkUrl}
                  alt="VietQR Code"
                  style={{ width: '150px', height: '150px', display: 'block', objectFit: 'contain' }}
                />
              </div>
              <div style={{ fontSize: '11px', color: '#334155', lineHeight: '1.4' }}>
                <div><strong>{bankConfig?.bankName}</strong> ({bankConfig?.accountNumber})</div>
                <div>Chủ TK: <strong>{bankConfig?.accountName}</strong></div>
                <div style={{ fontSize: '10px', color: '#4f46e5', marginTop: '2px', fontWeight: 'bold' }}>
                  Nội dung CK: <span>{qrResult.transferContent}</span>
                </div>
              </div>
              <div style={{ fontSize: '10px', color: '#1e3a8a', background: '#eff6ff', padding: '6px 8px', borderRadius: '6px', border: '1px solid #dbeafe', textAlign: 'left', width: '100%' }}>
                Thu ngân đối soát số dư ngân hàng. Sau khi tiền đã vào tài khoản, bấm nút <strong>"Xác nhận đã nhận tiền"</strong> bên dưới để hoàn tất.
              </div>
            </div>
          )}
        </div>
      )}
      {priceUpdateNotice}
      {apiNotice && <p className="pos-error" role="alert">{apiNotice.text} Giỏ hàng được giữ lại để kiểm tra.</p>}
      <button
        ref={submitRef}
        className="pos-checkout-button"
        type="button"
        disabled={isSubmitting || (paymentMethod === 'CASH' && isShortOfCash) || (paymentMethod === 'BANK_TRANSFER_QR' && isBankConfigInvalid) || !items.length || !branchId}
        onClick={()=>handleCheckout(false)}
      >
        {isSubmitting ? 'Đang xử lý...' : paymentMethod === 'BANK_TRANSFER_QR' ? `Xác nhận đã nhận tiền · ${formatCurrency(totalAmount)}` : `Thanh toán · ${formatCurrency(totalAmount)}`}
      </button>
      <button className="pos-draft-button" type="button" disabled={!items.length || isSubmitting} onClick={handlePrintPreview}>
        <Printer size={14} />In phiếu tạm tính
      </button>
    </div>
  );

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
              {isBankConfigInvalid || !qrResult ? (
                <div className="p-4 bg-rose-50 border border-rose-200 rounded-2xl text-xs text-rose-800 text-left max-w-sm">
                  <div className="font-bold flex items-center gap-1.5 text-rose-900 mb-1">
                    <AlertCircle className="w-4 h-4 shrink-0 text-rose-600" />
                    <span>Chưa cấu hình tài khoản nhận tiền</span>
                  </div>
                  <p className="m-0 leading-relaxed text-[11px] text-rose-700">
                    {qrError || 'Cửa hàng chưa có tài khoản ngân hàng nhận tiền hợp lệ. Nút thanh toán QR bị khóa. Vui lòng thanh toán Tiền mặt hoặc liên hệ Owner cấu hình trong Quản trị.'}
                  </p>
                </div>
              ) : (
                <>
                  {activeAccounts.length > 1 && (
                    <div className="w-full max-w-sm mb-3 text-left">
                      <label className="text-[11px] font-bold text-slate-600 mb-1 block">
                        Tài khoản nhận tiền:
                      </label>
                      <select
                        value={selectedAccountId}
                        disabled={isSubmitting}
                        onChange={(e) => handleAccountChange(e.target.value)}
                        className="w-full bg-white border border-slate-300 rounded-xl px-3 py-2 text-xs font-semibold text-slate-800 focus:outline-none focus:border-indigo-600"
                      >
                        {activeAccounts.map((acc) => (
                          <option key={acc.id} value={acc.id}>
                            {acc.bankShortName} - •••• {acc.accountNumber.slice(-4)} ({acc.accountName}) {acc.isDefault ? '★ Mặc định' : ''}
                          </option>
                        ))}
                      </select>
                    </div>
                  )}

                  {bankConfig?.isTestFixture && (
                    <p className="text-xs font-bold text-amber-800 bg-amber-100 px-3 py-1 rounded-full border border-amber-300 mb-3">
                      DỮ LIỆU KIỂM THỬ — không chuyển tiền thật
                    </p>
                  )}

                  <div className="bg-white p-3 rounded-2xl shadow-md border border-slate-200 mb-3 relative flex items-center justify-center min-w-[200px] min-h-[200px]">
                    <img
                      src={qrResult.quickLinkUrl}
                      alt="VietQR Code"
                      className="w-52 h-52 object-contain"
                    />
                  </div>

                  <div className="space-y-1.5 text-xs text-slate-600 max-w-sm w-full bg-white p-3.5 rounded-xl border border-slate-200">
                    <div className="flex justify-between items-center text-slate-700">
                      <span>Ngân hàng:</span>
                      <strong className="text-slate-900">{bankConfig?.bankName}</strong>
                    </div>
                    <div className="flex justify-between items-center text-slate-700">
                      <span>Số tài khoản:</span>
                      <strong className="font-mono text-slate-900">{bankConfig?.accountNumber}</strong>
                    </div>
                    <div className="flex justify-between items-center text-slate-700">
                      <span>Chủ tài khoản:</span>
                      <strong className="text-slate-900">{bankConfig?.accountName}</strong>
                    </div>
                    <div className="flex justify-between items-center text-slate-700 pt-1 border-t border-slate-100">
                      <span>Số tiền:</span>
                      <strong className="text-indigo-600 text-sm">{formatCurrency(totalAmount)}</strong>
                    </div>
                    <div className="flex justify-between items-center text-slate-700 pt-1 border-t border-slate-100">
                      <span>Nội dung chuyển khoản:</span>
                      <span className="font-mono font-bold text-indigo-700 bg-indigo-50 px-2 py-0.5 rounded text-xs">
                        {qrResult.transferContent}
                      </span>
                    </div>
                  </div>

                  <div className="mt-3 p-3 bg-blue-50 border border-blue-200 rounded-xl text-blue-900 text-left text-xs max-w-sm w-full">
                    <div className="font-bold flex items-center gap-1.5 text-blue-800 mb-1">
                      <CheckCircle2 className="w-4 h-4 text-blue-600 shrink-0" />
                      <span>Xác nhận giao dịch chuyển khoản</span>
                    </div>
                    <p className="m-0 text-[11px] text-blue-700 leading-relaxed">
                      Yêu cầu khách hàng chuyển khoản đúng số tiền và nội dung. Sau khi kiểm tra tiền đã vào tài khoản cửa hàng, thu ngân bấm nút xác nhận bên dưới để hoàn tất.
                    </p>
                  </div>
                </>
              )}
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
          {priceUpdateNotice}
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
          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={isSubmitting}
              onClick={onClose}
              className="px-4 py-2 text-xs font-semibold text-slate-600 hover:bg-slate-200 rounded-xl transition-colors disabled:opacity-50"
            >
              Đóng lại (Esc)
            </button>
            <button
              type="button"
              disabled={isSubmitting}
              onClick={handlePrintPreview}
              className="px-3.5 py-2 text-xs font-semibold text-amber-800 bg-amber-50 hover:bg-amber-100 border border-amber-200 rounded-xl transition-colors flex items-center gap-1.5 disabled:opacity-50"
              title="In phiếu tạm tính (chưa thanh toán)"
            >
              <Printer className="w-3.5 h-3.5 text-amber-600" />
              <span>In phiếu tạm tính</span>
            </button>
          </div>

          <button
            type="button"
            disabled={isSubmitting || (paymentMethod === 'CASH' && isShortOfCash) || (paymentMethod === 'BANK_TRANSFER_QR' && isBankConfigInvalid)}
            onClick={() => handleCheckout(false)}
            className={`inline-flex items-center gap-2 px-6 py-2.5 rounded-xl font-bold text-sm shadow-md transition-all ${
              isSubmitting || (paymentMethod === 'CASH' && isShortOfCash) || (paymentMethod === 'BANK_TRANSFER_QR' && isBankConfigInvalid)
                ? 'bg-slate-300 text-slate-500 cursor-not-allowed shadow-none'
                : 'bg-indigo-600 hover:bg-indigo-700 text-white shadow-indigo-600/30'
            }`}
          >
            {isSubmitting ? (
              <span>Đang gửi Checkout API...</span>
            ) : paymentMethod === 'BANK_TRANSFER_QR' ? (
              <>
                <CheckCircle2 className="w-4 h-4" />
                <span>Xác nhận đã nhận tiền (F4)</span>
              </>
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
