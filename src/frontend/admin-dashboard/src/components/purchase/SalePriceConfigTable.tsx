import React from 'react';
import { AlertCircle, AlertTriangle } from 'lucide-react';
import { Input, Select } from '../ui';
import { NumberField } from '../NumberField';

export interface SalePriceConfigRowItem {
  id?: string;
  purchaseOrderItemId?: string;
  skuId: string;
  skuCode: string;
  productName: string;
  unitId: string;
  unitName: string;
  priceListId: string;
  priceListCode: string;
  priceListName: string;
  mode: 'KEEP' | 'WAC_MARGIN' | 'FIXED';
  marginPercent?: number | null;
  fixedPrice?: number | null;
  priceSource?: 'SUPPLIER' | 'OWNER' | null;
  applyMode: 'ON_RECEIPT' | 'FROM_DATE';
  applyFrom?: string | null;
  appliedAt?: string | null;
  currentPrice: number;
  currentUnitWac: number;
  currentMarginPercent: number;
  projectedUnitWac: number;
  projectedPrice: number;
  isBelowCost: boolean;
}

interface SalePriceConfigTableProps {
  configs: SalePriceConfigRowItem[];
  onChange?: (configs: SalePriceConfigRowItem[]) => void;
  readOnly?: boolean;
}

const formatMoney = (v?: number | null) =>
  v != null ? `${Math.round(v).toLocaleString('vi-VN')} ₫` : '—';

