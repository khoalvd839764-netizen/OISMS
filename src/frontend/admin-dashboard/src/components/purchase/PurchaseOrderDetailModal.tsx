import React, { useEffect, useState } from 'react';
import { AlertCircle, Check, X } from 'lucide-react';
import { Badge, Button, Input, Modal } from '../ui';
import { purchaseOrderService } from '../../services/purchaseOrderService';
import type { PurchaseOrderResponse, PurchaseOrderStatus } from '../../types/purchaseOrder';
import { ClosePurchaseOrderModal } from './ClosePurchaseOrderModal';
import { SalePriceConfigTable, type SalePriceConfigRowItem } from './SalePriceConfigTable';
import { PurchaseOrderSetupWizardModal } from './PurchaseOrderSetupWizardModal';

interface PurchaseOrderDetailModalProps {
  po: PurchaseOrderResponse;
  userRole?: string;
  currentUserId?: string;
  onClose: () => void;
  onRefresh: () => void;
  onEdit: (po: PurchaseOrderResponse) => void;
  onCreateReceipt: (po: PurchaseOrderResponse) => void;
}

export const PurchaseOrderDetailModal: React.FC<PurchaseOrderDetailModalProps> = ({
  po,
  userRole,
  currentUserId,
  onClose,
  onRefresh,
  onEdit,
  onCreateReceipt,
}) => {
  const isOwner = userRole === 'Owner';
  const isCreator = !currentUserId || po.createdBy === currentUserId;
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showCloseModal, setShowCloseModal] = useState(false);
  const [showReturnBox, setShowReturnBox] = useState(false);
  const [returnReason, setReturnReason] = useState('');

  // Setup wizard state & session flag (Q28 / AC-04 / AC-05)
  const [showSetupWizard, setShowSetupWizard] = useState(false);
  const [hasViewedSalePriceConfig, setHasViewedSalePriceConfig] = useState(false);

  // Cấu hình giá bán (chỉ xem; sửa trong Thiết lập đơn mua)
  const [salePriceConfigs, setSalePriceConfigs] = useState<SalePriceConfigRowItem[]>(() => {
    return po.items.flatMap((it) =>
      (it.salePriceConfigs || []).map((c) => ({
        id: c.id,
        purchaseOrderItemId: it.id,
        skuId: it.skuId,
        skuCode: it.skuCode,
        productName: it.productName,
        unitId: c.unitId,
        unitName: c.unitName,
        priceListId: c.priceListId,
        priceListCode: c.priceListCode,
        priceListName: c.priceListName,
        mode: c.mode,
        marginPercent: c.marginPercent,
        fixedPrice: c.fixedPrice,
        priceSource: c.priceSource,
        applyMode: c.applyMode,
        applyFrom: c.applyFrom,
        appliedAt: c.appliedAt,
        currentPrice: c.currentPrice,
        currentUnitWac: c.currentUnitWac,
        currentMarginPercent: c.currentMarginPercent,
        projectedUnitWac: c.projectedUnitWac,
        projectedPrice: c.projectedPrice,
        isBelowCost: c.isBelowCost,
      }))
    );
  });

  useEffect(() => {
    setSalePriceConfigs(
      po.items.flatMap((it) =>
        (it.salePriceConfigs || []).map((c) => ({
          id: c.id,
          purchaseOrderItemId: it.id,
          skuId: it.skuId,
          skuCode: it.skuCode,
          productName: it.productName,
          unitId: c.unitId,
          unitName: c.unitName,
          priceListId: c.priceListId,
          priceListCode: c.priceListCode,
          priceListName: c.priceListName,
          mode: c.mode,
          marginPercent: c.marginPercent,
          fixedPrice: c.fixedPrice,
          priceSource: c.priceSource,
          applyMode: c.applyMode,
          applyFrom: c.applyFrom,
          appliedAt: c.appliedAt,
          currentPrice: c.currentPrice,
          currentUnitWac: c.currentUnitWac,
          currentMarginPercent: c.currentMarginPercent,
          projectedUnitWac: c.projectedUnitWac,
          projectedPrice: c.projectedPrice,
          isBelowCost: c.isBelowCost,
        }))
      )
    );
  }, [po]);

  const getStatusBadge = (status: PurchaseOrderStatus) => {
    switch (status) {
      case 'Draft':
        return <Badge tone="warning">Đề nghị mua (Draft)</Badge>;
      case 'PendingApproval':
        return <Badge tone="warning">Chờ duyệt giá (PendingApproval)</Badge>;
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

  const handleSubmitOrder = async () => {
    setLoading(true);
    setError(null);
    try {
      await purchaseOrderService.submitPurchaseOrder(po.id);
      onRefresh();
    } catch (err: any) {
      setError(
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        'Không thể gửi đơn đề nghị mua.'
      );
    } finally {
      setLoading(false);
    }
  };

  const handleReturnOrder = async () => {
    if (!returnReason.trim()) {
      setError('Bắt buộc nhập lý do trả về đơn mua cho nhân viên.');
      return;
    }
    setLoading(true);
    setError(null);
    try {
      await purchaseOrderService.returnPurchaseOrder(po.id, returnReason.trim());
      onRefresh();
    } catch (err: any) {
      setError(
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        'Không thể trả về đơn mua hàng.'
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

  // Tính các điều kiện kiểm tra checklist (AC-04)
  const todayStr = new Date().toISOString().split('T')[0];
  const hasSupplier = Boolean(po.supplierId);
  const hasAllPrices = po.items.length > 0 && !po.items.some((i) => i.unitPrice === null || i.unitPrice === undefined);
  const hasValidOrderDate = Boolean(po.orderDate && po.orderDate.split('T')[0] <= todayStr);

  return (
    <>
      <Modal wide title={`Chi tiết đơn mua hàng: ${po.number} (v${po.version})`} onClose={onClose}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          {/* Nhãn xem cho Owner khi đơn Draft do người khác tạo (AC-02) */}
          {po.status === 'Draft' && isOwner && !isCreator && (
            <div
              style={{
                padding: '10px 14px',
                borderRadius: 6,
                background: '#f1f5f9',
                border: '1px solid #cbd5e1',
                color: '#475569',
                fontSize: 13,
                fontWeight: 600,
              }}
            >
              Staff đang soạn — chỉ xem
            </div>
          )}

          {/* Checklist cho Owner ở trạng thái PendingApproval (AC-04) */}
          {isOwner && po.status === 'PendingApproval' && (
            <div
              style={{
                padding: 14,
                borderRadius: 8,
                background: '#f8fafc',
                border: '1px solid #e2e8f0',
                display: 'flex',
                flexDirection: 'column',
                gap: 10,
              }}
            >
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  flexWrap: 'wrap',
                  gap: 8,
                }}
              >
                <h4 style={{ margin: 0, fontSize: 13, fontWeight: 700, color: '#0f172a' }}>
                  Điều kiện duyệt đơn mua (Owner Checklist)
                </h4>
                <Button
                  variant="primary"
                  onClick={() => setShowSetupWizard(true)}
                  style={{ fontSize: 13, padding: '6px 14px' }}
                >
                  Thiết lập đơn mua
                </Button>
              </div>
              <div
                style={{
                  display: 'grid',
                  gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
                  gap: 8,
                  fontSize: 13,
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  {hasSupplier ? <Check size={16} color="#10b981" /> : <X size={16} color="#ef4444" />}
                  <span style={{ color: hasSupplier ? '#059669' : '#dc2626' }}>
                    {hasSupplier ? 'Đã chọn NCC' : 'Chưa chọn NCC'}
                  </span>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  {hasAllPrices ? <Check size={16} color="#10b981" /> : <X size={16} color="#ef4444" />}
                  <span style={{ color: hasAllPrices ? '#059669' : '#dc2626' }}>
                    {hasAllPrices ? 'Đã nhập đủ đơn giá mọi dòng' : 'Chưa đủ đơn giá mua'}
                  </span>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  {hasValidOrderDate ? <Check size={16} color="#10b981" /> : <X size={16} color="#ef4444" />}
                  <span style={{ color: hasValidOrderDate ? '#059669' : '#dc2626' }}>
                    {hasValidOrderDate ? 'Ngày đặt hợp lệ' : 'Ngày đặt chưa hợp lệ'}
                  </span>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  {hasViewedSalePriceConfig ? (
                    <Check size={16} color="#10b981" />
                  ) : (
                    <X size={16} color="#d97706" />
                  )}
                  <span style={{ color: hasViewedSalePriceConfig ? '#059669' : '#b45309' }}>
                    {hasViewedSalePriceConfig ? 'Đã xem cấu hình giá bán' : 'Chưa xem cấu hình giá bán'}
                  </span>
                </div>
              </div>
            </div>
          )}

          {po.returnReason && (
            <div
              style={{
                padding: 12,
                borderRadius: 6,
                background: '#fef3c7',
                border: '1px solid #f59e0b',
                color: '#92400e',
                fontSize: 13,
              }}
            >
              <strong>Lý do trả về gần nhất:</strong> {po.returnReason}
              {po.returnedByName && (
                <span>
                  {' '}
                  — Bởi {po.returnedByName} ({po.returnedAt ? new Date(po.returnedAt).toLocaleString('vi-VN') : ''})
                </span>
              )}
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
              <div>
                <strong>{po.supplierName || 'Chưa chọn / Ẩn với nhân viên'}</strong>
              </div>
            </div>
            <div>
              <span style={{ color: 'var(--text-muted, #64748b)' }}>Chi nhánh: </span>
              <div>
                <strong>{po.branchName}</strong>
              </div>
            </div>

            <div>
              <span style={{ color: 'var(--text-muted, #64748b)' }}>Ngày đặt hàng: </span>
              <div>
                <strong>
                  {po.orderDate
                    ? new Date(po.orderDate).toLocaleDateString('vi-VN')
                    : new Date(po.createdAt).toLocaleDateString('vi-VN')}
                </strong>
              </div>
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
                    {isOwner && <th>Đơn giá mua</th>}
                    {isOwner && <th>Giá vốn sau phân bổ</th>}
                    {isOwner && <th>Thành tiền</th>}
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
                      {isOwner && (
                        <td>
                          {it.unitPrice != null ? `${it.unitPrice.toLocaleString('vi-VN')} ₫` : 'Chưa nhập'}
                        </td>
                      )}
                      {isOwner && (
                        <td>
                          {it.allocatedUnitCost != null
                            ? `${it.allocatedUnitCost.toLocaleString('vi-VN')} ₫`
                            : '—'}
                        </td>
                      )}
                      {isOwner && (
                        <td>
                          <strong>
                            {it.lineTotal != null ? `${it.lineTotal.toLocaleString('vi-VN')} ₫` : '—'}
                          </strong>
                        </td>
                      )}
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
                alignItems: 'center',
                flexWrap: 'wrap',
                gap: 12,
                marginTop: 8,
                padding: '10px 14px',
                background: 'var(--surface-subtle, #f8fafc)',
                borderRadius: 6,
                fontSize: 13,
              }}
            >
              <div>
                Tiến độ nhận hàng: <strong>{totalReceivedBase}</strong> / {totalOrderedBase} (đơn vị gốc)
              </div>
              {isOwner && (
                <div style={{ display: 'flex', gap: 16, alignItems: 'center', flexWrap: 'wrap' }}>
                  <span>
                    Tiền hàng: <strong>{(po.subtotal ?? 0).toLocaleString('vi-VN')} ₫</strong>
                  </span>
                  <span>
                    Phí VC: <strong>+{(po.shippingFee ?? 0).toLocaleString('vi-VN')} ₫</strong>
                  </span>
                  <span>
                    Chiết khấu: <strong>−{(po.discountAmount ?? 0).toLocaleString('vi-VN')} ₫</strong>
                  </span>
                  <span>
                    Giá vốn phân bổ: <strong>{(po.totalAllocatedCost ?? 0).toLocaleString('vi-VN')} ₫</strong>
                  </span>
                  <span>
                    VAT: <strong>+{(po.vatAmount ?? 0).toLocaleString('vi-VN')} ₫</strong>
                  </span>
                  <span>
                    Tổng thanh toán:{' '}
                    <strong style={{ fontSize: 15, color: '#0284c7' }}>
                      {(po.totalAmount ?? 0).toLocaleString('vi-VN')} ₫
                    </strong>
                  </span>
                </div>
              )}
            </div>
          </div>

          {/* Cấu hình giá bán dự kiến khi nhập kho (Q15-Q18) */}
          {isOwner && salePriceConfigs.length > 0 && (
            <div
              style={{
                border: '1px solid #e2e8f0',
                borderRadius: 8,
                padding: 14,
                background: '#ffffff',
              }}
            >
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  marginBottom: 10,
                  flexWrap: 'wrap',
                  gap: 8,
                }}
              >
                <div>
                  <h4 style={{ margin: 0, fontSize: 14 }}>
                    Cấu hình giá bán tự động khi nhập kho (Q15 – Q18)
                  </h4>
                  <p style={{ margin: '4px 0 0', fontSize: 12, color: 'var(--text-muted, #64748b)' }}>
                    Cấu hình giá bán lẻ / bán sỉ cho từng đơn vị bán của mặt hàng. Khi xác nhận phiếu nhập kho đầu tiên, hệ thống sẽ tự động sinh giá bán.
                  </p>
                </div>
                {po.status === 'PendingApproval' && (
                  <span style={{ fontSize: 12, color: 'var(--text-muted, #64748b)' }}>
                    Sửa ở "Thiết lập đơn mua" → bước Giá bán
                  </span>
                )}
              </div>

              <SalePriceConfigTable configs={salePriceConfigs} readOnly />
            </div>
          )}

          {showReturnBox && isOwner && po.status === 'PendingApproval' && (
            <div
              style={{
                padding: 12,
                background: '#fffbeb',
                border: '1px solid #f59e0b',
                borderRadius: 8,
                display: 'flex',
                flexDirection: 'column',
                gap: 8,
              }}
            >
              <label style={{ fontWeight: 600, fontSize: 13 }}>
                Lý do trả về đơn mua về trạng thái Draft cho nhân viên sửa *
              </label>
              <Input
                value={returnReason}
                onChange={(e) => setReturnReason(e.target.value)}
                placeholder="Nhập lý do trả về (tối đa 500 ký tự)..."
                maxLength={500}
                disabled={loading}
              />
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
                <Button variant="secondary" onClick={() => setShowReturnBox(false)} disabled={loading}>
                  Đóng
                </Button>
                <Button variant="primary" onClick={handleReturnOrder} disabled={loading || !returnReason.trim()}>
                  {loading ? 'Đang trả về...' : 'Xác nhận trả về'}
                </Button>
              </div>
            </div>
          )}

          {/* Vị trí hiển thị lỗi: ngay trên thanh nút hành động (AC-06) */}
          {error && (
            <div
              role="alert"
              style={{
                padding: 12,
                borderRadius: 6,
                background: '#fee2e2',
                color: '#991b1b',
                fontSize: 14,
                display: 'flex',
                alignItems: 'center',
                gap: 8,
              }}
            >
              <AlertCircle size={16} />
              <span>{error}</span>
            </div>
          )}

          {/* Actions Footer */}
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, marginTop: 8, flexWrap: 'wrap', alignItems: 'center' }}>
            <Button variant="secondary" onClick={onClose} disabled={loading}>
              Đóng
            </Button>

            {/* Trạng thái Draft (AC-01 / AC-02) */}
            {po.status === 'Draft' && (
              <>
                {isCreator && (
                  <>
                    <Button variant="secondary" onClick={() => onEdit(po)} disabled={loading}>
                      Sửa đề nghị nháp
                    </Button>
                    <Button variant="danger" onClick={handleCancel} disabled={loading}>
                      Hủy đơn
                    </Button>
                    <Button variant="primary" onClick={handleSubmitOrder} disabled={loading}>
                      {loading ? 'Đang gửi...' : 'Gửi duyệt'}
                    </Button>
                  </>
                )}
                {!isCreator && isOwner && (
                  <Button variant="danger" onClick={handleCancel} disabled={loading}>
                    Hủy đơn
                  </Button>
                )}
              </>
            )}

            {/* Trạng thái PendingApproval: Owner (AC-04 / AC-05) */}
            {po.status === 'PendingApproval' && isOwner && (
              <>
                <Button variant="primary" onClick={() => setShowSetupWizard(true)} disabled={loading}>
                  Thiết lập đơn mua
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => setShowReturnBox((open) => !open)}
                  disabled={loading}
                >
                  Trả về Draft
                </Button>
                <Button variant="danger" onClick={handleCancel} disabled={loading}>
                  Hủy đơn
                </Button>

                <span style={{ fontSize: 12, color: 'var(--text-muted, #64748b)' }}>
                  Duyệt ở bước cuối "Xem lại &amp; duyệt" của Thiết lập đơn mua
                </span>
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

      {/* Trình hướng dẫn thiết lập đơn mua 3 bước (AC-05) */}
      {showSetupWizard && (
        <PurchaseOrderSetupWizardModal
          po={po}
          onClose={() => setShowSetupWizard(false)}
          onRefresh={() => {
            onRefresh();
          }}
          onMarkSalePriceConfigViewed={() => {
            setHasViewedSalePriceConfig(true);
          }}
          onSuccessApprove={() => {
            setShowSetupWizard(false);
            onRefresh();
            onClose();
          }}
        />
      )}

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
