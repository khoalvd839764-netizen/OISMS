import React, { useEffect, useState } from 'react';
import { AlertCircle, CheckCircle2 } from 'lucide-react';
import { Button, Input, Modal, Select } from '../ui';
import { pricingService } from '../../services/pricingService';
import { unitService } from '../../services/unitService';
import { generateUuid } from '../../services/apiAdapter';
import { useAuth } from '../../context/AuthContext';
import type { Sku } from '../../types/domain';
import type { SkuUnit } from '../../types/units';
import type { EffectivePrice } from '../../types/pricing';

interface CreatePriceChangeModalProps {
  isOpen: boolean;
  onClose: () => void;
  sku: Sku | null;
  onCreated: () => void;
}

export const CreatePriceChangeModal: React.FC<CreatePriceChangeModalProps> = ({
  isOpen,
  onClose,
  sku,
  onCreated,
}) => {
  const { account } = useAuth();
  const isOwner = account?.role === 'Owner';

  const [units, setUnits] = useState<SkuUnit[]>([]);
  const [loadingUnits, setLoadingUnits] = useState(false);
  const [unitsError, setUnitsError] = useState<string | null>(null);
  const [effectivePrices, setEffectivePrices] = useState<EffectivePrice[]>([]);

  const [selectedPriceList, setSelectedPriceList] = useState<'retail' | 'wholesale'>('retail');
  const [selectedUnitId, setSelectedUnitId] = useState<string>('');
  const [newPrice, setNewPrice] = useState<number>(0);
  const [reason, setReason] = useState<string>('');
  const [applyImmediately, setApplyImmediately] = useState<boolean>(true);
  const [scheduledDateTime, setScheduledDateTime] = useState<string>('');

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  // Client-Driven Idempotency Key: duy trì ổn định khi người dùng bấm Thử lại
  const [idempotencyKey, setIdempotencyKey] = useState<string>(() => generateUuid());

  useEffect(() => {
    if (!isOpen || !sku) return;
    setError(null);
    setSuccess(null);
    setIdempotencyKey(generateUuid());
    setReason('');
    setApplyImmediately(true);
    setScheduledDateTime('');

    // Tải đơn vị bán được và giá hiện hành của từng đơn vị
    let active = true;
    const loadUnits = async () => {
      setLoadingUnits(true);
      setUnitsError(null);
      setUnits([]);
      setEffectivePrices([]);
      setSelectedUnitId('');
      try {
        const [skuUnits, prices] = await Promise.all([
          unitService.getSkuUnits(sku.productId, sku.id),
          pricingService.getEffectivePrice(sku.id),
        ]);
        if (!active) return;
        const sellable = skuUnits.filter((u) => u.isActive !== false && u.canSell !== false);
        setUnits(sellable);
        setEffectivePrices(prices);
        setSelectedUnitId((sellable.find((u) => u.isBase) ?? sellable[0])?.unitId ?? '');
      } catch (err: any) {
        if (!active) return;
        setUnitsError(err?.response?.data?.detail || 'Không thể tải đơn vị và giá hiện hành của SKU.');
      } finally {
        if (active) setLoadingUnits(false);
      }
    };

    void loadUnits();
    return () => {
      active = false;
    };
  }, [isOpen, sku?.id]);

  const currentPrice =
    effectivePrices.find((p) => p.unitId === selectedUnitId && p.priceListCode === selectedPriceList)?.price ?? null;

  // Giá gợi ý luôn theo đúng đơn vị và bảng giá đang chọn
  useEffect(() => {
    setNewPrice(currentPrice ?? 0);
  }, [currentPrice, selectedUnitId, selectedPriceList]);

  if (!isOpen || !sku) return null;

  const handleSubmit = async () => {
    setError(null);
    setSuccess(null);

    if (!selectedUnitId) {
      setError('Vui lòng chọn đơn vị tính áp dụng giá.');
      return;
    }

    if (currentPrice == null && newPrice === 0) {
      setError('Đơn vị này chưa có giá. Vui lòng nhập đơn giá mới.');
      return;
    }

    if (newPrice < 0 || !Number.isFinite(newPrice)) {
      setError('Đơn giá mới phải là số hợp lệ và không được âm (≥ 0).');
      return;
    }

    const trimmedReason = reason.trim();
    if (!trimmedReason) {
      setError('Vui lòng nhập lý do thay đổi giá (tối đa 500 ký tự).');
      return;
    }
    if (trimmedReason.length > 500) {
      setError('Lý do thay đổi giá không được vượt quá 500 ký tự.');
      return;
    }

    let validFromIso: string | null = null;
    if (!applyImmediately) {
      if (!scheduledDateTime) {
        setError('Vui lòng chọn ngày và giờ bắt đầu có hiệu lực cho giá mới.');
        return;
      }
      const parsedDate = new Date(scheduledDateTime);
      if (isNaN(parsedDate.getTime()) || parsedDate.getTime() <= Date.now()) {
        setError('Thời điểm hiệu lực trong tương lai phải lớn hơn thời điểm hiện tại.');
        return;
      }
      validFromIso = parsedDate.toISOString();
    }

    setLoading(true);
    try {
      const res = await pricingService.createPriceChangeRequest(
        {
          reason: trimmedReason,
          requestedValidFrom: validFromIso,
          items: [
            {
              priceListCode: selectedPriceList,
              skuId: sku.id,
              unitId: selectedUnitId,
              newPrice: Number(newPrice),
            },
          ],
        },
        idempotencyKey
      );

      if (res.status === 'Approved') {
        setSuccess('Phiếu đổi giá đã được phê duyệt và kích hoạt thành công!');
      } else {
        setSuccess(
          'Phiếu đổi giá đã được tạo thành công và đang ở trạng thái Chờ duyệt (Pending) từ Chủ cửa hàng.'
        );
      }

      onCreated();
      setTimeout(() => {
        onClose();
      }, 1200);
    } catch (err: any) {
      const data = err?.response?.data;
      const msg = data?.detail || data?.message || err?.message || 'Lỗi khi tạo phiếu đổi giá.';
      setError(`Lỗi: ${msg}`);
      // Giữ nguyên idempotencyKey để nếu người dùng nhấn lại thì backend nhận diện idempotent retry
    } finally {
      setLoading(false);
    }
  };

  return (
    <Modal title={`Tạo phiếu đổi giá: ${sku.code}`} onClose={onClose}>
      <div style={{ padding: '0 4px' }}>
        {error && (
          <div
            style={{
              background: '#fef2f2',
              border: '1px solid #ef4444',
              color: '#b91c1c',
              padding: '12px 14px',
              borderRadius: '8px',
              fontSize: '12px',
              marginBottom: '14px',
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
            }}
          >
            <AlertCircle size={18} />
            <span>{error}</span>
          </div>
        )}

        {unitsError && (
          <div style={{ background: '#fef2f2', border: '1px solid #ef4444', color: '#b91c1c', padding: '12px 14px', borderRadius: '8px', fontSize: '12px', marginBottom: '14px' }}>
            {unitsError}
          </div>
        )}

        {success && (
          <div
            style={{
              background: '#dcfce7',
              border: '1px solid #22c55e',
              color: '#15803d',
              padding: '12px 14px',
              borderRadius: '8px',
              fontSize: '12px',
              marginBottom: '14px',
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
            }}
          >
            <CheckCircle2 size={18} />
            <span>{success}</span>
          </div>
        )}

        <div style={{ marginBottom: '14px' }}>
          <strong style={{ fontSize: '13px', display: 'block' }}>{sku.name}</strong>
          <span style={{ fontSize: '11px', color: '#64748b' }}>
            SKU: {sku.code} · WAC hiện tại:{' '}
            {sku.averageCost ? `${sku.averageCost.toLocaleString('vi-VN')} ₫` : '0 ₫'}
          </span>
        </div>

        <div className="form-grid compact">
          <label>
            <span>Bảng giá áp dụng *</span>
            <Select
              value={selectedPriceList}
              onChange={(e) => {
                setSelectedPriceList(e.target.value as 'retail' | 'wholesale');
              }}
            >
              <option value="retail">Bảng giá bán lẻ (Retail)</option>
              <option value="wholesale">Bảng giá bán sỉ (Wholesale)</option>
            </Select>
          </label>

          <label>
            <span>Đơn vị tính áp dụng *</span>
            <Select
              value={selectedUnitId}
              onChange={(e) => setSelectedUnitId(e.target.value)}
              disabled={loadingUnits}
            >
              {units.map((u) => (
                <option key={u.unitId} value={u.unitId}>
                  {u.unitName} ({u.unitSymbol}) {u.isBase ? '— [Đơn vị gốc]' : `(Hệ số ${u.factorToBase})`}
                </option>
              ))}
            </Select>
          </label>
        </div>

        <div className="form-grid compact" style={{ paddingTop: 0 }}>
          <label>
            <span>Đơn giá mới (VNĐ) *</span>
            <Input
              type="number"
              min="0"
              value={newPrice}
              onChange={(e) => setNewPrice(Number(e.target.value))}
              placeholder="Nhập giá mới..."
            />
            <small style={{ display: 'block', marginTop: '4px', color: '#64748b' }}>
              {loadingUnits
                ? 'Đang tải giá hiện hành...'
                : currentPrice != null
                ? `Giá hiện hành: ${currentPrice.toLocaleString('vi-VN')} ₫`
                : 'Đơn vị này chưa có giá'}
            </small>
          </label>

          <label>
            <span>Hiệu lực thời gian *</span>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '6px' }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '12px' }}>
                <input
                  type="radio"
                  name="applyTime"
                  checked={applyImmediately}
                  onChange={() => setApplyImmediately(true)}
                />
                <span>Áp dụng ngay khi duyệt</span>
              </label>
              <label style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '12px' }}>
                <input
                  type="radio"
                  name="applyTime"
                  checked={!applyImmediately}
                  onChange={() => setApplyImmediately(false)}
                />
                <span>Lên lịch ngày tương lai</span>
              </label>
            </div>
          </label>
        </div>

        {!applyImmediately && (
          <div style={{ padding: '0 20px 14px' }}>
            <label>
              <span style={{ fontSize: '11px', fontWeight: 600, display: 'block', marginBottom: '4px' }}>
                Chọn thời điểm bắt đầu có hiệu lực *
              </span>
              <Input
                type="datetime-local"
                value={scheduledDateTime}
                onChange={(e) => setScheduledDateTime(e.target.value)}
              />
            </label>
          </div>
        )}

        <div style={{ padding: '0 20px 14px' }}>
          <label>
            <span style={{ fontSize: '11px', fontWeight: 600, display: 'block', marginBottom: '4px' }}>
              Lý do thay đổi giá (bắt buộc) *
            </span>
            <Input
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="Ví dụ: Điều chỉnh theo giá thị trường, chương trình mùa vụ..."
              maxLength={500}
            />
          </label>
        </div>

        <div className="hint-box" style={{ margin: '0 20px 14px' }}>
          {isOwner ? (
            <span>
              <strong>Quyền hạn Chủ shop (Owner):</strong> Phiếu đổi giá sẽ được phê duyệt tự động và có hiệu lực ngay (hoặc đúng mốc thời gian đã lên lịch).
            </span>
          ) : (
            <span>
              <strong>Quyền hạn Nhân viên (Staff):</strong> Phiếu đổi giá sẽ chuyển sang trạng thái{' '}
              <strong>Chờ duyệt (Pending)</strong> để Chủ shop phê duyệt trước khi có hiệu lực. Trong thời gian này cửa hàng vẫn bán theo giá cũ.
            </span>
          )}
        </div>

        <div className="modal-actions">
          <Button variant="secondary" onClick={onClose} disabled={loading}>
            Hủy
          </Button>
          <Button onClick={handleSubmit} disabled={loading || loadingUnits || Boolean(unitsError)}>
            {loading ? 'Đang gửi...' : isOwner ? 'Xác nhận & Cập nhật giá' : 'Tạo phiếu chờ duyệt'}
          </Button>
        </div>
      </div>
    </Modal>
  );
};
