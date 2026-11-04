import React, { useState } from 'react';
import { Button, Input, Modal } from '../ui';
import { purchaseOrderService } from '../../services/purchaseOrderService';
import type { PurchaseOrderResponse } from '../../types/purchaseOrder';

interface ClosePurchaseOrderModalProps {
  po: PurchaseOrderResponse;
  onClose: () => void;
  onSuccess: (updated: PurchaseOrderResponse) => void;
}

export const ClosePurchaseOrderModal: React.FC<ClosePurchaseOrderModalProps> = ({
  po,
  onClose,
  onSuccess,
}) => {
  const [reason, setReason] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!reason.trim()) {
      setError('Vui lòng nhập lý do đóng đơn hàng.');
      return;
    }
    if (reason.trim().length > 500) {
      setError('Lý do không được vượt quá 500 ký tự.');
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const updated = await purchaseOrderService.closePurchaseOrder(po.id, reason.trim());
      onSuccess(updated);
    } catch (err: any) {
      setError(
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        'Không thể đóng đơn mua hàng. Vui lòng thử lại.'
      );
    } finally {
      setLoading(false);
    }
  };

  return (
    <Modal title={`Đóng đơn mua hàng: ${po.number}`} onClose={onClose}>
      <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <p style={{ margin: 0, color: 'var(--text-muted, #64748b)' }}>
          Đóng phần chưa nhận cho đơn mua từ <strong>{po.supplierName}</strong> tại chi nhánh <strong>{po.branchName}</strong>.
          Sau khi đóng, đơn mua sẽ chuyển sang trạng thái <strong>Đã đóng (Closed)</strong> và không thể lập thêm phiếu nhập.
        </p>

        {error && (
          <div role="alert" style={{ padding: 12, borderRadius: 6, background: '#fee2e2', color: '#991b1b' }}>
            {error}
          </div>
        )}

        <div>
          <label style={{ display: 'block', fontWeight: 600, marginBottom: 6 }}>
            Lý do đóng đơn <span style={{ color: '#ef4444' }}>*</span>
          </label>
          <Input
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Ví dụ: Nhà cung cấp hết hàng, chi nhánh thỏa thuận ngưng giao..."
            maxLength={500}
            required
            disabled={loading}
          />
          <small style={{ color: 'var(--text-muted, #64748b)' }}>
            {reason.length}/500 ký tự
          </small>
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 12, marginTop: 8 }}>
          <Button type="button" variant="secondary" onClick={onClose} disabled={loading}>
            Hủy bỏ
          </Button>
          <Button type="submit" variant="danger" disabled={loading || !reason.trim()}>
            {loading ? 'Đang đóng...' : 'Xác nhận đóng đơn'}
          </Button>
        </div>
      </form>
    </Modal>
  );
};
