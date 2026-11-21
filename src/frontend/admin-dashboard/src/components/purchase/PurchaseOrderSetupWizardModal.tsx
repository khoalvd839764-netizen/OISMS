import React, { useEffect, useState } from 'react';
import { AlertCircle, AlertTriangle, ArrowLeft, ArrowRight, Check, CheckCircle2 } from 'lucide-react';
import { Button, Input, Modal, Select } from '../ui';
import { NumberField } from '../NumberField';
import { purchaseOrderService } from '../../services/purchaseOrderService';
import { apiAdapter } from '../../services/apiAdapter';
import type {
  PurchaseOrderPriceSuggestion,
  PurchaseOrderResponse,
  UpdatePurchaseOrderDraftDto,
} from '../../types/purchaseOrder';
import { SalePriceConfigTable, type SalePriceConfigRowItem } from './SalePriceConfigTable';

interface PurchaseOrderSetupWizardModalProps {
  po: PurchaseOrderResponse;
  onClose: () => void;
  onRefresh: () => void;
  onSuccessApprove: () => void;
  onMarkSalePriceConfigViewed: () => void;
}

interface Step1Line {
  id: string;
  skuId: string;
  skuCode: string;
  productName: string;
  unitId: string | null;
  unitName: string;
  factorSnapshot: number;
  quantity: number;
  unitPrice: number | null;
}

