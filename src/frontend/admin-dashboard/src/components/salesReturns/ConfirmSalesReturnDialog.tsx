import React, { useState } from 'react';
import { AlertTriangle, CheckCircle2, X } from 'lucide-react';
import { Button } from '../ui';
import type { SalesReturn } from '../../types/salesReturns';

interface ConfirmSalesReturnDialogProps {
  isOpen: boolean;
  type: 'confirm' | 'cancel';
  salesReturn: SalesReturn | null;
  onClose: () => void;
  onConfirm: (refundMethod?: string | null) => Promise<void>;
  loading: boolean;
}

export const ConfirmSalesReturnDialog: React.FC<ConfirmSalesReturnDialogProps> = ({
  isOpen,
  type,
  salesReturn,
  onClose,
  onConfirm,
  loading,
}) => {
  if (!isOpen || !salesReturn) return null;

  const isMarketplace = ['Shopee', 'TikTok', 'Lazada'].includes(salesReturn.orderChannel);
  const [refundMethod, setRefundMethod] = useState<string>(
    salesReturn.refundMethod || (isMarketplace ? 'None' : 'Cash')
  );

  const totalQuantity = salesReturn.items?.reduce((sum, it) => sum + (Number(it.quantity) || 0), 0) || 0;
  const refundAmountStr = (salesReturn.totalRefundAmount || 0).toLocaleString('vi-VN');

  const handleAction = async () => {
    if (type === 'confirm') {
      await onConfirm(isMarketplace ? null : refundMethod);
    } else {
      await onConfirm();
    }
  };

  return (
    <div className="modal-backdrop" role="presentation">
      <div className="modal" role="dialog" aria-modal="true" style={{ maxWidth: 480 }}>
        <div className="modal-head">
          <div className="flex items-center gap-2">
            {type === 'confirm' ? (
              <CheckCircle2 className="w-5 h-5 text-emerald-600" />
            ) : (
              <AlertTriangle className="w-5 h-5 text-rose-600" />
            )}
            <h2 className="text-base font-bold text-gray-900">
              {type === 'confirm' ? 'Xác nhận nhập kho & hoàn tiền' : 'Xác nhận hủy phiếu trả hàng'}
            </h2>
          </div>
          <Button variant="ghost" aria-label="Đóng" onClick={onClose} disabled={loading}>
            <X size={20} />
          </Button>
        </div>

        <div className="p-4 space-y-4 text-sm text-gray-700">
          {type === 'confirm' ? (
            <>
              <p>
                Bạn đang thực hiện xác nhận phiếu trả hàng{' '}
                <strong className="text-gray-900">{salesReturn.number}</strong> (Đơn:{' '}
                <span className="font-medium text-indigo-600">{salesReturn.orderNumber}</span>).
              </p>

              <div className="p-3 bg-emerald-50 border border-emerald-200 rounded-lg space-y-1.5 text-xs text-emerald-900">
                <div>
                  • <strong>Nhập kho:</strong> {totalQuantity} sản phẩm vào kho{' '}
                  <strong>{salesReturn.warehouseName}</strong>.
                </div>
                <div>
                  • <strong>Hoàn tiền:</strong> {refundAmountStr} đ{' '}
                  {isMarketplace ? (
                    <em>(Đơn sàn TMĐT - tiền do sàn hoàn cho khách)</em>
                  ) : (
                    <span>(Phương thức hoàn tiền bên dưới)</span>
                  )}
                </div>
                <div className="text-emerald-700 font-semibold pt-1">
                  ⚠️ Lưu ý: Sau khi xác nhận, hệ thống sẽ ghi sổ kho IN, tính lại WAC và không thể hủy phiếu.
                </div>
              </div>

              {!isMarketplace && (
                <div>
                  <label className="block text-xs font-semibold text-gray-700 mb-1">
                    Phương thức hoàn tiền:
                  </label>
                  <select
                    value={refundMethod}
                    onChange={(e) => setRefundMethod(e.target.value)}
                    disabled={loading}
                    className="input w-full"
                  >
                    <option value="Cash">Tiền mặt (Cash)</option>
                    <option value="BankTransfer">Chuyển khoản ngân hàng (BankTransfer)</option>
                  </select>
                </div>
              )}
            </>
          ) : (
            <>
              <p>
                Bạn có chắc chắn muốn hủy phiếu trả hàng{' '}
                <strong className="text-gray-900">{salesReturn.number}</strong> không?
              </p>
              <div className="p-3 bg-rose-50 border border-rose-200 rounded-lg text-xs text-rose-800">
                ⚠️ Phiếu nháp sau khi hủy sẽ chuyển sang trạng thái <strong>Đã hủy</strong> và không thể khôi phục lại.
              </div>
            </>
          )}
        </div>

        <div className="p-4 border-t border-gray-100 flex justify-end gap-2 bg-gray-50/50 rounded-b-2xl">
          <Button variant="secondary" onClick={onClose} disabled={loading}>
            Đóng
          </Button>
          <Button
            variant={type === 'confirm' ? 'primary' : 'danger'}
            onClick={handleAction}
            disabled={loading}
          >
            {loading ? 'Đang xử lý...' : type === 'confirm' ? 'Xác nhận nhập kho' : 'Hủy phiếu'}
          </Button>
        </div>
      </div>
    </div>
  );
};
