import React, { useState, useEffect } from 'react';
import type { CreateStockTransferRequest } from '../../types/stockTransfer';
import { stockTransferService } from '../../services/stockTransferService';
import { apiAdapter } from '../../services/apiAdapter';
import type { Sku } from '../../types/domain';

interface CreateTransferModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

export const CreateTransferModal: React.FC<CreateTransferModalProps> = ({
  isOpen,
  onClose,
  onSuccess,
}) => {
  const [warehouses, setWarehouses] = useState<Array<{ id: string; name: string }>>([]);
  const [skus, setSkus] = useState<Sku[]>([]);
  
  const [fromWarehouseId, setFromWarehouseId] = useState('');
  const [toWarehouseId, setToWarehouseId] = useState('');
  const [note, setNote] = useState('');
  const [selectedSkuId, setSelectedSkuId] = useState('');
  const [quantity, setQuantity] = useState(1);
  
  const [items, setItems] = useState<Array<{ skuId: string; skuCode: string; quantity: number }>>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

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

    setLoading(true);
    setError(null);

    const payload: CreateStockTransferRequest = {
      fromWarehouseId,
      toWarehouseId,
      note,
      items: items.map((i) => ({ skuId: i.skuId, quantity: i.quantity })),
    };

    try {
      await stockTransferService.createTransfer(payload);
      onSuccess();
      onClose();
    } catch (err) {
      setError('Không thể tạo phiếu chuyển kho. Vui lòng thử lại.');
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
              <label className="block font-medium text-gray-700 mb-1">Kho xuất (Nguồn) *</label>
              <select
                value={fromWarehouseId}
                onChange={(e) => setFromWarehouseId(e.target.value)}
                className="w-full p-2 border border-gray-300 rounded focus:ring-1 focus:ring-indigo-500"
              >
                {warehouses.map((w) => (
                  <option key={w.id} value={w.id}>{w.name}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="block font-medium text-gray-700 mb-1">Kho nhận (Đích) *</label>
              <select
                value={toWarehouseId}
                onChange={(e) => setToWarehouseId(e.target.value)}
                className="w-full p-2 border border-gray-300 rounded focus:ring-1 focus:ring-indigo-500"
              >
                {warehouses.map((w) => (
                  <option key={w.id} value={w.id}>{w.name}</option>
                ))}
              </select>
            </div>
          </div>

          <div>
            <label className="block font-medium text-gray-700 mb-1">Ghi chú điều chuyển</label>
            <input
              type="text"
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="Ví dụ: Chuyển hàng hỗ trợ chi nhánh..."
              className="w-full p-2 border border-gray-300 rounded"
            />
          </div>

          {/* Chọn SKU & SL */}
          <div className="border p-3 rounded-lg bg-gray-50 space-y-2">
            <span className="font-semibold text-gray-700 block">Thêm mặt hàng SKU</span>
            <div className="flex gap-2">
              <select
                value={selectedSkuId}
                onChange={(e) => setSelectedSkuId(e.target.value)}
                className="flex-1 p-2 border border-gray-300 rounded"
              >
                {skus.map((s) => (
                  <option key={s.id} value={s.id}>{s.code} - {s.name}</option>
                ))}
              </select>
              <input
                type="number"
                min="1"
                value={quantity}
                onChange={(e) => setQuantity(Math.max(1, Number(e.target.value)))}
                className="w-20 p-2 border border-gray-300 rounded text-center"
              />
              <button
                type="button"
                onClick={handleAddItem}
                className="px-3 py-2 bg-indigo-600 text-white font-medium rounded hover:bg-indigo-700"
              >
                + Thêm
              </button>
            </div>
          </div>

          {/* Danh sách SKU đã chọn */}
          {items.length > 0 && (
            <div className="border rounded overflow-hidden">
              <table className="w-full text-left">
                <thead className="bg-gray-100 border-b">
                  <tr>
                    <th className="p-2">Mã SKU</th>
                    <th className="p-2 text-center">Số lượng</th>
                    <th className="p-2 text-right">Thao tác</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {items.map((item, idx) => (
                    <tr key={idx}>
                      <td className="p-2 font-medium">{item.skuCode}</td>
                      <td className="p-2 text-center">{item.quantity}</td>
                      <td className="p-2 text-right">
                        <button
                          type="button"
                          onClick={() => handleRemoveItem(idx)}
                          className="text-rose-600 font-bold hover:underline"
                        >
                          Xóa
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Modal Actions */}
          <div className="flex justify-end gap-2 pt-2 border-t">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-gray-600 bg-gray-100 rounded hover:bg-gray-200"
            >
              Hủy
            </button>
            <button
              type="submit"
              disabled={loading}
              className="px-4 py-2 text-white bg-indigo-600 rounded hover:bg-indigo-700 disabled:opacity-50"
            >
              {loading ? 'Đang tạo...' : 'Tạo phiếu nháp (Draft)'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};