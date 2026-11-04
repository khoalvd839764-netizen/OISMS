import React, { useEffect, useRef, useState } from 'react';
import { Button, Input, Modal, Select } from '../ui';
import { purchaseOrderService } from '../../services/purchaseOrderService';
import { apiAdapter } from '../../services/apiAdapter';
import type {
  CreatePurchaseOrderDto,
  PurchaseOrderResponse,
} from '../../types/purchaseOrder';

interface VariantOption {
  productId: string;
  variantId: string;
  sku: string;
  productName: string;
  variantName: string;
  baseUnitId?: string;
  baseUnitName?: string;
  units: Array<{
    id: string;
    unitId: string;
    unitName: string;
    factorToBase: number;
    isBase: boolean;
  }>;
}

interface OrderLine {
  skuId: string;
  productId: string;
  unitId: string;
  quantity: number;
  unitPrice: number;
}

interface CreatePurchaseOrderModalProps {
  initialPo?: PurchaseOrderResponse | null;
  onClose: () => void;
  onSuccess: (po: PurchaseOrderResponse) => void;
}

export const CreatePurchaseOrderModal: React.FC<CreatePurchaseOrderModalProps> = ({
  initialPo,
  onClose,
  onSuccess,
}) => {
  const isEdit = Boolean(initialPo && initialPo.status === 'Draft');

  const [branches, setBranches] = useState<Array<{ id: string; name: string }>>([]);
  const [suppliers, setSuppliers] = useState<Array<{ id: string; name: string }>>([]);
  const [variantOptions, setVariantOptions] = useState<VariantOption[]>([]);

  const [branchId, setBranchId] = useState(initialPo?.branchId || '');
  const [supplierId, setSupplierId] = useState(initialPo?.supplierId || '');
  const [number, setNumber] = useState(initialPo?.number || '');
  const [note, setNote] = useState(initialPo?.note || '');

  const [lines, setLines] = useState<OrderLine[]>(() => {
    if (initialPo && initialPo.items.length > 0) {
      return initialPo.items.map((it) => ({
        skuId: it.skuId,
        productId: '',
        unitId: it.unitId || '',
        quantity: it.quantity,
        unitPrice: it.unitPrice,
      }));
    }
    return [{ skuId: '', productId: '', unitId: '', quantity: 1, unitPrice: 0 }];
  });

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Idempotency Key - bảo lưu suốt quá trình retry
  const idempotencyKeyRef = useRef<string>(initialPo?.requestKey || crypto.randomUUID());

  useEffect(() => {
    let active = true;
    Promise.all([
      apiAdapter.getBranches(),
      apiAdapter.getSuppliers(),
      apiAdapter.getProducts(),
    ]).then(([branchesRes, suppliersRes, productsRes]) => {
      if (!active) return;
      if (branchesRes.data) {
        setBranches(branchesRes.data);
        if (!branchId && branchesRes.data.length > 0) {
          setBranchId(branchesRes.data[0].id);
        }
      }
      if (suppliersRes.data) {
        setSuppliers(suppliersRes.data);
        if (!supplierId && suppliersRes.data.length > 0) {
          setSupplierId(suppliersRes.data[0].id);
        }
      }
      if (productsRes.data) {
        const flat: VariantOption[] = [];
        for (const p of productsRes.data) {
          if (p.variants && Array.isArray(p.variants)) {
            for (const v of p.variants) {
              const uList = (v.units || []).map((u: any) => ({
                id: u.id || u.unitId,
                unitId: u.unitId || u.id,
                unitName: u.unitName || '',
                factorToBase: u.factorToBase || u.factor || 1,
                isBase: Boolean(u.isBase || u.isBaseUnit),
              }));
              // Đảm bảo đơn vị gốc luôn có trong uList
              if (v.baseUnitId && !uList.some((u: any) => u.unitId === v.baseUnitId)) {
                uList.unshift({
                  id: v.baseUnitId,
                  unitId: v.baseUnitId,
                  unitName: v.baseUnitName || 'Đơn vị gốc',
                  factorToBase: 1,
                  isBase: true,
                });
              }
              flat.push({
                productId: p.id,
                variantId: v.id,
                sku: v.sku,
                productName: p.name,
                variantName: v.variantName,
                baseUnitId: v.baseUnitId,
                baseUnitName: v.baseUnitName,
                units: uList,
              });
            }
          }
        }
        setVariantOptions(flat);

        // Khôi phục line nếu đang edit
        if (initialPo) {
          setLines((current) =>
            current.map((l) => {
              const matched = flat.find((f) => f.variantId === l.skuId);
              return matched
                ? {
                    ...l,
                    productId: matched.productId,
                    unitId: l.unitId || matched.baseUnitId || '',
                  }
                : l;
            })
          );
        } else if (flat.length > 0 && lines.length === 1 && !lines[0].skuId) {
          setLines([
            {
              skuId: flat[0].variantId,
              productId: flat[0].productId,
              unitId: flat[0].baseUnitId || '',
              quantity: 1,
              unitPrice: 0,
            },
          ]);
        }
      }
    });

    return () => {
      active = false;
    };
  }, []);

  const handleAddLine = () => {
    const unselected = variantOptions.find(
      (v) => !lines.some((l) => l.skuId === v.variantId)
    );
    const chosen = unselected || variantOptions[0];
    if (!chosen) return;

    setLines([
      ...lines,
      {
        skuId: chosen.variantId,
        productId: chosen.productId,
        unitId: chosen.baseUnitId || '',
        quantity: 1,
        unitPrice: 0,
      },
    ]);
  };

  const handleRemoveLine = (index: number) => {
    if (lines.length <= 1) return;
    setLines(lines.filter((_, i) => i !== index));
  };

  const handleSkuChange = (index: number, skuId: string) => {
    const matched = variantOptions.find((v) => v.variantId === skuId);
    setLines((prev) =>
      prev.map((l, i) =>
        i === index
          ? {
              ...l,
              skuId,
              productId: matched?.productId || '',
              unitId: matched?.baseUnitId || '',
            }
          : l
      )
    );
  };

  const handleUnitChange = (index: number, unitId: string) => {
    setLines((prev) =>
      prev.map((l, i) => (i === index ? { ...l, unitId } : l))
    );
  };

  const handleQuantityChange = (index: number, qty: number) => {
    setLines((prev) =>
      prev.map((l, i) => (i === index ? { ...l, quantity: Math.max(0.001, qty) } : l))
    );
  };

  const handlePriceChange = (index: number, price: number) => {
    setLines((prev) =>
      prev.map((l, i) => (i === index ? { ...l, unitPrice: Math.max(0, price) } : l))
    );
  };

  const totalAmount = lines.reduce(
    (sum, l) => sum + (Number(l.quantity) || 0) * (Number(l.unitPrice) || 0),
    0
  );

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!branchId) {
      setError('Vui lòng chọn chi nhánh tiếp nhận.');
      return;
    }
    if (!supplierId) {
      setError('Vui lòng chọn nhà cung cấp.');
      return;
    }
    if (lines.length === 0) {
      setError('Vui lòng thêm ít nhất một dòng mặt hàng.');
      return;
    }

    // Kiểm tra trùng SKU
    const skuSet = new Set<string>();
    for (const l of lines) {
      if (!l.skuId) {
        setError('Vui lòng chọn mặt hàng cho tất cả các dòng.');
        return;
      }
      if (skuSet.has(l.skuId)) {
        setError('Mỗi SKU chỉ được có tối đa một dòng trên đơn mua hàng.');
        return;
      }
      skuSet.add(l.skuId);

      if (l.quantity <= 0) {
        setError('Số lượng đặt phải lớn hơn 0.');
        return;
      }
    }

    setLoading(true);
    setError(null);

    const dto: CreatePurchaseOrderDto = {
      branchId,
      supplierId,
      number: number.trim() || null,
      note: note.trim() || null,
      items: lines.map((l) => ({
        skuId: l.skuId,
        unitId: l.unitId || null,
        quantity: Number(l.quantity),
        unitPrice: Number(l.unitPrice),
      })),
    };

    try {
      let result: PurchaseOrderResponse;
      if (isEdit && initialPo) {
        result = await purchaseOrderService.updatePurchaseOrder(initialPo.id, dto);
      } else {
        result = await purchaseOrderService.createPurchaseOrder(
          dto,
          idempotencyKeyRef.current
        );
      }
      onSuccess(result);
    } catch (err: any) {
      setError(
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        'Không thể lưu đơn mua hàng. Vui lòng kiểm tra lại.'
      );
    } finally {
      setLoading(false);
    }
  };

  return (
    <Modal
      title={isEdit ? `Sửa bản nháp đơn mua: ${initialPo?.number}` : 'Tạo đơn mua hàng (PO)'}
      onClose={onClose}
    >
      <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
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

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
          <div>
            <label style={{ display: 'block', fontWeight: 600, marginBottom: 6 }}>
              Chi nhánh đặt hàng <span style={{ color: '#ef4444' }}>*</span>
            </label>
            <Select
              value={branchId}
              onChange={(e) => setBranchId(e.target.value)}
              disabled={loading}
              required
            >
              <option value="">-- Chọn chi nhánh --</option>
              {branches.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </Select>
          </div>

          <div>
            <label style={{ display: 'block', fontWeight: 600, marginBottom: 6 }}>
              Nhà cung cấp <span style={{ color: '#ef4444' }}>*</span>
            </label>
            <Select
              value={supplierId}
              onChange={(e) => setSupplierId(e.target.value)}
              disabled={loading}
              required
            >
              <option value="">-- Chọn nhà cung cấp --</option>
              {suppliers.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
          </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
          <div>
            <label style={{ display: 'block', fontWeight: 600, marginBottom: 6 }}>
              Mã đơn mua (Tùy chọn)
            </label>
            <Input
              value={number}
              onChange={(e) => setNumber(e.target.value)}
              placeholder="Để trống hệ thống sẽ tự sinh PO-yyyyMMdd-XXXXXX"
              maxLength={50}
              disabled={loading}
            />
          </div>

          <div>
            <label style={{ display: 'block', fontWeight: 600, marginBottom: 6 }}>
              Ghi chú đơn hàng
            </label>
            <Input
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="Ghi chú điều khoản giao hàng, chiết khấu..."
              maxLength={1000}
              disabled={loading}
            />
          </div>
        </div>

        <div>
          <div
            style={{
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              marginBottom: 8,
            }}
          >
            <h4 style={{ margin: 0, fontSize: 15 }}>
              Danh sách mặt hàng đặt mua ({lines.length})
            </h4>
            <Button
              type="button"
              variant="secondary"
              onClick={handleAddLine}
              disabled={loading}
            >
              + Thêm mặt hàng
            </Button>
          </div>

          <div className="table-wrap" style={{ maxHeight: 280, overflowY: 'auto' }}>
            <table style={{ width: '100%', fontSize: 13 }}>
              <thead>
                <tr>
                  <th style={{ width: '35%' }}>Mặt hàng (SKU)</th>
                  <th style={{ width: '20%' }}>Đơn vị mua</th>
                  <th style={{ width: '15%' }}>Số lượng</th>
                  <th style={{ width: '18%' }}>Đơn giá mua (VNĐ)</th>
                  <th style={{ width: '12%' }}>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {lines.map((line, idx) => {
                  const currentVariant = variantOptions.find(
                    (v) => v.variantId === line.skuId
                  );
                  const availableUnits = currentVariant?.units || [];

                  return (
                    <tr key={idx}>
                      <td>
                        <Select
                          value={line.skuId}
                          onChange={(e) => handleSkuChange(idx, e.target.value)}
                          disabled={loading}
                          required
                        >
                          <option value="">-- Chọn mặt hàng --</option>
                          {variantOptions.map((v) => (
                            <option key={v.variantId} value={v.variantId}>
                              {v.sku} - {v.productName} ({v.variantName})
                            </option>
                          ))}
                        </Select>
                      </td>
                      <td>
                        <Select
                          value={line.unitId}
                          onChange={(e) => handleUnitChange(idx, e.target.value)}
                          disabled={loading || availableUnits.length === 0}
                        >
                          {availableUnits.map((u) => (
                            <option key={u.unitId} value={u.unitId}>
                              {u.unitName} {u.isBase ? '(Gốc)' : `(x${u.factorToBase})`}
                            </option>
                          ))}
                        </Select>
                      </td>
                      <td>
                        <Input
                          type="number"
                          step="any"
                          min="0.001"
                          value={line.quantity}
                          onChange={(e) =>
                            handleQuantityChange(idx, parseFloat(e.target.value) || 0)
                          }
                          disabled={loading}
                          required
                        />
                      </td>
                      <td>
                        <Input
                          type="number"
                          step="any"
                          min="0"
                          value={line.unitPrice}
                          onChange={(e) =>
                            handlePriceChange(idx, parseFloat(e.target.value) || 0)
                          }
                          disabled={loading}
                          required
                        />
                      </td>
                      <td style={{ textAlign: 'center' }}>
                        <Button
                          type="button"
                          variant="ghost"
                          onClick={() => handleRemoveLine(idx)}
                          disabled={loading || lines.length <= 1}
                          style={{ color: '#ef4444' }}
                        >
                          Xóa
                        </Button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <div
            style={{
              display: 'flex',
              justifyContent: 'flex-end',
              alignItems: 'center',
              marginTop: 12,
              padding: '8px 12px',
              background: 'var(--surface-subtle, #f8fafc)',
              borderRadius: 6,
              fontSize: 14,
            }}
          >
            <span>Tổng giá trị đơn mua: </span>
            <strong style={{ marginLeft: 8, fontSize: 16, color: '#0284c7' }}>
              {totalAmount.toLocaleString('vi-VN')} ₫
            </strong>
          </div>
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 12, marginTop: 8 }}>
          <Button type="button" variant="secondary" onClick={onClose} disabled={loading}>
            Hủy
          </Button>
          <Button type="submit" disabled={loading}>
            {loading ? 'Đang lưu...' : isEdit ? 'Cập nhật bản nháp' : 'Tạo đơn mua (Draft)'}
          </Button>
        </div>
      </form>
    </Modal>
  );
};