export const PurchaseOrderSetupWizardModal: React.FC<PurchaseOrderSetupWizardModalProps> = ({
  po: initialPo,
  onClose,
  onRefresh,
  onSuccessApprove,
  onMarkSalePriceConfigViewed,
}) => {
  const [currentPo, setCurrentPo] = useState<PurchaseOrderResponse>(initialPo);
  const [step, setStep] = useState<1 | 2 | 3>(1);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const todayStr = new Date().toISOString().split('T')[0];

  // Bước 1 state
  const [suppliers, setSuppliers] = useState<Array<{ id: string; name: string }>>([]);
  const [supplierId, setSupplierId] = useState(currentPo.supplierId || '');
  const [orderDate, setOrderDate] = useState(
    currentPo.orderDate
      ? currentPo.orderDate.split('T')[0]
      : todayStr
  );
  const [shippingFee, setShippingFee] = useState<number>(currentPo.shippingFee ?? 0);
  const [discountAmount, setDiscountAmount] = useState<number>(currentPo.discountAmount ?? 0);
  const [vatAmount, setVatAmount] = useState<number>(currentPo.vatAmount ?? 0);
  const [lines, setLines] = useState<Step1Line[]>(() =>
    currentPo.items.map((it) => ({
      id: it.id,
      skuId: it.skuId,
      skuCode: it.skuCode,
      productName: it.productName,
      unitId: it.unitId,
      unitName: it.unitName,
      factorSnapshot: it.factorSnapshot,
      quantity: it.quantity,
      unitPrice: it.unitPrice ?? null,
    }))
  );
  const [suggestions, setSuggestions] = useState<Record<string, PurchaseOrderPriceSuggestion>>({});

  // Bước 2 state
  const [salePriceConfigs, setSalePriceConfigs] = useState<SalePriceConfigRowItem[]>(() =>
    currentPo.items.flatMap((it) =>
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
  const [isConfigValid, setIsConfigValid] = useState(true);

  // Bước 3 state (xác nhận)
  const [confirmZeroPrice, setConfirmZeroPrice] = useState(false);
  const [confirmLossPrice, setConfirmLossPrice] = useState(false);

  // Load suppliers
  useEffect(() => {
    let active = true;
    apiAdapter.getSuppliers().then((res) => {
      if (!active) return;
      if (res.data) {
        setSuppliers(res.data);
      }
    });
    return () => {
      active = false;
    };
  }, []);

  // Load suggestions
  useEffect(() => {
    if (!currentPo.branchId) return;
    let active = true;

    lines.forEach((line) => {
      if (!line.skuId) return;
      const key = `${currentPo.branchId}:${supplierId || 'none'}:${line.skuId}:${line.unitId || 'base'}`;
      if (suggestions[key]) return;

      purchaseOrderService
        .getPriceSuggestion({
          branchId: currentPo.branchId,
          supplierId: supplierId || undefined,
          skuId: line.skuId,
          unitId: line.unitId || undefined,
        })
        .then((res) => {
          if (!active) return;
          setSuggestions((prev) => ({ ...prev, [key]: res }));
        })
        .catch(() => {});
    });

    return () => {
      active = false;
    };
  }, [currentPo.branchId, supplierId, lines]);

  // Cập nhật configs khi currentPo thay đổi
  const syncConfigsFromPo = (po: PurchaseOrderResponse) => {
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
  };

  const handlePriceChange = (index: number, price: number | null) => {
    setLines((prev) =>
      prev.map((l, i) =>
        i === index ? { ...l, unitPrice: price === null ? null : Math.max(0, price) } : l
      )
    );
  };

  // Tính toán sơ bộ tổng tiền Bước 1
  const subtotal = lines.reduce(
    (sum, l) => sum + (Number(l.quantity) || 0) * (Number(l.unitPrice) || 0),
    0
  );
  const netAllocatedCost = Math.max(
    0,
    subtotal + (Number(shippingFee) || 0) - (Number(discountAmount) || 0)
  );
  const totalPayable = netAllocatedCost + (Number(vatAmount) || 0);

  const computePreviewAllocatedUnitCost = (line: Step1Line, idx: number): number | null => {
    if (line.unitPrice === null || line.quantity <= 0) return null;
    if (discountAmount > subtotal) return null;

    const netAdj = (Number(shippingFee) || 0) - (Number(discountAmount) || 0);
    const totalQty = lines.reduce((s, l) => s + (Number(l.quantity) || 0), 0);

    let running = 0;
    for (let i = 0; i < lines.length; i++) {
      const cur = lines[i];
      const q = Number(cur.quantity) || 0;
      const p = Number(cur.unitPrice) || 0;
      const lineTotal = q * p;
      if (i < lines.length - 1) {
        const rawAlloc =
          subtotal > 0
            ? lineTotal + (netAdj * lineTotal) / subtotal
            : totalQty > 0
            ? ((Number(shippingFee) || 0) * q) / totalQty
            : 0;
        const unitCost = q > 0 ? Math.round((rawAlloc / q) * 1e6) / 1e6 : 0;
        if (i === idx) return unitCost;
        running += Math.round(unitCost * q * 1e6) / 1e6;
      } else {
        const lastTotal = netAllocatedCost - running;
        const lastUnitCost = q > 0 ? Math.round((lastTotal / q) * 1e6) / 1e6 : 0;
        if (i === idx) return lastUnitCost;
      }
    }
    return null;
  };

  // Chuyển từ Bước 1 sang Bước 2: Lưu thông tin đơn mua
  const handleProceedToStep2 = async () => {
    if (!supplierId) {
      setError('Vui lòng chọn nhà cung cấp trước khi tiếp tục.');
      return;
    }
    if (orderDate && orderDate > todayStr) {
      setError('Ngày đặt hàng không được lớn hơn ngày hiện tại.');
      return;
    }
    if (lines.some((l) => l.unitPrice === null || l.unitPrice === undefined)) {
      setError('Vui lòng nhập đơn giá mua cho tất cả các dòng mặt hàng.');
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const itemsPayload = lines.map((l) => ({
        skuId: l.skuId,
        unitId: l.unitId || null,
        quantity: Number(l.quantity),
        unitPrice: Number(l.unitPrice),
      }));

      const updateDto: UpdatePurchaseOrderDraftDto = {
        branchId: currentPo.branchId,
        supplierId: supplierId || null,
        orderDate: orderDate || undefined,
        shippingFee: Number(shippingFee) || 0,
        discountAmount: Number(discountAmount) || 0,
        vatAmount: Number(vatAmount) || 0,
        version: currentPo.version,
        number: currentPo.number || null,
        note: currentPo.note || null,
        items: itemsPayload,
      };

      const updated = await purchaseOrderService.updateDraftPurchaseOrder(currentPo.id, updateDto);
      setCurrentPo(updated);
      syncConfigsFromPo(updated);
      onRefresh();
      setStep(2);
    } catch (err: any) {
      if (err?.response?.status === 409 || err?.response?.data?.errorCode === 'CONCURRENCY_CONFLICT') {
        setError('Đơn vừa được sửa ở nơi khác, tải lại...');
        try {
          const fresh = await purchaseOrderService.getPurchaseOrderById(currentPo.id);
          setCurrentPo(fresh);
          syncConfigsFromPo(fresh);
          onRefresh();
        } catch {
          // ignore
        }
      } else {
        setError(
          err?.response?.data?.detail ||
          err?.response?.data?.message ||
          'Không thể lưu thông tin đơn mua ở Bước 1.'
        );
      }
    } finally {
      setLoading(false);
    }
  };

  // Chuyển từ Bước 2 sang Bước 3: Lưu cấu hình giá bán
  const handleProceedToStep3 = async () => {
    if (!isConfigValid) {
      setError('Vui lòng sửa các trường cấu hình giá bán không hợp lệ trước khi tiếp tục.');
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const itemsMap = new Map<string, any[]>();
      for (const c of salePriceConfigs) {
        const itemId = c.purchaseOrderItemId || '';
        if (!itemId) continue;
        if (!itemsMap.has(itemId)) {
          itemsMap.set(itemId, []);
        }
        itemsMap.get(itemId)!.push({
          unitId: c.unitId,
          priceListId: c.priceListId,
          mode: c.mode,
          marginPercent: c.marginPercent,
          fixedPrice: c.fixedPrice,
          priceSource: c.priceSource,
          applyMode: c.applyMode,
          applyFrom: c.applyFrom ? new Date(c.applyFrom).toISOString() : null,
        });
      }

      const payload = {
        version: currentPo.version,
        items: Array.from(itemsMap.entries()).map(([purchaseOrderItemId, configs]) => ({
          purchaseOrderItemId,
          configs,
        })),
      };

      await purchaseOrderService.updateSalePriceConfigs(currentPo.id, payload);
      onMarkSalePriceConfigViewed();

      const fresh = await purchaseOrderService.getPurchaseOrderById(currentPo.id);
      setCurrentPo(fresh);
      syncConfigsFromPo(fresh);
      onRefresh();
      setStep(3);
    } catch (err: any) {
      if (err?.response?.status === 409 || err?.response?.data?.errorCode === 'CONCURRENCY_CONFLICT') {
        setError('Đơn vừa được sửa ở nơi khác, tải lại...');
        try {
          const fresh = await purchaseOrderService.getPurchaseOrderById(currentPo.id);
          setCurrentPo(fresh);
          syncConfigsFromPo(fresh);
          onRefresh();
        } catch {
          // ignore
        }
      } else {
        setError(
          err?.response?.data?.detail ||
          err?.response?.data?.message ||
          'Không thể lưu cấu hình giá bán ở Bước 2.'
        );
      }
    } finally {
      setLoading(false);
    }
  };

  // Kiểm tra cảnh báo Bước 3
  const hasZeroPrice =
    currentPo.approvalReadiness?.hasZeroPrice ??
    currentPo.items.some((i) => Number(i.unitPrice) === 0);

  const hasLossPrice =
    currentPo.approvalReadiness?.hasLossPrice ??
    salePriceConfigs.some((c) => c.isBelowCost);

  const canApprove =
    (!hasZeroPrice || confirmZeroPrice) &&
    (!hasLossPrice || confirmLossPrice);

  // Thực hiện duyệt ở Bước 3
  const handleApprove = async () => {
    if (!canApprove) return;

    setLoading(true);
    setError(null);
    try {
      await purchaseOrderService.approvePurchaseOrder(
        currentPo.id,
        confirmZeroPrice,
        currentPo.version,
        confirmLossPrice
      );
      onSuccessApprove();
    } catch (err: any) {
      if (err?.response?.status === 409 || err?.response?.data?.errorCode === 'CONCURRENCY_CONFLICT') {
        setError('Đơn vừa được sửa ở nơi khác, tải lại...');
        try {
          const fresh = await purchaseOrderService.getPurchaseOrderById(currentPo.id);
          setCurrentPo(fresh);
          syncConfigsFromPo(fresh);
          onRefresh();
        } catch {
          // ignore
        }
      } else {
        setError(
          err?.response?.data?.detail ||
          err?.response?.data?.message ||
          'Không thể duyệt đơn mua hàng.'
        );
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <Modal
      wide
      title={`Thiết lập đơn mua & Duyệt: ${currentPo.number} (v${currentPo.version})`}
      onClose={onClose}
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16, padding: '10px 16px 20px' }}>
        {/* Thanh tiến trình 3 bước */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '12px 20px',
            background: 'var(--surface-subtle, #f8fafc)',
            borderRadius: 8,
            border: '1px solid #e2e8f0',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span
              style={{
                width: 28,
                height: 28,
                borderRadius: '50%',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                background: step === 1 ? '#0284c7' : step > 1 ? '#10b981' : '#e2e8f0',
                color: step >= 1 ? '#ffffff' : '#64748b',
                fontWeight: 600,
                fontSize: 13,
              }}
            >
              {step > 1 ? <Check size={16} /> : '1'}
            </span>
            <span style={{ fontWeight: step === 1 ? 600 : 500, fontSize: 13, color: step === 1 ? '#0284c7' : 'inherit' }}>
              B1: NCC & Đơn giá mua
            </span>
          </div>

          <div style={{ width: 40, height: 2, background: step > 1 ? '#10b981' : '#cbd5e1' }} />

          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span
              style={{
                width: 28,
                height: 28,
                borderRadius: '50%',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                background: step === 2 ? '#0284c7' : step > 2 ? '#10b981' : '#e2e8f0',
                color: step >= 2 ? '#ffffff' : '#64748b',
                fontWeight: 600,
                fontSize: 13,
              }}
            >
              {step > 2 ? <Check size={16} /> : '2'}
            </span>
            <span style={{ fontWeight: step === 2 ? 600 : 500, fontSize: 13, color: step === 2 ? '#0284c7' : 'inherit' }}>
              B2: Cấu hình giá bán
            </span>
          </div>

          <div style={{ width: 40, height: 2, background: step > 2 ? '#10b981' : '#cbd5e1' }} />

          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span
              style={{
                width: 28,
                height: 28,
                borderRadius: '50%',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                background: step === 3 ? '#0284c7' : '#e2e8f0',
                color: step === 3 ? '#ffffff' : '#64748b',
                fontWeight: 600,
                fontSize: 13,
              }}
            >
              3
            </span>
            <span style={{ fontWeight: step === 3 ? 600 : 500, fontSize: 13, color: step === 3 ? '#0284c7' : 'inherit' }}>
              B3: Xác nhận & Duyệt
            </span>
          </div>
        </div>

        {/* Thông báo lỗi nếu có */}
        {error && (
          <div
            role="alert"
            style={{
              padding: '10px 14px',
              borderRadius: 6,
              background: '#fee2e2',
              color: '#991b1b',
              fontSize: 13,
              display: 'flex',
              alignItems: 'center',
              gap: 8,
            }}
          >
            <AlertCircle size={16} />
            <span>{error}</span>
          </div>
        )}

        {/* NỘI DUNG BƯỚC 1: NCC & ĐƠN GIÁ MUA */}
        {step === 1 && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
                gap: 12,
                padding: 12,
                background: 'var(--surface-subtle, #f8fafc)',
                borderRadius: 8,
              }}
            >
              <div>
                <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 4 }}>
                  Nhà cung cấp *
                </label>
                <Select
                  value={supplierId}
                  onChange={(e) => setSupplierId(e.target.value)}
                  disabled={loading}
                >
                  <option value="">-- Chọn nhà cung cấp --</option>
                  {suppliers.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                    </option>
                  ))}
                </Select>
              </div>

              <div>
                <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 4 }}>
                  Ngày đặt hàng *
                </label>
                <Input
                  type="date"
                  max={todayStr}
                  value={orderDate}
                  onChange={(e) => setOrderDate(e.target.value)}
                  disabled={loading}
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 4 }}>
                  Chi nhánh
                </label>
                <Input value={currentPo.branchName} disabled />
              </div>
            </div>

            {/* Bảng nhập đơn giá từng dòng */}
            <div>
              <h4 style={{ margin: '0 0 8px 0', fontSize: 13 }}>
                Nhập đơn giá mua cho các mặt hàng (Bắt buộc đủ mọi dòng)
              </h4>
              <div className="table-wrap">
                <table style={{ width: '100%', fontSize: 13 }}>
                  <thead>
                    <tr>
                      <th style={{ width: '25%' }}>Mặt hàng</th>
                      <th style={{ width: '12%' }}>Đơn vị</th>
                      <th style={{ width: '10%' }}>Số lượng</th>
                      <th style={{ width: '20%' }}>Đơn giá mua (₫) *</th>
                      <th style={{ width: '18%' }}>Gợi ý giá</th>
                      <th style={{ width: '15%' }}>Giá vốn dự kiến</th>
                    </tr>
                  </thead>
                  <tbody>
                    {lines.map((l, idx) => {
                      const suggKey = `${currentPo.branchId}:${supplierId || 'none'}:${l.skuId}:${l.unitId || 'base'}`;
                      const sugg = suggestions[suggKey];
                      const previewCost = computePreviewAllocatedUnitCost(l, idx);

                      return (
                        <tr key={l.id}>
                          <td>
                            <strong>{l.skuCode}</strong>
                            <div style={{ fontSize: 12, color: 'var(--text-muted, #64748b)' }}>
                              {l.productName}
                            </div>
                          </td>
                          <td>
                            {l.unitName}
                            {l.factorSnapshot > 1 && (
                              <div style={{ fontSize: 11, color: 'var(--text-muted, #64748b)' }}>
                                (x{l.factorSnapshot})
                              </div>
                            )}
                          </td>
                          <td>
                            <strong>{l.quantity}</strong>
                          </td>
                          <td>
                            <NumberField
                              value={l.unitPrice}
                              onChange={(val) => handlePriceChange(idx, val)}
                              placeholder="Nhập giá mua (0đ = tặng)"
                              disabled={loading}
                            />
                            {l.unitPrice === 0 && (
                              <div style={{ fontSize: 11, color: '#d97706', marginTop: 2 }}>
                                ⚠️ Giá 0đ (Hàng tặng)
                              </div>
                            )}
                          </td>
                          <td style={{ fontSize: 11, color: 'var(--text-muted, #64748b)' }}>
                            {sugg ? (
                              <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                                {sugg.suggestedUnitPrice != null && (
                                  <button
                                    type="button"
                                    onClick={() => handlePriceChange(idx, sugg.suggestedUnitPrice ?? null)}
                                    title="Nhấn để áp dụng giá mua gần nhất"
                                    style={{
                                      border: 'none',
                                      background: 'none',
                                      padding: 0,
                                      cursor: 'pointer',
                                      textAlign: 'left',
                                      color: '#0284c7',
                                      fontSize: 11,
                                    }}
                                  >
                                    Gần nhất: {sugg.suggestedUnitPrice.toLocaleString('vi-VN')} ₫
                                  </button>
                                )}
                                {sugg.currentUnitWac != null && (
                                  <button
                                    type="button"
                                    onClick={() => handlePriceChange(idx, sugg.currentUnitWac)}
                                    title="Nhấn để áp dụng WAC hiện tại"
                                    style={{
                                      border: 'none',
                                      background: 'none',
                                      padding: 0,
                                      cursor: 'pointer',
                                      textAlign: 'left',
                                      color: '#64748b',
                                      fontSize: 11,
                                    }}
                                  >
                                    WAC: {sugg.currentUnitWac.toLocaleString('vi-VN')} ₫
                                  </button>
                                )}
                              </div>
                            ) : (
                              <span>—</span>
                            )}
                          </td>
                          <td>
                            {previewCost != null ? (
                              <strong>{Math.round(previewCost).toLocaleString('vi-VN')} ₫</strong>
                            ) : (
                              <span style={{ color: '#94a3b8' }}>—</span>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>

            {/* Chi phí phụ & Tổng kết */}
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))',
                gap: 12,
                padding: 12,
                background: 'var(--surface-subtle, #f8fafc)',
                borderRadius: 8,
              }}
            >
              <div>
                <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 4 }}>
                  Phí vận chuyển (₫)
                </label>
                <NumberField
                  value={shippingFee}
                  onChange={(val) => setShippingFee(val ?? 0)}
                  disabled={loading}
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 4 }}>
                  Chiết khấu (₫)
                </label>
                <NumberField
                  value={discountAmount}
                  onChange={(val) => setDiscountAmount(val ?? 0)}
                  disabled={loading}
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 4 }}>
                  Thuế VAT (₫)
                </label>
                <NumberField
                  value={vatAmount}
                  onChange={(val) => setVatAmount(val ?? 0)}
                  disabled={loading}
                />
              </div>

              <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
                <span style={{ fontSize: 12, color: 'var(--text-muted, #64748b)' }}>Tổng thanh toán:</span>
                <strong style={{ fontSize: 16, color: '#0284c7' }}>
                  {totalPayable.toLocaleString('vi-VN')} ₫
                </strong>
              </div>
            </div>
          </div>
        )}

        {/* NỘI DUNG BƯỚC 2: CẤU HÌNH GIÁ BÁN */}
        {step === 2 && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
            <div>
              <h4 style={{ margin: '0 0 4px 0', fontSize: 14 }}>
                Cấu hình giá bán tự động khi nhập kho (Q15 – Q18)
              </h4>
              <p style={{ margin: 0, fontSize: 12, color: 'var(--text-muted, #64748b)' }}>
                Thiết lập công thức tính giá bán theo WAC + % Lãi gộp hoặc Giá mới cố định cho từng bảng giá.
              </p>
            </div>

            <SalePriceConfigTable
              configs={salePriceConfigs}
              onChange={setSalePriceConfigs}
              onValidationChange={setIsConfigValid}
              readOnly={false}
            />
          </div>
        )}

        {/* NỘI DUNG BƯỚC 3: XÁC NHẬN & DUYỆT */}
        {step === 3 && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            {/* Tóm tắt thông tin */}
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
                <span style={{ color: 'var(--text-muted, #64748b)' }}>Nhà cung cấp: </span>
                <div>
                  <strong>{currentPo.supplierName || 'Chưa chọn'}</strong>
                </div>
              </div>
              <div>
                <span style={{ color: 'var(--text-muted, #64748b)' }}>Chi nhánh: </span>
                <div>
                  <strong>{currentPo.branchName}</strong>
                </div>
              </div>
              <div>
                <span style={{ color: 'var(--text-muted, #64748b)' }}>Ngày đặt hàng: </span>
                <div>
                  <strong>
                    {currentPo.orderDate
                      ? new Date(currentPo.orderDate).toLocaleDateString('vi-VN')
                      : new Date().toLocaleDateString('vi-VN')}
                  </strong>
                </div>
              </div>
            </div>

            {/* Bảng tóm tắt mặt hàng và giá vốn sau phân bổ */}
            <div className="table-wrap">
              <table style={{ width: '100%', fontSize: 13 }}>
                <thead>
                  <tr>
                    <th>Mặt hàng</th>
                    <th>Đơn vị</th>
                    <th>Số lượng</th>
                    <th>Đơn giá mua</th>
                    <th>Giá vốn phân bổ</th>
                    <th>Thành tiền</th>
                  </tr>
                </thead>
                <tbody>
                  {currentPo.items.map((it) => (
                    <tr key={it.id}>
                      <td>
                        <strong>{it.skuCode}</strong>
                        <div style={{ fontSize: 12, color: 'var(--text-muted, #64748b)' }}>
                          {it.productName}
                        </div>
                      </td>
                      <td>{it.unitName}</td>
                      <td>{it.quantity}</td>
                      <td>
                        {it.unitPrice != null
                          ? `${Number(it.unitPrice).toLocaleString('vi-VN')} ₫`
                          : '—'}
                      </td>
                      <td>
                        <strong>
                          {it.allocatedUnitCost != null
                            ? `${Number(it.allocatedUnitCost).toLocaleString('vi-VN')} ₫`
                            : '—'}
                        </strong>
                      </td>
                      <td>
                        {it.lineTotal != null
                          ? `${Number(it.lineTotal).toLocaleString('vi-VN')} ₫`
                          : '—'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Tóm tắt tài chính */}
            <div
              style={{
                display: 'flex',
                justifyContent: 'flex-end',
                gap: 16,
                padding: '10px 14px',
                background: 'var(--surface-subtle, #f8fafc)',
                borderRadius: 6,
                fontSize: 13,
                flexWrap: 'wrap',
              }}
            >
              <span>
                Tiền hàng: <strong>{(currentPo.subtotal ?? 0).toLocaleString('vi-VN')} ₫</strong>
              </span>
              <span>
                Phí VC: <strong>+{(currentPo.shippingFee ?? 0).toLocaleString('vi-VN')} ₫</strong>
              </span>
              <span>
                Chiết khấu: <strong>−{(currentPo.discountAmount ?? 0).toLocaleString('vi-VN')} ₫</strong>
              </span>
              <span>
                Giá vốn phân bổ:{' '}
                <strong>{(currentPo.totalAllocatedCost ?? 0).toLocaleString('vi-VN')} ₫</strong>
              </span>
              <span>
                VAT: <strong>+{(currentPo.vatAmount ?? 0).toLocaleString('vi-VN')} ₫</strong>
              </span>
              <span>
                Tổng thanh toán:{' '}
                <strong style={{ fontSize: 15, color: '#0284c7' }}>
                  {(currentPo.totalAmount ?? 0).toLocaleString('vi-VN')} ₫
                </strong>
              </span>
            </div>

            {/* Hộp xác nhận giá 0đ */}
            {hasZeroPrice && (
              <div
                style={{
                  padding: 12,
                  borderRadius: 6,
                  background: '#fef3c7',
                  border: '1px solid #f59e0b',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 8,
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, color: '#92400e', fontWeight: 600, fontSize: 13 }}>
                  <AlertTriangle size={18} />
                  <span>Cảnh báo: Có mặt hàng đơn giá 0đ</span>
                </div>
                <p style={{ margin: 0, fontSize: 12, color: '#78350f' }}>
                  Đơn mua có mặt hàng có đơn giá bằng 0đ (hàng khuyến mãi/tặng kèm). Bạn cần xác nhận trước khi duyệt.
                </p>
                <label
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 8,
                    cursor: 'pointer',
                    fontSize: 13,
                    fontWeight: 600,
                    color: '#92400e',
                    marginTop: 4,
                  }}
                >
                  <input
                    type="checkbox"
                    checked={confirmZeroPrice}
                    onChange={(e) => setConfirmZeroPrice(e.target.checked)}
                    disabled={loading}
                  />
                  <span>Tôi xác nhận duyệt đơn mua này có mặt hàng đơn giá 0đ.</span>
                </label>
              </div>
            )}

            {/* Hộp xác nhận bán lỗ */}
            {hasLossPrice && (
              <div
                style={{
                  padding: 12,
                  borderRadius: 6,
                  background: '#fee2e2',
                  border: '1px solid #ef4444',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 8,
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, color: '#991b1b', fontWeight: 600, fontSize: 13 }}>
                  <AlertTriangle size={18} />
                  <span>Cảnh báo: Có cấu hình giá bán thấp hơn giá vốn (bán lỗ)</span>
                </div>
                <p style={{ margin: 0, fontSize: 12, color: '#7f1d1d' }}>
                  Đơn mua này có cấu hình giá bán dự kiến thấp hơn giá vốn phân bổ. Khi nhập kho, hệ thống sẽ tự động cập nhật giá bán lỗ theo cấu hình đã chọn.
                </p>
                <label
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 8,
                    cursor: 'pointer',
                    fontSize: 13,
                    fontWeight: 600,
                    color: '#991b1b',
                    marginTop: 4,
                  }}
                >
                  <input
                    type="checkbox"
                    checked={confirmLossPrice}
                    onChange={(e) => setConfirmLossPrice(e.target.checked)}
                    disabled={loading}
                  />
                  <span>Tôi xác nhận duyệt đơn mua với cấu hình bán lỗ.</span>
                </label>
              </div>
            )}
          </div>
        )}

        {/* NÚT ĐIỀU HƯỚNG CUỐI MODAL */}
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            marginTop: 10,
            borderTop: '1px solid #e2e8f0',
            paddingTop: 14,
          }}
        >
          <div>
            <Button variant="secondary" onClick={onClose} disabled={loading}>
              Đóng (Lưu tiến độ)
            </Button>
          </div>

          <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
            {step === 1 && (
              <Button variant="primary" onClick={handleProceedToStep2} disabled={loading}>
                {loading ? 'Đang lưu...' : (
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                    Tiếp: Cấu hình giá bán <ArrowRight size={16} />
                  </span>
                )}
              </Button>
            )}

            {step === 2 && (
              <>
                <Button variant="secondary" onClick={() => setStep(1)} disabled={loading}>
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                    <ArrowLeft size={16} /> Quay lại B1
                  </span>
                </Button>
                <Button
                  variant="primary"
                  onClick={handleProceedToStep3}
                  disabled={loading || !isConfigValid}
                >
                  {loading ? 'Đang lưu...' : (
                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                      Tiếp: Xác nhận & Duyệt <ArrowRight size={16} />
                    </span>
                  )}
                </Button>
              </>
            )}

            {step === 3 && (
              <>
                <Button variant="secondary" onClick={() => setStep(2)} disabled={loading}>
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                    <ArrowLeft size={16} /> Quay lại B2
                  </span>
                </Button>
                <Button
                  variant="primary"
                  onClick={handleApprove}
                  disabled={loading || !canApprove}
                >
                  {loading ? 'Đang duyệt...' : (
                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                      <CheckCircle2 size={16} /> Duyệt đơn mua
                    </span>
                  )}
                </Button>
              </>
            )}
          </div>
        </div>
      </div>
    </Modal>
  );
};
