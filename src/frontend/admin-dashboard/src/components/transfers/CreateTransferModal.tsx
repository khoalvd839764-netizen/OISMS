import React, { useState, useEffect } from 'react';
import type { CreateStockTransferRequest } from '../../types/stockTransfer';
import { stockTransferService } from '../../services/stockTransferService';
import { apiAdapter, generateUuid } from '../../services/apiAdapter';
import type { Sku } from '../../types/domain';

interface CreateTransferModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

interface WarehouseItem {
  id: string;
  name: string;
  branchId?: string;
  branchName?: string;
}

export const CreateTransferModal: React.FC<CreateTransferModalProps> = ({
  isOpen,
  onClose,
  onSuccess,
}) => {
  const [warehouses, setWarehouses] = useState<WarehouseItem[]>([]);
  const [skus, setSkus] = useState<Sku[]>([]);
  
  const [fromWarehouseId, setFromWarehouseId] = useState('');
  const [toWarehouseId, setToWarehouseId] = useState('');
  const [note, setNote] = useState('');
  const [selectedSkuId, setSelectedSkuId] = useState('');
  const [quantity, setQuantity] = useState(1);
  
  const [items, setItems] = useState<Array<{ skuId: string; skuCode: string; quantity: number }>>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // [BF-004] Idempotency key ổn định theo chữ ký payload (đổi khi sửa form, giữ nguyên khi retry cùng ý định)
  const [intentState, setIntentState] = useState<{ sig: string; key: string }>(() => ({
    sig: '',
    key: generateUuid ? generateUuid() : (crypto.randomUUID ? crypto.randomUUID() : `tr-${Date.now()}`),
  }));

  // [BF-012] Tồn kho thực tế (OnHand, Reserved, Available) tại đúng kho nguồn đang chọn
  const [warehouseBalances, setWarehouseBalances] = useState<
    Record<string, { onHand: number; reserved: number; available: number }>
  >({});
  const [loadedWarehouseId, setLoadedWarehouseId] = useState<string | null>(null);

  useEffect(() => {
    if (!isOpen) return;

    const loadMasterData = async () => {
      try {
        const [whRes, skuRes] = await Promise.all([
          apiAdapter.getWarehouses(),
          apiAdapter.getSkus(),
        ]);
        if (whRes.data) {
          setWarehouses(whRes.data);
          if (whRes.data.length >= 2) {
            setFromWarehouseId(whRes.data[0].id);
            setToWarehouseId(whRes.data[1].id);
          } else if (whRes.data.length === 1) {
            setFromWarehouseId(whRes.data[0].id);
          }
        }
        if (skuRes.data) {
          setSkus(skuRes.data);
          if (skuRes.data.length > 0) {
            setSelectedSkuId(skuRes.data[0].id);
          }
        }
      } catch (err) {
        setError('Không thể tải dữ liệu kho và sản phẩm.');
      }
    };

    loadMasterData();
  }, [isOpen]);

  // [BF-012] Khi đổi kho nguồn, nạp lại tồn kho thực tế của kho nguồn và xóa các dòng hàng cũ không còn thuộc kho nguồn mới
  useEffect(() => {
    if (!fromWarehouseId) {
      setWarehouseBalances({});
      setLoadedWarehouseId(null);
      return;
    }
    let cancelled = false;
    setLoadedWarehouseId(null);
    const fetchWarehouseBalances = async () => {
      try {
        const res = await apiAdapter.getBalances({ warehouseId: fromWarehouseId });
        if (cancelled) return;
        if (res && res.data) {
          const balMap: Record<string, { onHand: number; reserved: number; available: number }> = {};
          res.data.forEach((b: any) => {
            const onHand = Number(b.onHand ?? 0);
            const reserved = Number(b.reserved ?? 0);
            balMap[b.id || b.skuId] = {
              onHand,
              reserved,
              available: Math.max(0, onHand - reserved),
            };
          });
          setWarehouseBalances(balMap);
          setLoadedWarehouseId(fromWarehouseId);
        }
      } catch (err) {
        console.warn('Lỗi đọc tồn kho nguồn:', err);
      }
    };
    fetchWarehouseBalances();
    return () => {
      cancelled = true;
    };
  }, [fromWarehouseId]);

  if (!isOpen) return null;

  const handleAddItem = () => {
    if (!selectedSkuId || quantity <= 0) return;
    const skuObj = skus.find((s) => s.id === selectedSkuId);
    if (!skuObj) return;

    const existingIdx = items.findIndex((i) => i.skuId === selectedSkuId);
    if (existingIdx >= 0) {
      const updated = [...items];
      updated[existingIdx].quantity += quantity;
      setItems(updated);
    } else {
      setItems([...items, { skuId: selectedSkuId, skuCode: skuObj.code, quantity }]);
    }
  };

  const handleRemoveItem = (index: number) => {
    setItems(items.filter((_, i) => i !== index));
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (fromWarehouseId === toWarehouseId) {
      setError('Kho xuất nguồn và kho nhận đích không được trùng nhau.');
      return;
    }
    if (items.length === 0) {
      setError('Vui lòng thêm ít nhất 1 mặt hàng vào phiếu chuyển.');
      return;
    }

    const fromWh = warehouses.find((w) => w.id === fromWarehouseId);
    const toWh = warehouses.find((w) => w.id === toWarehouseId);

    if (!fromWh?.branchId || !toWh?.branchId) {
      setError('Không xác định được chi nhánh trực thuộc của kho xuất hoặc kho nhận.');
      return;
    }

    setLoading(true);
    setError(null);

    const currentSig = JSON.stringify({
      fromWarehouseId,
      toWarehouseId,
      fromBranchId: fromWh.branchId,
      toBranchId: toWh.branchId,
      items: items.map((i) => ({ skuId: i.skuId, quantity: i.quantity })),
    });
    let activeKey = intentState.key;
    if (intentState.sig !== currentSig) {
      activeKey = generateUuid ? generateUuid() : (crypto.randomUUID ? crypto.randomUUID() : `tr-${Date.now()}`);
      setIntentState({ sig: currentSig, key: activeKey });
    }

    const payload: CreateStockTransferRequest = {
      fromWarehouseId,
      toWarehouseId,
      fromBranchId: fromWh.branchId,
      toBranchId: toWh.branchId,
      requestKey: activeKey,
      note: note.trim() || undefined,
      items: items.map((i) => ({ skuId: i.skuId, quantity: i.quantity })),
    };

    try {
      await stockTransferService.createTransfer(payload);
      onSuccess();
      onClose();
    } catch (err: any) {
      setError(err?.response?.data?.detail || err?.detail || err?.message || 'Không thể tạo phiếu chuyển kho. Vui lòng thử lại.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/50 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg p-6 max-w-xl w-full shadow-xl space-y-4">
        <div className="flex justify-between items-center border-b pb-2">
          <h2 className="text-lg font-bold text-gray-800">Tạo phiếu điều chuyển kho (Draft)</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600 font-bold">✕</button>
        </div>

        {error && (
          <div className="p-2.5 text-xs bg-rose-50 border border-rose-200 text-rose-700 rounded">
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit} className="space-y-4 text-xs">
          {/* Chọn Kho */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-gray-600 font-semibold mb-1">Kho xuất (Nguồn) *</label>
              <select
                value={fromWarehouseId}
                onChange={(e) => {
                  setFromWarehouseId(e.target.value);
                  setItems([]);
                }}
                className="w-full p-2 border border-gray-300 rounded focus:ring-1 focus:ring-indigo-500 bg-white"
                required
              >
                {warehouses.map((wh) => (
                  <option key={wh.id} value={wh.id}>
                    {wh.name} {wh.branchName ? `(${wh.branchName})` : ''}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-gray-600 font-semibold mb-1">Kho nhận (Đích) *</label>
              <select
                value={toWarehouseId}
                onChange={(e) => setToWarehouseId(e.target.value)}
                className="w-full p-2 border border-gray-300 rounded focus:ring-1 focus:ring-indigo-500 bg-white"
                required
              >
                {warehouses.map((wh) => (
                  <option key={wh.id} value={wh.id}>
                    {wh.name} {wh.branchName ? `(${wh.branchName})` : ''}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {/* Chọn SKU & Số lượng */}
          <div className="border border-gray-200 p-3 rounded bg-gray-50 space-y-2">
            <span className="font-semibold text-gray-700 block">Thêm mặt hàng chuyển</span>
            <div className="flex gap-2">
              <select
                value={selectedSkuId}
                onChange={(e) => setSelectedSkuId(e.target.value)}
                className="flex-1 p-2 border border-gray-300 rounded bg-white"
              >
                {skus.map((sku) => {
                  const whBal = warehouseBalances[sku.id];
                  const onHand = loadedWarehouseId === fromWarehouseId ? (whBal ? whBal.onHand : 0) : (whBal ? whBal.onHand : 0);
                  const reserved = whBal ? whBal.reserved : 0;
                  const available = whBal ? whBal.available : Math.max(0, onHand - reserved);
                  return (
                    <option key={sku.id} value={sku.id} disabled={sku.isActive === false}>
                      {sku.code} - {sku.name} (Tồn: {onHand} · Giữ: {reserved} · Khả dụng: {available})
                      {sku.isActive === false ? ' [Ngừng HĐ]' : ''}
                    </option>
                  );
                })}
              </select>
              <input
                type="number"
                min="1"
                value={quantity}
                onChange={(e) => setQuantity(Number(e.target.value))}
                className="w-20 p-2 border border-gray-300 rounded text-center bg-white"
              />
              <button
                type="button"
                onClick={handleAddItem}
                className="px-3 py-2 bg-indigo-600 text-white font-medium rounded hover:bg-indigo-700 transition"
              >
                + Thêm
              </button>
            </div>
          </div>

          {/* Danh sách SKU đã chọn */}
          {items.length > 0 && (
            <div className="border border-gray-200 rounded overflow-hidden">
              <table className="w-full text-left">
                <thead className="bg-gray-100 text-gray-600">
                  <tr>
                    <th className="p-2">Mã SKU</th>
                    <th className="p-2 text-center">Số lượng (theo đơn vị gốc)</th>
                    <th className="p-2 text-right">Xóa</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200">
                  {items.map((item, idx) => (
                    <tr key={item.skuId}>
                      <td className="p-2 font-medium">{item.skuCode}</td>
                      <td className="p-2 text-center">{item.quantity}</td>
                      <td className="p-2 text-right">
                        <button
                          type="button"
                          onClick={() => handleRemoveItem(idx)}
                          className="text-rose-600 hover:text-rose-800"
                        >
                          ✕
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Ghi chú */}
          <div>
            <label className="block text-gray-600 font-semibold mb-1">Ghi chú điều chuyển</label>
            <textarea
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="Ghi chú lý do chuyển kho, mã vận đơn..."
              className="w-full p-2 border border-gray-300 rounded focus:ring-1 focus:ring-indigo-500 bg-white"
              rows={2}
            />
          </div>

          <div className="flex justify-end gap-2 pt-2 border-t">
            <button
              type="button"
              onClick={onClose}
              disabled={loading}
              className="px-4 py-2 border border-gray-300 rounded text-gray-600 hover:bg-gray-50"
            >
              Hủy
            </button>
            <button
              type="submit"
              disabled={loading}
              className="px-4 py-2 bg-indigo-600 text-white font-medium rounded hover:bg-indigo-700 disabled:opacity-50"
            >
              {loading ? 'Đang tạo...' : 'Tạo phiếu chuyển'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