export const SalePriceConfigTable: React.FC<SalePriceConfigTableProps> = ({
  configs,
  onChange,
  readOnly = false,
}) => {
  if (!configs || configs.length === 0) {
    return (
      <div style={{ padding: 12, color: 'var(--text-muted, #64748b)', fontSize: 13 }}>
        Không có cấu hình giá bán nào.
      </div>
    );
  }

  const handleModeChange = (index: number, newMode: 'KEEP' | 'WAC_MARGIN' | 'FIXED') => {
    if (readOnly || !onChange) return;
    const updated = [...configs];
    const item = { ...updated[index], mode: newMode };

    if (newMode === 'KEEP') {
      item.marginPercent = null;
      item.fixedPrice = null;
      item.priceSource = null;
      item.projectedPrice = item.currentPrice;
    } else if (newMode === 'WAC_MARGIN') {
      item.fixedPrice = null;
      item.priceSource = null;
      item.marginPercent = item.marginPercent ?? (item.currentMarginPercent > 0 ? item.currentMarginPercent : 20);
      const m = item.marginPercent;
      const raw = (1 - m / 100) > 0 ? item.projectedUnitWac / (1 - m / 100) : item.projectedUnitWac;
      item.projectedPrice = Math.ceil(raw / 1000) * 1000;
    } else if (newMode === 'FIXED') {
      item.marginPercent = null;
      item.priceSource = item.priceSource || 'OWNER';
      item.fixedPrice = item.fixedPrice ?? (item.currentPrice > 0 ? item.currentPrice : Math.ceil(item.projectedUnitWac / 1000) * 1000);
      item.projectedPrice = item.fixedPrice;
    }

    item.isBelowCost = item.projectedPrice < item.projectedUnitWac;
    updated[index] = item;
    onChange(updated);
  };

  const handleMarginChange = (index: number, val: number | null) => {
    if (readOnly || !onChange) return;
    const updated = [...configs];
    const item = { ...updated[index], marginPercent: val };
    const m = val ?? 0;
    const raw = (1 - m / 100) > 0 ? item.projectedUnitWac / (1 - m / 100) : item.projectedUnitWac;
    item.projectedPrice = Math.ceil(raw / 1000) * 1000;
    item.isBelowCost = item.projectedPrice < item.projectedUnitWac;
    updated[index] = item;
    onChange(updated);
  };

  const handleFixedPriceChange = (index: number, val: number | null) => {
    if (readOnly || !onChange) return;
    const updated = [...configs];
    const item = { ...updated[index], fixedPrice: val, projectedPrice: val ?? 0 };
    item.isBelowCost = (val ?? 0) < item.projectedUnitWac;
    updated[index] = item;
    onChange(updated);
  };

  const handleSourceChange = (index: number, source: 'SUPPLIER' | 'OWNER') => {
    if (readOnly || !onChange) return;
    const updated = [...configs];
    updated[index] = { ...updated[index], priceSource: source };
    onChange(updated);
  };

  const handleApplyModeChange = (index: number, mode: 'ON_RECEIPT' | 'FROM_DATE') => {
    if (readOnly || !onChange) return;
    const updated = [...configs];
    const todayStr = new Date().toISOString().split('T')[0];
    updated[index] = {
      ...updated[index],
      applyMode: mode,
      applyFrom: mode === 'FROM_DATE' ? (updated[index].applyFrom || todayStr) : null,
    };
    onChange(updated);
  };

  const handleApplyFromChange = (index: number, dateStr: string) => {
    if (readOnly || !onChange) return;
    const updated = [...configs];
    updated[index] = { ...updated[index], applyFrom: dateStr };
    onChange(updated);
  };

  const hasLossPrice = configs.some((c) => c.isBelowCost);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      {hasLossPrice && (
        <div
          role="alert"
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            padding: '8px 12px',
            borderRadius: 6,
            background: '#fef2f2',
            border: '1px solid #f87171',
            color: '#b91c1c',
            fontSize: 13,
            fontWeight: 500,
          }}
        >
          <AlertTriangle size={16} />
          <span>Cảnh báo: Có mặt hàng có giá bán dự kiến thấp hơn giá vốn (bán lỗ). Bạn sẽ cần xác nhận trước khi duyệt đơn.</span>
        </div>
      )}

      <div className="table-wrap" style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', fontSize: 13, borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: '#f8fafc', borderBottom: '1px solid #e2e8f0', textAlign: 'left' }}>
              <th style={{ padding: 8 }}>Mặt hàng & Đơn vị bán</th>
              <th style={{ padding: 8 }}>Bảng giá</th>
              <th style={{ padding: 8 }}>Giá hiện tại / Giá vốn</th>
              <th style={{ padding: 8 }}>Phương thức đổi giá</th>
              <th style={{ padding: 8 }}>Thiết lập</th>
              <th style={{ padding: 8 }}>Giá dự kiến</th>
              <th style={{ padding: 8 }}>Thời điểm áp dụng</th>
            </tr>
          </thead>
          <tbody>
            {configs.map((c, idx) => (
              <tr
                key={`${c.skuId}-${c.unitId}-${c.priceListId}`}
                style={{
                  borderBottom: '1px solid #e2e8f0',
                  background: c.isBelowCost ? '#fff1f2' : undefined,
                }}
              >
                <td style={{ padding: 8 }}>
                  <strong>{c.skuCode}</strong>
                  <div style={{ color: 'var(--text-muted, #64748b)', fontSize: 12 }}>{c.productName}</div>
                  <div style={{ color: '#0284c7', fontSize: 12, fontWeight: 500 }}>
                    Đơn vị: {c.unitName}
                  </div>
                </td>
                <td style={{ padding: 8 }}>
                  <span style={{ fontWeight: 600 }}>{c.priceListName || c.priceListCode}</span>
                </td>
                <td style={{ padding: 8 }}>
                  <div>Bán: {formatMoney(c.currentPrice)}</div>
                  <div style={{ color: 'var(--text-muted, #64748b)', fontSize: 12 }}>
                    Vốn hiện tại: {formatMoney(c.currentUnitWac)}
                  </div>
                  <div style={{ color: '#059669', fontSize: 12 }}>
                    Lãi gộp: {c.currentMarginPercent.toFixed(1)}%
                  </div>
                </td>
                <td style={{ padding: 8, minWidth: 150 }}>
                  {readOnly ? (
                    <div>
                      {c.mode === 'KEEP' && 'Giữ giá cũ'}
                      {c.mode === 'WAC_MARGIN' && 'WAC + lãi'}
                      {c.mode === 'FIXED' && 'Giá mới'}
                    </div>
                  ) : (
                    <Select
                      value={c.mode}
                      onChange={(e) => handleModeChange(idx, e.target.value as any)}
                    >
                      <option value="KEEP">Giữ giá cũ</option>
                      <option value="WAC_MARGIN">WAC + lãi</option>
                      <option value="FIXED">Giá mới</option>
                    </Select>
                  )}
                </td>
                <td style={{ padding: 8, minWidth: 160 }}>
                  {c.mode === 'KEEP' && (
                    <span style={{ color: 'var(--text-muted, #64748b)', fontSize: 12 }}>
                      Không đổi giá bán
                    </span>
                  )}
                  {c.mode === 'WAC_MARGIN' && (
                    <div>
                      {readOnly ? (
                        <span>Lãi gộp: {c.marginPercent ?? 0}%</span>
                      ) : (
                        <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                          <NumberField
                            aria-label="Phần trăm lãi gộp"
                            value={c.marginPercent ?? null}
                            onChange={(val) => handleMarginChange(idx, val)}
                            decimals={2}
                            suffix="%"
                            placeholder="Nhập % lãi"
                          />
                        </div>
                      )}
                      <div style={{ color: 'var(--text-muted, #64748b)', fontSize: 11, marginTop: 2 }}>
                        Vốn dự kiến: {formatMoney(c.projectedUnitWac)}
                      </div>
                    </div>
                  )}
                  {c.mode === 'FIXED' && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                      {readOnly ? (
                        <div>
                          {formatMoney(c.fixedPrice)} ({c.priceSource === 'SUPPLIER' ? 'NCC đề xuất' : 'Tự đặt'})
                        </div>
                      ) : (
                        <>
                          <NumberField
                            aria-label="Giá mới cố định"
                            value={c.fixedPrice ?? null}
                            onChange={(val) => handleFixedPriceChange(idx, val)}
                            decimals={0}
                            suffix="₫"
                            placeholder="Nhập giá mới"
                          />
                          <Select
                            value={c.priceSource || 'OWNER'}
                            onChange={(e) => handleSourceChange(idx, e.target.value as any)}
                          >
                            <option value="OWNER">Owner tự đặt</option>
                            <option value="SUPPLIER">NCC đề xuất</option>
                          </Select>
                        </>
                      )}
                    </div>
                  )}
                </td>
                <td style={{ padding: 8 }}>
                  <div style={{ fontWeight: 700, fontSize: 14 }}>
                    {formatMoney(c.projectedPrice)}
                  </div>
                  {c.isBelowCost && (
                    <div
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 4,
                        color: '#dc2626',
                        fontSize: 11,
                        fontWeight: 600,
                        marginTop: 2,
                      }}
                    >
                      <AlertCircle size={12} />
                      <span>Bán lỗ!</span>
                    </div>
                  )}
                </td>
                <td style={{ padding: 8, minWidth: 150 }}>
                  {readOnly ? (
                    <div>
                      {c.applyMode === 'ON_RECEIPT'
                        ? 'Khi nhập kho'
                        : `Từ ngày: ${c.applyFrom ? new Date(c.applyFrom).toLocaleDateString('vi-VN') : '—'}`}
                    </div>
                  ) : (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                      <Select
                        value={c.applyMode}
                        onChange={(e) => handleApplyModeChange(idx, e.target.value as any)}
                      >
                        <option value="ON_RECEIPT">Ngay khi nhập kho</option>
                        <option value="FROM_DATE">Từ ngày chọn</option>
                      </Select>
                      {c.applyMode === 'FROM_DATE' && (
                        <Input
                          type="date"
                          value={c.applyFrom ? c.applyFrom.split('T')[0] : ''}
                          onChange={(e) => handleApplyFromChange(idx, e.target.value)}
                        />
                      )}
                    </div>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
};
