import React, { useState } from 'react';
import { Badge, Button, Modal } from '../ui';
import { purchaseOrderService } from '../../services/purchaseOrderService';
import type { PurchaseOrderResponse, PurchaseOrderStatus } from '../../types/purchaseOrder';
import { ClosePurchaseOrderModal } from './ClosePurchaseOrderModal';

interface PurchaseOrderDetailModalProps {
  po: PurchaseOrderResponse;
  userRole?: string;
  onClose: () => void;
  onRefresh: () => void;
  onEdit: (po: PurchaseOrderResponse) => void;
  onCreateReceipt: (po: PurchaseOrderResponse) => void;
}

export const PurchaseOrderDetailModal: React.FC<PurchaseOrderDetailModalProps> = ({
  po,
  userRole,
  onClose,
  onRefresh,
  onEdit,
  onCreateReceipt,
}) => {
  const isOwner = userRole === 'Owner';
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showCloseModal, setShowCloseModal] = useState(false);

  const getStatusBadge = (status: PurchaseOrderStatus) => {
    switch (status) {
      case 'Draft':
        return <Badge tone="warning">Bản nháp (Draft)</Badge>;
      case 'Approved':
        return <Badge tone="info">Đã duyệt (Approved)</Badge>;
      case 'PartiallyReceived':
        return <Badge tone="warning">Đã nhận một phần</Badge>;
      case 'Received':
        return <Badge tone="success">Đã nhận đủ (Received)</Badge>;
      case 'Closed':
        return <Badge tone="neutral">Đã đóng (Closed)</Badge>;
      case 'Cancelled':
        return <Badge tone="danger">Đã hủy (Cancelled)</Badge>;
      default:
        return <Badge tone="neutral">{status}</Badge>;
    }
  };

  const handleApprove = async () => {
    const hasZeroPrice = po.items.some((i) => Number(i.unitPrice) === 0);
    let confirmZero = false;
    if (hasZeroPrice) {
      const ok = window.confirm(
        'Đơn mua này có mặt hàng có đơn giá bằng 0đ (hàng tặng/khuyến mãi). Bạn có chắc chắn muốn duyệt đơn với giá 0đ không?'
      );
      if (!ok) return;
      confirmZero = true;
    }

    setLoading(true);
    setError(null);
    try {
      await purchaseOrderService.approvePurchaseOrder(po.id, confirmZero);
      onRefresh();
    } catch (err: any) {
      setError(
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        'Không thể duyệt đơn mua hàng.'
      );
    } finally {
      setLoading(false);
    }
  };

  const handleCancel = async () => {
    const ok = window.confirm(
      `Bạn có chắc chắn muốn hủy đơn mua ${po.number} không? Thao tác này không thể hoàn tác.`
    );
    if (!ok) return;

    setLoading(true);
    setError(null);
    try {
      await purchaseOrderService.cancelPurchaseOrder(po.id);
      onRefresh();
    } catch (err: any) {
      setError(
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        'Không thể hủy đơn mua hàng.'
      );
    } finally {
      setLoading(false);
    }
  };

  const totalOrderedBase = po.items.reduce((s, i) => s + (Number(i.orderedBaseQuantity) || 0), 0);
  const totalReceivedBase = po.items.reduce((s, i) => s + (Number(i.receivedBaseQuantity) || 0), 0);

  return (
    <>
      <Modal title={`Chi tiết đơn mua hàng: ${po.number}`} onClose={onClose}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          {error && (
            <div
              role="alert"
              style={{
                padding: 12,
                borderRadius: 6,
                background: '#fee2e2',
                color: '#991b1b',
                fontSize: 14,
              }}
            >
              {error}
            </div>
          )}

          {/* Thông tin chung */}
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(3, 1fr)',
              gap: 12,
              padding: 12,
              background: 'var(--surface-subtle, #f8fafc)',
              borderRadius: 8,
              fontSize: 13,
            }}
          >
            <div>
              <span style={{ color: 'var(--text-muted, #64748b)' }}>Trạng thái: </span>
              <div>{getStatusBadge(po.status)}</div>
            </div>
            <div>
              <span style={{ color: 'var(--text-muted, #64748b)' }}>Nhà cung cấp: </span>
              <strong>{po.supplierName}</strong>
            </div>
            <div>
              <span style={{ color: 'var(--text-muted, #64748b)' }}>Chi nhánh: </span>
              <strong>{po.branchName}</strong>
            </div>

            <div>
              <span style={{ color: 'var(--text-muted, #64748b)' }}>Người tạo: </span>
              <div>{po.createdByName || 'N/A'} ({new Date(po.createdAt).toLocaleDateString('vi-VN')})</div>
            </div>

            {po.approvedByName && (
              <div>
                <span style={{ color: 'var(--text-muted, #64748b)' }}>Người duyệt: </span>
                <div style={{ color: '#059669', fontWeight: 600 }}>
                  {po.approvedByName} ({po.approvedAt ? new Date(po.approvedAt).toLocaleDateString('vi-VN') : ''})
                </div>
              </div>
            )}

            {po.closedByName && (
              <div style={{ gridColumn: 'span 2' }}>
                <span style={{ color: 'var(--text-muted, #64748b)' }}>Người đóng: </span>
                <span style={{ fontWeight: 600 }}>{po.closedByName}</span>
                {po.closeReason && <div style={{ color: '#d97706', fontSize: 12 }}>Lý do: {po.closeReason}</div>}
              </div>
            )}

            {po.cancelledByName && (
              <div style={{ gridColumn: 'span 2' }}>
                <span style={{ color: 'var(--text-muted, #64748b)' }}>Người hủy: </span>
                <span style={{ color: '#ef4444', fontWeight: 600 }}>{po.cancelledByName}</span>
              </div>
            )}

            {po.note && (
              <div style={{ gridColumn: 'span 3', borderTop: '1px solid #e2e8f0', paddingTop: 6 }}>
                <span style={{ color: 'var(--text-muted, #64748b)' }}>Ghi chú: </span>
                <span>{po.note}</span>
              </div>
            )}
          </div>

          {/* Bảng sản phẩm */}
          <div>
            <h4 style={{ margin: '0 0 8px 0', fontSize: 14 }}>Danh sách mặt hàng</h4>
            <div className="table-wrap">
              <table style={{ width: '100%', fontSize: 13 }}>
                <thead>
                  <tr>
                    <th>Mặt hàng</th>
                    <th>Đơn vị</th>
                    <th>Số lượng đặt</th>
                    <th>Đơn giá mua</th>
                    <th>Thành tiền</th>
                    <th>Đã nhận (gốc)</th>
                    <th>Còn lại (đơn vị mua)</th>
                  </tr>
                </thead>
                <tbody>
                  {po.items.map((it) => (
                    <tr key={it.id}>
                      <td>
                        <strong>{it.skuCode}</strong>
                        <div style={{ color: 'var(--text-muted, #64748b)', fontSize: 12 }}>
                          {it.productName}
                        </div>
                      </td>
                      <td>
                        {it.unitName}
                        {it.factorSnapshot > 1 && (
                          <div style={{ fontSize: 11, color: 'var(--text-muted, #64748b)' }}>
                            (Hệ số: {it.factorSnapshot})
                          </div>
                        )}
                      </td>
                      <td>{it.quantity}</td>
                      <td>{it.unitPrice.toLocaleString('vi-VN')} ₫</td>
                      <td><strong>{it.lineTotal.toLocaleString('vi-VN')} ₫</strong></td>
                      <td>
                        {it.receivedBaseQuantity} / {it.orderedBaseQuantity}
                      </td>
                      <td>
                        <strong
                          style={{
                            color: it.remainingQuantity > 0 ? '#0284c7' : '#059669',
                          }}
                        >
                          {it.remainingQuantity} {it.unitName}
                        </strong>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                marginTop: 8,
                padding: '8px 12px',
                background: 'var(--surface-subtle, #f8fafc)',
                borderRadius: 6,
                fontSize: 13,
              }}
            >
              <div>
                Tiến độ nhận hàng: <strong>{totalReceivedBase}</strong> / {totalOrderedBase} (đơn vị gốc)
              </div>
              <div>
                Tổng giá trị đơn mua: <strong style={{ fontSize: 15, color: '#0284c7' }}>
                  {po.totalAmount.toLocaleString('vi-VN')} ₫
                </strong>
              </div>
            </div>
          </div>

          {/* Actions */}
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 12, marginTop: 8 }}>
            <Button variant="secondary" onClick={onClose} disabled={loading}>
              Đóng
            </Button>

            {/* Các hành động theo trạng thái */}
            {po.status === 'Draft' && (
              <>
                <Button
                  variant="secondary"
                  onClick={() => onEdit(po)}
                  disabled={loading}
                >
                  Sửa bản nháp
                </Button>
                {isOwner && (
                  <>
                    <Button
                      variant="danger"
                      onClick={handleCancel}
                      disabled={loading}
                    >
                      Hủy đơn
                    </Button>
                    <Button
                      variant="primary"
                      onClick={handleApprove}
                      disabled={loading}
                    >
                      {loading ? 'Đang duyệt...' : 'Duyệt đơn mua'}
                    </Button>
                  </>
                )}
              </>
            )}

            {(po.status === 'Approved' || po.status === 'PartiallyReceived') && (
              <>
                {isOwner && (
                  <Button
                    variant="secondary"
                    onClick={() => setShowCloseModal(true)}
                    disabled={loading}
                  >
                    Đóng đơn mua
                  </Button>
                )}
                {isOwner && po.status === 'Approved' && (
                  <Button
                    variant="danger"
                    onClick={handleCancel}
                    disabled={loading}
                  >
                    Hủy đơn
                  </Button>
                )}
                <Button
                  variant="primary"
                  onClick={() => onCreateReceipt(po)}
                  disabled={loading}
                >
                  Lập phiếu nhập kho
                </Button>
              </>
            )}
          </div>
        </div>
      </Modal>

      {showCloseModal && (
        <ClosePurchaseOrderModal
          po={po}
          onClose={() => setShowCloseModal(false)}
          onSuccess={() => {
            setShowCloseModal(false);
            onRefresh();
          }}
        />
      )}
    </>
  );
};
