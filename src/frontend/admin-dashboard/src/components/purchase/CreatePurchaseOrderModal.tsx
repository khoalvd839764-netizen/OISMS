import React, { useEffect, useRef, useState } from 'react';
import { Button, Input, Modal, Select } from '../ui';
import { NumberField } from '../NumberField';
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
    isDefaultPurchase: boolean;
  }>;
}

const defaultUnitId = (option: VariantOption) =>
  (option.units.find((u) => u.isDefaultPurchase) || option.units.find((u) => u.isBase) || option.units[0])?.unitId ||
  option.baseUnitId ||
  '';

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
  const [loadError, setLoadError] = useState<string | null>(null);
  const [quickSupplierOpen, setQuickSupplierOpen] = useState(false);
  const [newSupplierName, setNewSupplierName] = useState('');
  const [savingSupplier, setSavingSupplier] = useState(false);

  // Idempotency Key - bảo lưu suốt quá trình retry
  const idempotencyKeyRef = useRef<string>(initialPo?.requestKey || crypto.randomUUID());

  useEffect(() => {
    let active = true;
    Promise.all([
      apiAdapter.getBranches(),
      apiAdapter.getSuppliers(),
      apiAdapter.getSkus(),
    ]).then(([branchesRes, suppliersRes, skusRes]) => {
      if (!active) return;
      const failed = branchesRes.error || suppliersRes.error || skusRes.error;
      if (failed) {
        setLoadError(failed.message || 'Không thể tải dữ liệu để lập đơn mua.');
      }
      if (branchesRes.data) {
        setBranches(branchesRes.data);
        if (!branchId && branchesRes.data.length > 0) {
          setBranchId(branchesRes.data[0].id);
        }
      }
      if (suppliersRes.data) {
        setSuppliers(suppliersRes.data);
        if (!supplierId && suppliersRes.data.length === 1) {
          setSupplierId(suppliersRes.data[0].id);
        }
      }
      if (skusRes.data) {
        // getSkus trả danh sách SKU phẳng; bỏ dòng sản phẩm chưa có biến thể
        const flat: VariantOption[] = skusRes.data
          .filter((sku) => sku.isActive !== false && sku.id !== sku.productId)
          .map((sku) => {
            const units = (sku.units || [])
              .filter((u) => u.isActive !== false && u.canPurchase !== false)
              .map((u) => ({
                id: u.id,
                unitId: u.unitId,
                unitName: u.unitName,
                factorToBase: u.factorToBase,
                isBase: u.isBase,
                isDefaultPurchase: u.isDefaultPurchase,
              }));
            if (sku.baseUnitId && !units.some((u) => u.unitId === sku.baseUnitId)) {
              units.unshift({
                id: sku.baseUnitId,
                unitId: sku.baseUnitId,
                unitName: sku.baseUnitName || 'Đơn vị gốc',
                factorToBase: 1,
                isBase: true,
                isDefaultPurchase: false,
              });
            }
            return {
              productId: sku.productId,
              variantId: sku.id,
              sku: sku.code,
              productName: sku.name,
              variantName: sku.variant,
              baseUnitId: sku.baseUnitId || undefined,
              baseUnitName: sku.baseUnitName || undefined,
              units,
            };
          });
        setVariantOptions(flat);

        // Khôi phục dòng khi sửa bản nháp
        if (initialPo) {
          setLines((current) =>
            current.map((l) => {
              const matched = flat.find((f) => f.variantId === l.skuId);
              return matched
                ? { ...l, productId: matched.productId, unitId: l.unitId || defaultUnitId(matched) }
                : l;
            })
          );
        }
      }
    });

    return () => {
      active = false;
    };
  }, []);

  const handleAddLine = () => {
    if (variantOptions.length === 0) {
      setError('Chưa có SKU đang hoạt động. Vui lòng tạo sản phẩm trước khi lập đơn mua.');
      return;
    }
    if (lines.length >= variantOptions.length) {
      setError('Đã thêm hết các SKU đang có. Mỗi SKU chỉ có một dòng trên đơn mua.');
      return;
    }
    setError(null);
    setLines([...lines, { skuId: '', productId: '', unitId: '', quantity: 1, unitPrice: 0 }]);
  };

  // Tạo nhanh nhà cung cấp ngay trên form và chọn luôn
  const handleQuickCreateSupplier = async () => {
    const name = newSupplierName.trim();
    if (!name) {
      setError('Vui lòng nhập tên nhà cung cấp.');
      return;
    }
    setSavingSupplier(true);
    setError(null);
    const res = await apiAdapter.createSupplier({ name });
    setSavingSupplier(false);
    if (res.error) {
      setError(res.error.message || 'Không thể tạo nhà cung cấp.');
      return;
    }
    const created = res.data as { id: string; name: string };
    setSuppliers((current) => [...current, { id: created.id, name: created.name }]);
    setSupplierId(created.id);
    setNewSupplierName('');
    setQuickSupplierOpen(false);
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
              unitId: matched ? defaultUnitId(matched) : '',
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
      prev.map((l, i) => (i === index ? { ...l, quantity: qty } : l))
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
      wide
      title={isEdit ? `Sửa bản nháp đơn mua: ${initialPo?.number}` : 'Tạo đơn mua hàng (PO)'}
      onClose={onClose}
    >
      <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 16, padding: 20 }}>
        {loadError && (
          <div role="alert" style={{ padding: 12, borderRadius: 6, background: '#fee2e2', color: '#991b1b', fontSize: 14 }}>
            {loadError}
          </div>
        )}
        {!loadError && variantOptions.length === 0 && (
          <div style={{ padding: 12, borderRadius: 6, background: '#fef3c7', color: '#92400e', fontSize: 14 }}>
            Chưa có SKU đang hoạt động để đặt mua. Vui lòng tạo sản phẩm ở trang Sản phẩm trước.
          </div>
        )}
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
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
              <label style={{ fontWeight: 600 }}>
                Nhà cung cấp <span style={{ color: '#ef4444' }}>*</span>
              </label>
              <button
                type="button"
                onClick={() => setQuickSupplierOpen((open) => !open)}
                style={{ background: 'none', border: 'none', color: '#1769e0', fontSize: 12, fontWeight: 600, cursor: 'pointer' }}
              >
                {quickSupplierOpen ? 'Đóng' : '+ Thêm nhà cung cấp'}
              </button>
            </div>
            {quickSupplierOpen ? (
              <div className="inline-input">
                <Input
                  value={newSupplierName}
                  onChange={(e) => setNewSupplierName(e.target.value)}
                  placeholder="Tên nhà cung cấp mới"
                  maxLength={150}
                  disabled={savingSupplier}
                />
                <Button type="button" variant="secondary" onClick={handleQuickCreateSupplier} disabled={savingSupplier}>
                  {savingSupplier ? 'Đang lưu...' : 'Lưu'}
                </Button>
              </div>
            ) : (
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
            )}
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
              disabled={loading || variantOptions.length === 0}
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
                          {availableUnits.length === 0 && <option value="">Chọn mặt hàng trước</option>}
                          {availableUnits.map((u) => (
                            <option key={u.unitId} value={u.unitId}>
                              {u.unitName} {u.isBase ? '(Gốc)' : `(x${u.factorToBase})`}
                            </option>
                          ))}
                        </Select>
                      </td>
                      <td>
                        <NumberField
                          aria-label="Số lượng đặt"
                          value={line.quantity || null}
                          decimals={3}
                          onChange={(value) => handleQuantityChange(idx, value ?? 0)}
                          placeholder="Số lượng"
                          disabled={loading}
                        />
                      </td>
                      <td>
                        <NumberField
                          aria-label="Đơn giá mua"
                          value={line.unitPrice || null}
                          decimals={2}
                          suffix="₫"
                          onChange={(value) => handlePriceChange(idx, value ?? 0)}
                          placeholder="Đơn giá"
                          disabled={loading}
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

          <p style={{ margin: '8px 0 0', fontSize: 12, color: '#64748b' }}>
            Cần mua theo thùng, bao...? Cấu hình đơn vị quy đổi của SKU ở trang Sản phẩm, rồi chọn tại cột Đơn vị mua.
          </p>

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
