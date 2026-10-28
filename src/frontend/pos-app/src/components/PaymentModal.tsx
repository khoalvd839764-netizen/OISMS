import React, { useState, useEffect } from 'react';
import { X, Banknote, QrCode, Printer, AlertCircle, ShieldCheck, RefreshCw } from 'lucide-react';
import type { CartItem, PosCheckoutRequest, ReceiptData, PendingCheckoutRecord, PosUser } from '../types/pos';
import {
  checkoutPosOrder,
  generateUuidV4,
  getPendingCheckout,
  savePendingCheckout,
  clearPendingCheckout,
  updatePendingCheckoutStatus,
  getStoredUser,
  isSameOwner,
  mapServerReceiptToReceiptData,
} from '../services/api';
import {
  getPosBankConfig,
  generateVietQrPayload,
  extractShortReference,
  getOrCreatePosQrIntent,
  isPosQrIntentFor,
  clearPosQrIntent,
} from '../services/qrService';
import type { BankConfig, VietQrResult } from '../types/qr';

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
  currentUser?: PosUser | null;
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
  currentUser,
  onSuccessAndPrint,
  onPreviewReceipt,
}) => {
  const activeUser = currentUser || getStoredUser();
  const [paymentMethod, setPaymentMethod] = useState<'CASH' | 'BANK_TRANSFER_QR'>('CASH');
  const [customerName, setCustomerName] = useState('');
  const [customerPhone, setCustomerPhone] = useState('');
  const [userGivenAmount, setUserGivenAmount] = useState<number | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [apiNotice, setApiNotice] = useState<{ type: 'warning' | 'error' | 'success'; text: string; details?: any } | null>(null);
  const [pendingAttempt, setPendingAttempt] = useState<PendingCheckoutRecord | null>(() => {
    return getPendingCheckout(activeUser, branchId);
  });

  // POS-T01-F01: Xác minh nghiêm ngặt owner và branch của attempt trước khi hiển thị hoặc gửi.
  // Tuyệt đối không cho state modal cũ hiển thị hoặc gửi attempt của user trước bằng token của user sau.
  const getActivePendingRecord = (): PendingCheckoutRecord | null => {
    if (pendingAttempt && isSameOwner(pendingAttempt.owner, activeUser, branchId)) {
      return pendingAttempt;
    }
    return getPendingCheckout(activeUser, branchId);
  };

  useEffect(() => {
    if (isOpen) {
      const fresh = getPendingCheckout(activeUser, branchId);
      setPendingAttempt(fresh);
      if (!fresh) {
        setApiNotice(null);
      } else {
        setPaymentMethod(fresh.requestPayload.paymentMethod);
      }
    }
  }, [isOpen, activeUser?.id, activeUser?.tenantId, activeUser?.email, branchId]);

  const totalAmount = items.reduce((sum, item) => sum + item.retailPrice * item.quantity, 0);
  const amountGiven = userGivenAmount !== null ? userGivenAmount : totalAmount;

  if (!isOpen) return null;

  const currentPending = getActivePendingRecord();
  const isRetry = Boolean(currentPending && (currentPending.status === 'unknown' || currentPending.status === 'submitting'));
  const cartFingerprint = JSON.stringify(items.map(i => [i.variantId, i.sku, i.quantity, i.retailPrice]));
  const savedQr = currentPending?.qrSnapshot;
  const intent = paymentMethod === 'BANK_TRANSFER_QR'
    ? (isRetry ? savedQr : getOrCreatePosQrIntent(activeUser, branchId, cartFingerprint, totalAmount, getPosBankConfig(branchId, activeUser?.tenantId), generateUuidV4))
    : null;
  const qrIntent = isPosQrIntentFor(intent, activeUser, branchId) &&
    (!currentPending || (intent.requestKey === currentPending.requestKey && intent.amount === currentPending.requestPayload.totalAmount)) ? intent : null;
  const activeRequestKey = currentPending?.requestKey || qrIntent?.requestKey || '';
  const bankConfig: BankConfig | null = qrIntent?.bankConfig || null;
  let qrResult: VietQrResult | null = null;
  let qrError: string | null = null;

  if (paymentMethod === 'BANK_TRANSFER_QR') {
    if (!bankConfig) {
      qrError = 'Chưa cấu hình tài khoản ngân hàng thụ hưởng cho chi nhánh.';
    } else {
      try {
        qrResult = generateVietQrPayload(bankConfig, {
          amount: qrIntent!.amount,
          requestKey: activeRequestKey,
          branchId,
          orderDescription: `OISMS POS ${extractShortReference(activeRequestKey)}`,
        });
      } catch (err: any) {
        qrError = err.message || 'Lỗi khi tạo mã VietQR.';
      }
    }
  }

  // Older unknown QR attempts have no receiver snapshot: allow only their
  // original checkout replay, without generating a QR to a new recipient.
  const isBankConfigInvalid = paymentMethod === 'BANK_TRANSFER_QR' && !isRetry && (!bankConfig || Boolean(qrError));

  // POS-T01-F01: Cảnh báo API chỉ hiển thị nếu thuộc về giao dịch của người dùng hiện tại
  const isNoticeForCurrent = apiNotice && (!apiNotice.details?.requestKey || apiNotice.details?.requestKey === currentPending?.requestKey);
  const activeNotice = isNoticeForCurrent ? apiNotice : null;

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
    if (isSubmitting) {
      return; // Chặn submit song song
    }

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

    setIsSubmitting(true);
    setApiNotice(null);

    // Xác minh lại owner và branch ngay tại thời điểm submit
    const activePending = getActivePendingRecord();
    const isRetryActive = Boolean(activePending && (activePending.status === 'unknown' || activePending.status === 'submitting'));

    let requestKey: string;
    let checkoutRequest: PosCheckoutRequest;

    if (isRetryActive && activePending) {
      // GIỮ NGUYÊN requestKey và snapshot requestPayload khi retry (T01-AC-01, T01-AC-02, PR24-F01)
      requestKey = activePending.requestKey;
      checkoutRequest = activePending.requestPayload;
      updatePendingCheckoutStatus('submitting', undefined, undefined, activeUser, branchId);
    } else {
      // Giao dịch mới: nếu phương thức là QR thì dùng activeRequestKey khớp với mã hiển thị; nếu là Tiền mặt thì sinh UUID v4 mới
      requestKey = paymentMethod === 'BANK_TRANSFER_QR' ? activeRequestKey : generateUuidV4();
      checkoutRequest = {
        branchId: branchId || '00000000-0000-0000-0000-000000000000',
        channel: 'POS',
        customerName: customerName.trim() || undefined,
        customerPhone: customerPhone.trim() || undefined,
        paymentMethod,
        amountGiven: paymentMethod === 'CASH' ? amountGiven : totalAmount,
        changeDue: paymentMethod === 'CASH' ? changeDue : 0,
        requestKey,
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

      const newRecord: PendingCheckoutRecord = {
        version: 1,
        requestKey,
        owner: {
          tenantId: activeUser?.tenantId,
          tenantCode: activeUser?.tenantCode,
          tenantName: activeUser?.tenantName || tenantName,
          userId: activeUser?.id,
          email: activeUser?.email,
          cashierName: activeUser?.fullName || cashierName,
          branchId,
        },
        createdAt: Date.now(),
        updatedAt: Date.now(),
        status: 'submitting',
        requestPayload: checkoutRequest,
        ...(paymentMethod === 'BANK_TRANSFER_QR' && qrIntent ? { qrSnapshot: qrIntent } : {}),
      };
      savePendingCheckout(newRecord);
      setPendingAttempt(newRecord);
    }

    try {
      const result = await checkoutPosOrder(checkoutRequest);

      if (result.success) {
        if (!result.receipt) {
          throw new Error('Dữ liệu biên lai (receipt snapshot) từ máy chủ bị thiếu.');
        }

        // B01-F02: Ánh xạ và validate receipt snapshot từ server TRƯỚC khi xóa pending.
        // PR24-F03 & T02-AC-01 / T02-AC-02: Tuyệt đối không dùng dữ liệu local để vá receipt chính thức.
        const officialReceipt: ReceiptData = mapServerReceiptToReceiptData(result.receipt, {
          tenantName: activeUser?.tenantName || tenantName,
          fallbackCashierName: activeUser?.fullName || cashierName,
          fallbackBranchName: branchName,
        });

        // B01-F02: CHỈ xóa giỏ và pending sau khi mapping thành công trọn vẹn
        clearPendingCheckout(activeUser, branchId);
        clearPosQrIntent(activeUser, branchId);
        setPendingAttempt(null);

        onSuccessAndPrint(officialReceipt);
        onClose();
      } else if (result.isAuthExpired) {
        // 401 hết phiên: lưu pending trạng thái unknown/chờ đăng nhập, không làm mất giỏ
        updatePendingCheckoutStatus('unknown', result.message, result.errorCode, activeUser, branchId);
        setPendingAttempt(getPendingCheckout(activeUser, branchId));
        setApiNotice({
          type: 'warning',
          text: result.message,
          details: checkoutRequest,
        });
      } else if (result.isUnknown || isRetryActive) {
        // Kết quả chưa xác định (timeout, mất kết nối sau commit, 500/502/503/504, 2xx lỗi JSON): giữ nguyên pending để retry (POS-T01-F02, POS-T01-F04)
        updatePendingCheckoutStatus('unknown', result.message, result.errorCode, activeUser, branchId);
        setPendingAttempt(getPendingCheckout(activeUser, branchId));
        setApiNotice({
          type: 'warning',
          text: result.message,
          details: checkoutRequest,
        });
      } else {
        // Thất bại xác định (400 validation, 403, 409 thiếu tồn/mismatch, 404/405/501): Xóa pending
        clearPendingCheckout(activeUser, branchId);
        setPendingAttempt(null);
        setApiNotice({
          type: 'warning',
          text: result.message,
          details: checkoutRequest,
        });
      }
    } catch (err: any) {
      updatePendingCheckoutStatus('unknown', err.message, undefined, activeUser, branchId);
      setPendingAttempt(getPendingCheckout(activeUser, branchId));
      setApiNotice({
        type: 'error',
        text: `Lỗi gọi API: ${err.message}. Kết quả giao dịch chưa xác định, mã yêu cầu được giữ nguyên để đối soát/thử lại.`,
        details: checkoutRequest,
      });
    } finally {
      setIsSubmitting(false);
    }
  };

  const handlePrintPreview = () => {
    // T02-AC-04: Phiếu xem trước / tạm tính: tách biệt hoàn toàn khỏi thanh toán thành công, không gọi API, không xóa giỏ/key
    const draftReceipt: ReceiptData = {
      receiptNumber: `DRAFT-${Date.now().toString().slice(-6)}`,
      createdAt: new Date().toLocaleString('vi-VN'),
      tenantName: activeUser?.tenantName || tenantName || 'CỬA HÀNG OISMS',
      branchName: branchName || 'Chi nhánh mặc định',
      branchAddress: branchAddress?.trim() || undefined,
      branchPhone: branchPhone?.trim() || undefined,
      cashierName: activeUser?.fullName || cashierName || 'Thu ngân',
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

  const handleSafeClose = () => {
    if (isSubmitting) return; // Không đóng modal khi đang gửi request
    onClose();
  };

  const handleResetPending = () => {
    // POS-T01-F02: Không cho nút reset hoặc hộp confirm làm mất key/payload của giao dịch unknown!
    // Giao dịch unknown đang chờ đối soát hoặc thử lại, tuyệt đối không được xóa pending để sinh key mới.
    setApiNotice({
      type: 'warning',
      text: 'Giao dịch chưa xác định kết quả từ máy chủ. Để đảm bảo an toàn và không trừ tồn kho hai lần, hệ thống giữ nguyên mã yêu cầu và giỏ hàng. Vui lòng bấm "Thử lại thanh toán (Retry)" hoặc liên hệ quản trị viên để đối soát.',
    });
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-fade-in no-print">
      <div className="bg-white rounded-3xl shadow-2xl border border-slate-100 max-w-xl w-full overflow-hidden flex flex-col max-h-[90vh]" role="dialog" aria-label="Thanh Toán Đơn Hàng">
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
            disabled={isSubmitting}
            onClick={handleSafeClose}
            aria-label="Đóng thanh toán"
            className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition-colors disabled:opacity-50"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Modal Content */}
        <div className="p-6 overflow-y-auto space-y-6">
          {/* Unknown Pending Attempt Warning Banner */}
          {isRetry && currentPending && (
            <div className="p-3.5 bg-amber-50 border border-amber-300 rounded-2xl text-xs text-amber-900 space-y-1">
              <div className="font-bold flex items-center gap-1.5 text-amber-800">
                <RefreshCw className="w-4 h-4 text-amber-600 animate-spin" />
                <span>Giao dịch đang chờ xác nhận kết quả (Retry Mode)</span>
              </div>
              <p className="m-0 text-amber-700 leading-relaxed text-[11px]">
                Yêu cầu thanh toán trước đó chưa nhận được phản hồi xác nhận từ máy chủ. Hệ thống giữ nguyên mã yêu cầu{' '}
                <code className="font-mono font-bold bg-amber-100 px-1 py-0.5 rounded text-amber-950">{currentPending.requestKey}</code>{' '}
                và nội dung giỏ hàng để gửi lại an toàn, tuyệt đối không trừ tồn kho 2 lần.
              </p>
              <div className="pt-1 flex gap-2">
                <button
                  type="button"
                  disabled={isSubmitting}
                  onClick={handleResetPending}
                  className="text-[11px] text-amber-800 hover:text-amber-950 underline font-semibold"
                >
                  Yêu cầu đang chờ đối soát (giữ nguyên mã để gửi lại)
                </button>
              </div>
            </div>
          )}

          {/* Method Selection */}
          <div>
            <label className="text-xs font-bold text-slate-500 uppercase tracking-wider mb-2 block">
              Phương thức thanh toán
            </label>
            <div className="grid grid-cols-2 gap-3">
              <button
                type="button"
                disabled={isSubmitting || isRetry}
                onClick={() => setPaymentMethod('CASH')}
                className={`flex items-center justify-center gap-2.5 p-3.5 rounded-2xl border-2 font-bold text-sm transition-all disabled:opacity-70 ${
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
                disabled={isSubmitting || isRetry}
                onClick={() => setPaymentMethod('BANK_TRANSFER_QR')}
                className={`flex items-center justify-center gap-2.5 p-3.5 rounded-2xl border-2 font-bold text-sm transition-all disabled:opacity-70 ${
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
                    disabled={isSubmitting || isRetry}
                    value={amountGiven || ''}
                    onChange={(e) => setUserGivenAmount(parseFloat(e.target.value) || 0)}
                    placeholder="Nhập số tiền khách đưa..."
                    className="w-full text-xl font-bold text-slate-900 px-4 py-3 bg-white border border-slate-300 rounded-xl focus:outline-none focus:border-indigo-600 focus:ring-4 focus:ring-indigo-100 disabled:bg-slate-100 disabled:text-slate-500"
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
                    disabled={isSubmitting || isRetry}
                    onClick={() => setUserGivenAmount(opt.value)}
                    className="py-1.5 px-2 bg-white hover:bg-slate-100 text-slate-700 border border-slate-200 rounded-lg text-xs font-semibold transition-colors disabled:opacity-50"
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

          {/* QR CODE SECTION - S3-POS-B03: Thay thế Mã QR mô phỏng bằng VietQR thật chuẩn EMVCo/NAPAS 247 */}
          {paymentMethod === 'BANK_TRANSFER_QR' && (
            <div className="bg-slate-50 p-5 rounded-2xl border border-slate-200/60 flex flex-col items-center text-center">
              {isBankConfigInvalid || !qrResult || !bankConfig ? (
                <div className="w-full max-w-sm p-4 bg-rose-50 border border-rose-200 rounded-xl text-rose-900 text-left space-y-2">
                  <div className="font-bold flex items-center gap-1.5 text-xs text-rose-800">
                    <AlertCircle className="w-4 h-4 shrink-0 text-rose-600" />
                    <span>Không thể tạo mã VietQR</span>
                  </div>
                  <p className="text-xs leading-relaxed text-rose-700 m-0">
                    {qrError || 'Chưa cấu hình thông tin ngân hàng thụ hưởng cho POS chi nhánh này. Vui lòng liên hệ quản trị viên hoặc chọn phương thức Tiền mặt.'}
                  </p>
                  <p className="text-[11px] text-rose-500 font-medium italic m-0 pt-1">
                    {isRetry ? 'Giao dịch cũ chưa có snapshot người nhận hợp lệ. Chỉ thử lại yêu cầu gốc để đối soát; không quét/chuyển khoản thêm.' : 'Nút thanh toán QR bị khóa cho tới khi có cấu hình hợp lệ. Bạn vẫn có thể chọn Tiền mặt.'}
                  </p>
                </div>
              ) : (
                <>
                  {bankConfig.isTestFixture && <p className="text-xs font-bold text-amber-800">DỮ LIỆU KIỂM THỬ — không chuyển tiền thật</p>}
                  <div className="bg-white p-3 rounded-2xl shadow-md border border-slate-200 mb-3 relative flex items-center justify-center min-w-[200px] min-h-[200px]">
                    <img
                      src={qrResult.quickLinkUrl}
                      alt="VietQR Code"
                      className="w-48 h-48 object-contain rounded-xl"
                      onError={(e) => {
                        (e.currentTarget as HTMLElement).style.display = 'none';
                        const fallback = document.getElementById('qr-offline-fallback');
                        if (fallback) fallback.style.display = 'flex';
                      }}
                    />
                    <div
                      id="qr-offline-fallback"
                      style={{ display: 'none' }}
                      className="w-48 h-48 flex-col items-center justify-center p-3 text-center bg-slate-50 rounded-xl border border-dashed border-slate-300 text-xs text-slate-500"
                    >
                      <QrCode className="w-8 h-8 text-slate-400 mb-2" />
                      <span className="font-semibold text-slate-700">Mã QR ngoại tuyến</span>
                      <span className="text-[10px] text-slate-500 mt-1 break-all line-clamp-3 font-mono">
                        {qrResult.rawPayload.slice(0, 40)}...
                      </span>
                    </div>
                  </div>

                  <div className="w-full max-w-sm space-y-2 text-xs text-slate-700 text-left bg-white p-3.5 rounded-xl border border-slate-200 shadow-sm">
                    <div className="flex justify-between items-center py-0.5 border-b border-slate-100">
                      <span className="text-slate-500">Ngân hàng:</span>
                      <span className="font-bold text-slate-800">{bankConfig.bankName} ({bankConfig.bankBin})</span>
                    </div>
                    <div className="flex justify-between items-center py-0.5 border-b border-slate-100">
                      <span className="text-slate-500">Số tài khoản:</span>
                      <span className="font-mono font-bold text-indigo-700 text-sm tracking-wide">{bankConfig.accountNumber}</span>
                    </div>
                    <div className="flex justify-between items-center py-0.5 border-b border-slate-100">
                      <span className="text-slate-500">Chủ tài khoản:</span>
                      <span className="font-semibold text-slate-800 uppercase">{bankConfig.accountName}</span>
                    </div>
                    <div className="flex justify-between items-center py-0.5 border-b border-slate-100">
                      <span className="text-slate-500">Số tiền:</span>
                      <span className="font-extrabold text-emerald-600 text-sm">{formatCurrency(qrIntent!.amount)}</span>
                    </div>
                    <div className="flex justify-between items-start py-0.5">
                      <span className="text-slate-500 shrink-0">Nội dung CK:</span>
                      <span className="font-mono font-bold text-indigo-900 bg-indigo-50 px-2 py-0.5 rounded text-xs break-all text-right">
                        {qrResult.transferContent}
                      </span>
                    </div>
                    <div className="pt-1 text-[10px] text-slate-400 font-mono flex justify-between">
                      <span>UUID:</span>
                      <span className="truncate max-w-[200px]" title={activeRequestKey}>{activeRequestKey}</span>
                    </div>
                  </div>

                  <div className="mt-3 p-3 bg-amber-50 border border-amber-200 rounded-xl text-amber-900 text-left max-w-sm">
                    <div className="font-bold flex items-center gap-1.5 text-xs text-amber-800 mb-1">
                      <AlertCircle className="w-4 h-4 shrink-0 text-amber-600" />
                      <span>Xác nhận thủ công của thu ngân</span>
                    </div>
                    <p className="text-[11px] leading-relaxed text-amber-700 m-0">
                      Hệ thống chưa tự động đối soát ngân hàng qua Webhook. Sau khi khách hàng quét mã và chuyển khoản thành công, <strong>thu ngân phải kiểm tra biến động số dư trên App ngân hàng hoặc tin nhắn SMS</strong> trước khi nhấn "Gửi Checkout đơn hàng (F4)".
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
                disabled={isSubmitting || isRetry}
                value={customerName}
                onChange={(e) => setCustomerName(e.target.value)}
                placeholder="Khách lẻ..."
                className="w-full text-xs px-3 py-2 border border-slate-300 rounded-lg focus:outline-none focus:border-indigo-600 disabled:bg-slate-100 disabled:text-slate-500"
              />
            </div>
            <div>
              <label className="text-xs font-semibold text-slate-600 mb-1 block">
                Số điện thoại (tùy chọn)
              </label>
              <input
                type="text"
                disabled={isSubmitting || isRetry}
                value={customerPhone}
                onChange={(e) => setCustomerPhone(e.target.value)}
                placeholder="09..."
                className="w-full text-xs px-3 py-2 border border-slate-300 rounded-lg focus:outline-none focus:border-indigo-600 disabled:bg-slate-100 disabled:text-slate-500"
              />
            </div>
          </div>

          {/* API Notice / DoD Alert */}
          {activeNotice && (
            <div
              className={`p-4 rounded-xl text-xs space-y-2 border ${
                activeNotice.type === 'warning'
                  ? 'bg-amber-50 text-amber-900 border-amber-200'
                  : 'bg-rose-50 text-rose-900 border-rose-200'
              }`}
            >
              <div className="flex items-start gap-2">
                <AlertCircle className="w-4 h-4 text-amber-600 shrink-0 mt-0.5" />
                <div className="font-semibold">{activeNotice.text}</div>
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
              onClick={handleSafeClose}
              className="px-4 py-2 text-xs font-semibold text-slate-600 hover:bg-slate-200 rounded-xl transition-colors disabled:opacity-50"
            >
              Đóng lại (Esc)
            </button>
            <button
              type="button"
              disabled={isSubmitting}
              onClick={() => handlePrintPreview()}
              className="px-3.5 py-2 text-xs font-semibold text-amber-800 bg-amber-50 hover:bg-amber-100 border border-amber-200 rounded-xl transition-colors flex items-center gap-1.5 disabled:opacity-50"
              title="In phiếu tạm tính (chưa thanh toán)"
            >
              <Printer className="w-3.5 h-3.5 text-amber-600" />
              <span>In phiếu tạm tính</span>
            </button>
          </div>

          <button
            type="button"
            disabled={isSubmitting || isShortOfCash || isBankConfigInvalid}
            onClick={() => handleCheckout(false)}
            className={`inline-flex items-center gap-2 px-6 py-2.5 rounded-xl font-bold text-sm shadow-md transition-all ${
              isSubmitting || isShortOfCash || isBankConfigInvalid
                ? 'bg-slate-300 text-slate-500 cursor-not-allowed shadow-none'
                : 'bg-indigo-600 hover:bg-indigo-700 text-white shadow-indigo-600/30'
            }`}
          >
            {isSubmitting ? (
              <span>Đang gửi Checkout API...</span>
            ) : isRetry ? (
              <>
                <RefreshCw className="w-4 h-4" />
                <span>Thử lại thanh toán (F4)</span>
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
