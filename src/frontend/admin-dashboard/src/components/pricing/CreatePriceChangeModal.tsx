import React, { useEffect, useRef, useState } from 'react';
import { AlertCircle, CheckCircle2 } from 'lucide-react';
import { Button, Input, Modal } from '../ui';
import { NumberField } from '../NumberField';
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

type PriceListCode = 'retail' | 'wholesale';

const PRICE_LISTS: Array<{ code: PriceListCode; label: string }> = [
  { code: 'retail', label: 'Giá bán lẻ' },
  { code: 'wholesale', label: 'Giá bán sỉ' },
];

const priceKey = (unitId: string, list: PriceListCode) => `${unitId}:${list}`;
const formatPrice = (value: number) => `${value.toLocaleString('vi-VN')} ₫`;

export const CreatePriceChangeModal: React.FC<CreatePriceChangeModalProps> = ({
  isOpen,
  onClose,
  sku,
  onCreated,
}) => {
  const { account } = useAuth();
  const isOwner = account?.role === 'Owner';

  const [units, setUnits] = useState<SkuUnit[]>([]);
  const [effectivePrices, setEffectivePrices] = useState<EffectivePrice[]>([]);
  const [loadingUnits, setLoadingUnits] = useState(false);
  const [unitsError, setUnitsError] = useState<string | null>(null);

  // Giá mới theo từng đơn vị và bảng giá; bỏ trống nghĩa là giữ giá hiện tại
  const [newPrices, setNewPrices] = useState<Record<string, number | null>>({});
  const [reason, setReason] = useState('');
  const [applyImmediately, setApplyImmediately] = useState(true);
  const [scheduledDateTime, setScheduledDateTime] = useState('');

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  // Giữ nguyên key khi thử lại cùng nội dung; đổi nội dung thì là yêu cầu mới
  const intentRef = useRef<{ signature: string; key: string } | null>(null);

  useEffect(() => {
    if (!isOpen || !sku) return;
    setError(null);
    setSuccess(null);
    setNewPrices({});
    setReason('');
    setApplyImmediately(true);
    setScheduledDateTime('');
    intentRef.current = null;

    let active = true;
    const load = async () => {
      setLoadingUnits(true);
      setUnitsError(null);
      setUnits([]);
      setEffectivePrices([]);
      try {
        const [skuUnits, retailPrices, wholesalePrices] = await Promise.all([
          unitService.getSkuUnits(sku.productId, sku.id),
          pricingService.getEffectivePrice(sku.id, 'retail'),
          pricingService.getEffectivePrice(sku.id, 'wholesale'),
        ]);
        const prices = [...retailPrices, ...wholesalePrices];
        if (!active) return;
        const sellable = skuUnits
          .filter((u) => u.isActive !== false && u.canSell !== false)
          .sort((a, b) => Number(b.isBase) - Number(a.isBase) || a.factorToBase - b.factorToBase);
        setUnits(sellable);
        setEffectivePrices(prices);
      } catch (err: any) {
        if (!active) return;
        setUnitsError(err?.response?.data?.detail || 'Không thể tải đơn vị và giá hiện hành của SKU.');
      } finally {
        if (active) setLoadingUnits(false);
      }
    };
    void load();
    return () => {
      active = false;
    };
  }, [isOpen, sku?.id]);

  if (!isOpen || !sku) return null;

  const currentPrice = (unitId: string, list: PriceListCode) =>
    effectivePrices.find((p) => p.unitId === unitId && p.priceListCode === list)?.price ?? null;

  // Gợi ý cho đơn vị quy đổi chưa có giá: chỉ điền khi người dùng bấm, server vẫn là nơi quyết định giá
  const suggestedPrice = (unit: SkuUnit, list: PriceListCode) => {
    if (unit.isBase || currentPrice(unit.unitId, list) != null) return null;
    const base = units.find((u) => u.isBase);
    const basePrice = base ? newPrices[priceKey(base.unitId, list)] ?? currentPrice(base.unitId, list) : null;
    return basePrice != null ? Math.round(basePrice * unit.factorToBase * 100) / 100 : null;
  };

  const changes = units.flatMap((unit) =>
    PRICE_LISTS.flatMap(({ code }) => {
      const next = newPrices[priceKey(unit.unitId, code)];
      if (next == null || next === currentPrice(unit.unitId, code)) return [];
      return [{ priceListCode: code, skuId: sku.id, unitId: unit.unitId, newPrice: next }];
    })
  );

  const handleSubmit = async () => {
    setError(null);
    setSuccess(null);

    if (changes.length === 0) {
      setError('Vui lòng nhập ít nhất một giá mới khác giá hiện tại.');
      return;
    }

    const trimmedReason = reason.trim();
    if (!trimmedReason) {
      setError('Vui lòng nhập lý do thay đổi giá.');
      return;
    }

    let validFromIso: string | null = null;
    if (!applyImmediately) {
      const parsedDate = new Date(scheduledDateTime);
      if (!scheduledDateTime || isNaN(parsedDate.getTime()) || parsedDate.getTime() <= Date.now()) {
        setError('Vui lòng chọn thời điểm hiệu lực trong tương lai.');
        return;
      }
      validFromIso = parsedDate.toISOString();
    }

    const payload = { reason: trimmedReason, requestedValidFrom: validFromIso, items: changes };
    const signature = JSON.stringify(payload);
    if (intentRef.current?.signature !== signature) {
      intentRef.current = { signature, key: generateUuid() };
    }

    setLoading(true);
    try {
      const res = await pricingService.createPriceChangeRequest(payload, intentRef.current.key);
      setSuccess(
        res.status === 'Approved'
          ? `Đã cập nhật ${changes.length} giá.`
          : `Đã tạo phiếu đổi ${changes.length} giá, đang chờ Chủ cửa hàng duyệt.`
      );
      onCreated();
      setTimeout(onClose, 1200);
    } catch (err: any) {
      const data = err?.response?.data;
      setError(data?.detail || data?.message || 'Không thể tạo phiếu đổi giá. Vui lòng thử lại.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <Modal wide title={`Đổi giá bán: ${sku.name}${sku.variant ? ` · ${sku.variant}` : ''}`} onClose={onClose}>
      <div className="price-editor">
        <p className="price-editor__hint">
          Mã <strong>{sku.code}</strong>. Nhập giá mới ở ô cần đổi, ô để trống sẽ giữ nguyên giá hiện tại.
        </p>

        {unitsError && (
          <div className="price-editor__alert price-editor__alert--error" role="alert">
            <AlertCircle size={16} />
            <span>{unitsError}</span>
          </div>
        )}

        {loadingUnits ? (
          <p className="price-editor__hint">Đang tải giá hiện hành...</p>
        ) : (
          !unitsError && (
            <div className="table-wrap">
              <table className="price-editor__table">
                <thead>
                  <tr>
                    <th>Đơn vị bán</th>
                    {PRICE_LISTS.map((list) => (
                      <th key={list.code}>{list.label}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {units.map((unit) => (
                    <tr key={unit.unitId}>
                      <td>
                        <strong>{unit.unitName}</strong>
                        <small>{unit.isBase ? 'Đơn vị gốc' : `Bằng ${unit.factorToBase} đơn vị gốc`}</small>
                      </td>
                      {PRICE_LISTS.map(({ code, label }) => {
                        const key = priceKey(unit.unitId, code);
                        const current = currentPrice(unit.unitId, code);
                        const suggestion = suggestedPrice(unit, code);
                        return (
                          <td key={code}>
                            <NumberField
                              aria-label={`${label} theo ${unit.unitName}`}
                              value={newPrices[key] ?? null}
                              onChange={(value) => setNewPrices((prev) => ({ ...prev, [key]: value }))}
                              decimals={2}
                              suffix="₫"
                              placeholder={current != null ? current.toLocaleString('vi-VN') : 'Nhập giá'}
                              disabled={loading}
                            />
                            <small>
                              {current != null ? `Hiện tại: ${formatPrice(current)}` : 'Chưa có giá'}
                              {suggestion != null && newPrices[key] == null && (
                                <button
                                  type="button"
                                  className="price-editor__suggest"
                                  onClick={() => setNewPrices((prev) => ({ ...prev, [key]: suggestion }))}
                                >
                                  Dùng {formatPrice(suggestion)}
                                </button>
                              )}
                            </small>
                          </td>
                        );
                      })}
                    </tr>
                  ))}
                  {units.length === 0 && (
                    <tr>
                      <td colSpan={3}>SKU này chưa có đơn vị được phép bán.</td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          )
        )}

        <div className="price-editor__row">
          <label>
            <span>Lý do đổi giá *</span>
            <Input
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="Ví dụ: Điều chỉnh theo giá nhà cung cấp"
              maxLength={500}
              disabled={loading}
            />
          </label>
          <div>
            <span className="price-editor__label">Thời điểm áp dụng</span>
            <div className="price-editor__options">
              <label>
                <input type="radio" name="priceApplyTime" checked={applyImmediately} onChange={() => setApplyImmediately(true)} />
                {isOwner ? 'Áp dụng ngay' : 'Ngay khi được duyệt'}
              </label>
              <label>
                <input type="radio" name="priceApplyTime" checked={!applyImmediately} onChange={() => setApplyImmediately(false)} />
                Chọn ngày giờ
              </label>
            </div>
            {!applyImmediately && (
              <Input type="datetime-local" value={scheduledDateTime} onChange={(e) => setScheduledDateTime(e.target.value)} />
            )}
          </div>
        </div>

        {error && (
          <div className="price-editor__alert price-editor__alert--error" role="alert">
            <AlertCircle size={16} />
            <span>{error}</span>
          </div>
        )}
        {success && (
          <div className="price-editor__alert price-editor__alert--success" role="status">
            <CheckCircle2 size={16} />
            <span>{success}</span>
          </div>
        )}
        {!isOwner && !success && (
          <p className="price-editor__hint">Phiếu sẽ chờ Chủ cửa hàng duyệt. Trong lúc đó cửa hàng vẫn bán theo giá cũ.</p>
        )}
      </div>

      <div className="modal-actions">
        <Button variant="secondary" onClick={onClose} disabled={loading}>
          Hủy
        </Button>
        <Button onClick={handleSubmit} disabled={loading || loadingUnits || Boolean(unitsError) || Boolean(success)}>
          {loading
            ? 'Đang gửi...'
            : changes.length === 0
            ? isOwner ? 'Lưu giá mới' : 'Gửi duyệt'
            : isOwner
            ? `Lưu ${changes.length} giá mới`
            : `Gửi duyệt ${changes.length} giá`}
        </Button>
      </div>
    </Modal>
  );
};
