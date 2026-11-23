import React, { useEffect, useState } from 'react';
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
  onValidationChange?: (hasErrors: boolean) => void;
  readOnly?: boolean;
}

const formatMoney = (v?: number | null) =>
  v != null && v > 0 ? `${Math.round(v).toLocaleString('vi-VN')} ₫` : '—';

export const SalePriceConfigTable: React.FC<SalePriceConfigTableProps> = ({
  configs,
  onChange,
  onValidationChange,
  readOnly = false,
}) => {
  // Chế độ nhập theo dòng cho WAC_MARGIN: 'MARGIN' (Lãi gộp trên giá bán) | 'MARKUP' (Cộng thêm trên giá vốn)
  const [inputModes, setInputModes] = useState<Record<number, 'MARGIN' | 'MARKUP'>>({});
  // Giá trị tạm khi nhập theo MARKUP
  const [markupValues, setMarkupValues] = useState<Record<number, number | null>>({});
  // Lỗi validation theo dòng
  const [rowErrors, setRowErrors] = useState<Record<number, string | null>>({});

  useEffect(() => {
    const hasAnyError = Object.values(rowErrors).some((e) => Boolean(e));
    onValidationChange?.(hasAnyError);
  }, [rowErrors, onValidationChange]);

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

    // Clear error on mode switch
    const newErrors = { ...rowErrors };
    delete newErrors[index];
    setRowErrors(newErrors);

    if (newMode === 'KEEP') {
      item.marginPercent = null;
      item.fixedPrice = null;
      item.priceSource = null;
      item.projectedPrice = item.currentPrice;
    } else if (newMode === 'WAC_MARGIN') {
      item.fixedPrice = null;
      item.priceSource = null;
      const initialMargin = item.marginPercent ?? (item.currentMarginPercent > 0 && item.currentMarginPercent < 100 ? item.currentMarginPercent : 20);
      item.marginPercent = initialMargin;
      const raw = (1 - initialMargin / 100) > 0 ? item.projectedUnitWac / (1 - initialMargin / 100) : item.projectedUnitWac;
      item.projectedPrice = Math.ceil(raw / 1000) * 1000;

      // Sync initial markup
      if (initialMargin >= 0 && initialMargin < 100) {
        const k = Number(((initialMargin / (100 - initialMargin)) * 100).toFixed(2));
        setMarkupValues((prev) => ({ ...prev, [index]: k }));
      }
    } else if (newMode === 'FIXED') {
      item.marginPercent = null;
      item.priceSource = item.priceSource || 'OWNER';
      item.fixedPrice = item.fixedPrice ?? (item.currentPrice > 0 ? item.currentPrice : Math.ceil(item.projectedUnitWac / 1000) * 1000);
      item.projectedPrice = item.fixedPrice;
    }

    item.isBelowCost = item.projectedPrice > 0 && item.projectedPrice < item.projectedUnitWac;
    updated[index] = item;
    onChange(updated);
  };

  const handleSwitchInputMode = (index: number, newMode: 'MARGIN' | 'MARKUP') => {
    setInputModes((prev) => ({ ...prev, [index]: newMode }));
    const currentMargin = configs[index]?.marginPercent;

    if (newMode === 'MARKUP') {
      if (currentMargin != null && currentMargin >= 0 && currentMargin < 100) {
        const k = Number(((currentMargin / (100 - currentMargin)) * 100).toFixed(2));
        setMarkupValues((prev) => ({ ...prev, [index]: k }));
      }
    }

    // Re-validate based on new mode
    if (newMode === 'MARGIN') {
      if (currentMargin == null) {
        setRowErrors((prev) => ({ ...prev, [index]: 'Bắt buộc nhập % lãi gộp' }));
      } else if (currentMargin < 0 || currentMargin >= 100) {
        setRowErrors((prev) => ({ ...prev, [index]: 'Lãi gộp tính trên giá bán, phải từ 0% đến dưới 100%' }));
      } else {
        setRowErrors((prev) => {
          const next = { ...prev };
          delete next[index];
          return next;
        });
      }
    } else {
      const currentMarkup = markupValues[index] ?? (currentMargin != null && currentMargin < 100 ? (currentMargin / (100 - currentMargin)) * 100 : null);
      if (currentMarkup == null) {
        setRowErrors((prev) => ({ ...prev, [index]: 'Bắt buộc nhập % cộng thêm' }));
      } else if (currentMarkup < 0) {
        setRowErrors((prev) => ({ ...prev, [index]: 'Phần trăm cộng thêm trên giá vốn phải từ 0% trở lên' }));
      } else {
        setRowErrors((prev) => {
          const next = { ...prev };
          delete next[index];
          return next;
        });
      }
    }
  };

  const handleMarginChange = (index: number, val: number | null) => {
    if (readOnly || !onChange) return;
    const updated = [...configs];
    const item = { ...updated[index], marginPercent: val };

    if (val === null) {
      setRowErrors((prev) => ({ ...prev, [index]: 'Bắt buộc nhập % lãi gộp' }));
      item.projectedPrice = 0;
      item.isBelowCost = false;
    } else if (val < 0 || val >= 100) {
      setRowErrors((prev) => ({ ...prev, [index]: 'Lãi gộp tính trên giá bán, phải từ 0% đến dưới 100%' }));
      item.projectedPrice = 0;
      item.isBelowCost = false;
    } else {
      setRowErrors((prev) => {
        const next = { ...prev };
        delete next[index];
        return next;
      });
      const raw = (1 - val / 100) > 0 ? item.projectedUnitWac / (1 - val / 100) : 0;
      item.projectedPrice = Math.ceil(raw / 1000) * 1000;
      item.isBelowCost = item.projectedPrice > 0 && item.projectedPrice < item.projectedUnitWac;

      // Update markup equivalent
      const k = Number(((val / (100 - val)) * 100).toFixed(2));
      setMarkupValues((prev) => ({ ...prev, [index]: k }));
    }

    updated[index] = item;
    onChange(updated);
  };

  const handleMarkupChange = (index: number, val: number | null) => {
    if (readOnly || !onChange) return;
    setMarkupValues((prev) => ({ ...prev, [index]: val }));
    const updated = [...configs];
    const item = { ...updated[index] };

    if (val === null) {
      setRowErrors((prev) => ({ ...prev, [index]: 'Bắt buộc nhập % cộng thêm' }));
      item.marginPercent = null;
      item.projectedPrice = 0;
      item.isBelowCost = false;
    } else if (val < 0) {
      setRowErrors((prev) => ({ ...prev, [index]: 'Phần trăm cộng thêm trên giá vốn phải từ 0% trở lên' }));
      item.marginPercent = null;
      item.projectedPrice = 0;
      item.isBelowCost = false;
    } else {
      setRowErrors((prev) => {
        const next = { ...prev };
        delete next[index];
        return next;
      });
      // Quy đổi sang lãi gộp m = k ÷ (100 + k) × 100 (làm tròn 4 chữ số)
      const m = Number(((val / (100 + val)) * 100).toFixed(4));
      item.marginPercent = m;
      const raw = (1 - m / 100) > 0 ? item.projectedUnitWac / (1 - m / 100) : 0;
      item.projectedPrice = Math.ceil(raw / 1000) * 1000;
      item.isBelowCost = item.projectedPrice > 0 && item.projectedPrice < item.projectedUnitWac;
    }

    updated[index] = item;
    onChange(updated);
  };

  const handleFixedPriceChange = (index: number, val: number | null) => {
    if (readOnly || !onChange) return;
    const updated = [...configs];
    const item = { ...updated[index], fixedPrice: val, projectedPrice: val ?? 0 };
    item.isBelowCost = (val ?? 0) > 0 && (val ?? 0) < item.projectedUnitWac;
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
              <th style={{ padding: 8, minWidth: 260 }}>Thiết lập</th>
              <th style={{ padding: 8 }}>Giá dự kiến</th>
              <th style={{ padding: 8 }}>Thời điểm áp dụng</th>
            </tr>
          </thead>
          <tbody>
            {configs.map((c, idx) => {
              const currentInputMode = inputModes[idx] || 'MARGIN';
              const error = rowErrors[idx];
              const m = c.marginPercent;
              const markupEquiv = m != null && m >= 0 && m < 100
                ? Number(((m / (100 - m)) * 100).toFixed(2))
                : null;
              const currentMarkupInput = markupValues[idx] ?? markupEquiv;

              return (
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
                  <td style={{ padding: 8, minWidth: 260 }}>
                    {c.mode === 'KEEP' && (
                      <span style={{ color: 'var(--text-muted, #64748b)', fontSize: 12 }}>
                        Không đổi giá bán
                      </span>
                    )}
                    {c.mode === 'WAC_MARGIN' && (
                      <div>
                        {readOnly ? (
                          <div>
                            <div>Lãi gộp: {c.marginPercent ?? 0}%</div>
                            {markupEquiv != null && (
                              <div style={{ color: 'var(--text-muted, #64748b)', fontSize: 11 }}>
                                (= cộng thêm {markupEquiv}% trên giá vốn)
                              </div>
                            )}
                          </div>
                        ) : (
                          <div>
                            {/* Công tắc chọn chế độ nhập AC-08 */}
                            <div
                              style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: 6,
                                fontSize: 11,
                                marginBottom: 6,
                                flexWrap: 'wrap',
                              }}
                            >
                              <span style={{ color: 'var(--text-muted, #64748b)' }}>Nhập theo:</span>
                              <label style={{ display: 'inline-flex', alignItems: 'center', gap: 3, cursor: 'pointer', fontWeight: currentInputMode === 'MARGIN' ? 600 : 400 }}>
                                <input
                                  type="radio"
                                  name={`input-mode-${idx}`}
                                  checked={currentInputMode === 'MARGIN'}
                                  onChange={() => handleSwitchInputMode(idx, 'MARGIN')}
                                />
                                Lãi gộp trên giá bán
                              </label>
                              <span style={{ color: '#cbd5e1' }}>|</span>
                              <label style={{ display: 'inline-flex', alignItems: 'center', gap: 3, cursor: 'pointer', fontWeight: currentInputMode === 'MARKUP' ? 600 : 400 }}>
                                <input
                                  type="radio"
                                  name={`input-mode-${idx}`}
                                  checked={currentInputMode === 'MARKUP'}
                                  onChange={() => handleSwitchInputMode(idx, 'MARKUP')}
                                />
                                Cộng thêm trên vốn
                              </label>
                            </div>

                            <div
                              style={{
                                border: error ? '1px solid #ef4444' : undefined,
                                borderRadius: 6,
                                background: error ? '#fef2f2' : undefined,
                              }}
                            >
                              {currentInputMode === 'MARGIN' ? (
                                <NumberField
                                  aria-label="Phần trăm lãi gộp"
                                  value={c.marginPercent ?? null}
                                  onChange={(val) => handleMarginChange(idx, val)}
                                  decimals={2}
                                  suffix="%"
                                  placeholder="Nhập % lãi gộp (0 đến <100)"
                                />
                              ) : (
                                <NumberField
                                  aria-label="Phần trăm cộng thêm trên giá vốn"
                                  value={currentMarkupInput}
                                  onChange={(val) => handleMarkupChange(idx, val)}
                                  decimals={2}
                                  suffix="%"
                                  placeholder="Nhập % cộng thêm (≥ 0, ví dụ 100)"
                                />
                              )}
                            </div>

                            {/* Dòng lỗi viền đỏ nếu có */}
                            {error ? (
                              <div style={{ color: '#dc2626', fontSize: 11, marginTop: 3, fontWeight: 500 }}>
                                ⚠ {error}
                              </div>
                            ) : (
                              /* Dòng quy đổi luôn hiển thị AC-08 */
                              m != null && m >= 0 && m < 100 && (
                                <div style={{ color: '#0369a1', fontSize: 11, marginTop: 3, fontWeight: 500 }}>
                                  Lãi gộp {m.toFixed(2)}% = cộng thêm {(markupEquiv ?? 0).toFixed(2)}% trên giá vốn
                                </div>
                              )
                            )}
                          </div>
                        )}
                        <div style={{ color: 'var(--text-muted, #64748b)', fontSize: 11, marginTop: 4 }}>
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
                      {error ? '—' : formatMoney(c.projectedPrice)}
                    </div>
                    {!error && c.isBelowCost && (
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
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
};
