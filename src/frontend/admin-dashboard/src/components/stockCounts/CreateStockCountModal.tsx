import React, { useState, useEffect } from 'react';
import { apiAdapter } from '../../services/apiAdapter';
import { stockCountService } from '../../services/stockCountService';
import type { Sku } from '../../types/domain';
import { X, AlertCircle, Check } from 'lucide-react';

interface CreateStockCountModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

export const CreateStockCountModal: React.FC<CreateStockCountModalProps> = ({
  isOpen,
  onClose,
  onSuccess,
}) => {
  const [branches, setBranches] = useState<Array<{ id: string; name: string }>>([]);
  const [warehouses, setWarehouses] = useState<Array<{ id: string; name: string; branchId?: string }>>([]);
  const [skus, setSkus] = useState<Sku[]>([]);

  const [selectedBranchId, setSelectedBranchId] = useState('');
  const [selectedWarehouseId, setSelectedWarehouseId] = useState('');
  const [scopeMode, setScopeMode] = useState<'ALL' | 'PARTIAL'>('ALL');
  const [selectedSkuIds, setSelectedSkuIds] = useState<string[]>([]);
  const [note, setNote] = useState('');

  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!isOpen) return;

    const loadData = async () => {
      setLoading(true);
      setErrorMessage(null);
      try {
        const [brRes, whRes, skuRes] = await Promise.all([
          apiAdapter.getBranches(),
          apiAdapter.getWarehouses(),
          apiAdapter.getSkus(),
        ]);

        if (brRes.data && brRes.data.length > 0) {
          setBranches(brRes.data);
          setSelectedBranchId(brRes.data[0].id);
        }

        if (whRes.data && whRes.data.length > 0) {
          setWarehouses(whRes.data);
          const firstWh = whRes.data.find(
            (w: any) => !brRes.data[0]?.id || w.branchId === brRes.data[0]?.id
          ) || whRes.data[0];
          setSelectedWarehouseId(firstWh.id);
        }

        if (skuRes.data) {
          setSkus(skuRes.data);
        }
      } catch (err) {
        setErrorMessage('Không thể tải danh mục chi nhánh, kho hoặc sản phẩm.');
      } finally {
        setLoading(false);
      }
    };

    loadData();
  }, [isOpen]);

  // Khi đổi Chi nhánh, tự động lọc và chọn kho thuộc chi nhánh đó
  const handleBranchChange = (branchId: string) => {
    setSelectedBranchId(branchId);
    const availableWh = warehouses.filter((w) => !w.branchId || w.branchId === branchId);
    if (availableWh.length > 0) {
      setSelectedWarehouseId(availableWh[0].id);
    } else {
      setSelectedWarehouseId('');
    }
  };

  const toggleSkuSelection = (skuId: string) => {
    setSelectedSkuIds((prev) =>
      prev.includes(skuId) ? prev.filter((id) => id !== skuId) : [...prev, skuId]
    );
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage(null);

    if (!selectedBranchId) {
      setErrorMessage('Vui lòng chọn chi nhánh.');
      return;
    }
    if (!selectedWarehouseId) {
      setErrorMessage('Vui lòng chọn kho kiểm kê.');
      return;
    }
    if (scopeMode === 'PARTIAL' && selectedSkuIds.length === 0) {
      setErrorMessage('Vui lòng chọn ít nhất 1 mặt hàng SKU khi kiểm kê theo phạm vi nhóm.');
      return;
    }

    setSubmitting(true);
    try {
      await stockCountService.createStockCount({
        branchId: selectedBranchId,
        warehouseId: selectedWarehouseId,
        skuIds: scopeMode === 'PARTIAL' ? selectedSkuIds : null,
        note: note.trim() || undefined,
      });

      onSuccess();
      onClose();
    } catch (err: any) {
      setErrorMessage(err.detail || err.message || 'Lỗi khởi tạo đợt kiểm kê.');
    } finally {
      setSubmitting(false);
    }
  };

  if (!isOpen) return null;

  const filteredWarehouses = warehouses.filter(
    (w) => !w.branchId || w.branchId === selectedBranchId
  );

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <div className="bg-white rounded-xl shadow-2xl max-w-2xl w-full max-h-[90vh] flex flex-col overflow-hidden animate-in fade-in zoom-in-95 duration-150">
        {/* Header */}
        <div className="px-6 py-4 border-b border-gray-200 flex justify-between items-center bg-gray-50">
          <div>
            <h2 className="text-lg font-bold text-gray-900">Khởi Tạo Đợt Kiểm Kê Kho</h2>
            <p className="text-xs text-gray-500 mt-0.5">
              Hệ thống sẽ chụp snapshot số dư và sequence sổ kho tại thời điểm này.
            </p>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 p-1 rounded-lg hover:bg-gray-200 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Form Body */}
        <form onSubmit={handleSubmit} className="p-6 overflow-y-auto space-y-4 flex-1">
          {errorMessage && (
            <div className="p-3 bg-rose-50 border border-rose-200 rounded-lg flex items-start gap-2 text-rose-700 text-sm">
              <AlertCircle className="w-5 h-5 flex-shrink-0 mt-0.5" />
              <span>{errorMessage}</span>
            </div>
          )}

          {loading ? (
            <div className="py-12 text-center text-gray-500">Đang tải danh mục...</div>
          ) : (
            <>
              {/* Chi nhánh & Kho */}
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <label className="block text-xs font-semibold text-gray-700 uppercase mb-1">
                    Chi nhánh *
                  </label>
                  <select
                    value={selectedBranchId}
                    onChange={(e) => handleBranchChange(e.target.value)}
                    className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white focus:ring-2 focus:ring-indigo-500 outline-none"
                    required
                  >
                    {branches.map((b) => (
                      <option key={b.id} value={b.id}>
                        {b.name}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="block text-xs font-semibold text-gray-700 uppercase mb-1">
                    Kho kiểm kê *
                  </label>
                  <select
                    value={selectedWarehouseId}
                    onChange={(e) => setSelectedWarehouseId(e.target.value)}
                    className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white focus:ring-2 focus:ring-indigo-500 outline-none"
                    required
                  >
                    {filteredWarehouses.length === 0 ? (
                      <option value="">Không có kho khả dụng</option>
                    ) : (
                      filteredWarehouses.map((w) => (
                        <option key={w.id} value={w.id}>
                          {w.name}
                        </option>
                      ))
                    )}
                  </select>
                </div>
              </div>

              {/* Phạm vi kiểm kê */}
              <div>
                <label className="block text-xs font-semibold text-gray-700 uppercase mb-2">
                  Phạm vi kiểm kê
                </label>
                <div className="grid grid-cols-2 gap-3">
                  <button
                    type="button"
                    onClick={() => setScopeMode('ALL')}
                    className={`p-3 rounded-lg border text-left transition-all ${
                      scopeMode === 'ALL'
                        ? 'border-indigo-600 bg-indigo-50/50 ring-2 ring-indigo-500/20'
                        : 'border-gray-200 hover:bg-gray-50'
                    }`}
                  >
                    <div className="font-semibold text-sm text-gray-900">Toàn bộ kho (ALL)</div>
                    <div className="text-xs text-gray-500 mt-1">
                      Snapshot tất cả các mặt hàng hiện có số dư trong kho.
                    </div>
                  </button>

                  <button
                    type="button"
                    onClick={() => setScopeMode('PARTIAL')}
                    className={`p-3 rounded-lg border text-left transition-all ${
                      scopeMode === 'PARTIAL'
                        ? 'border-indigo-600 bg-indigo-50/50 ring-2 ring-indigo-500/20'
                        : 'border-gray-200 hover:bg-gray-50'
                    }`}
                  >
                    <div className="font-semibold text-sm text-gray-900">Nhóm mặt hàng (PARTIAL)</div>
                    <div className="text-xs text-gray-500 mt-1">
                      Chỉ chọn một số SKU cụ thể để đối soát và cân bằng.
                    </div>
                  </button>
                </div>
              </div>

              {/* Danh sách chọn SKU nếu chọn PARTIAL */}
              {scopeMode === 'PARTIAL' && (
                <div className="border border-gray-200 rounded-lg p-3 bg-gray-50">
                  <div className="flex justify-between items-center mb-2">
                    <span className="text-xs font-semibold text-gray-700 uppercase">
                      Chọn các mặt hàng kiểm đếm ({selectedSkuIds.length} đã chọn)
                    </span>
                    <button
                      type="button"
                      onClick={() =>
                        setSelectedSkuIds(
                          selectedSkuIds.length === skus.length ? [] : skus.map((s) => s.id)
                        )
                      }
                      className="text-xs text-indigo-600 hover:underline"
                    >
                      {selectedSkuIds.length === skus.length ? 'Bỏ chọn tất cả' : 'Chọn tất cả'}
                    </button>
                  </div>
                  <div className="max-h-44 overflow-y-auto space-y-1.5 border border-gray-200 rounded bg-white p-2">
                    {skus.map((sku) => {
                      const checked = selectedSkuIds.includes(sku.id);
                      return (
                        <div
                          key={sku.id}
                          onClick={() => toggleSkuSelection(sku.id)}
                          className={`flex items-center justify-between p-2 rounded cursor-pointer text-xs transition-colors ${
                            checked ? 'bg-indigo-50 text-indigo-900' : 'hover:bg-gray-100 text-gray-700'
                          }`}
                        >
                          <div>
                            <span className="font-mono font-medium">{sku.code}</span>
                            <span className="ml-2 text-gray-500">{sku.name}</span>
                          </div>
                          {checked && <Check className="w-4 h-4 text-indigo-600" />}
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}

              {/* Ghi chú */}
              <div>
                <label className="block text-xs font-semibold text-gray-700 uppercase mb-1">
                  Ghi chú đợt kiểm kê
                </label>
                <textarea
                  value={note}
                  onChange={(e) => setNote(e.target.value)}
                  placeholder="Ví dụ: Kiểm kê định kỳ cuối tháng, kiểm kê đột xuất kho đông lạnh..."
                  className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white focus:ring-2 focus:ring-indigo-500 outline-none"
                  rows={2}
                />
              </div>
            </>
          )}

          {/* Footer actions */}
          <div className="pt-4 border-t border-gray-200 flex justify-end gap-2">
            <button
              type="button"
              onClick={onClose}
              disabled={submitting}
              className="px-4 py-2 text-xs font-semibold text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50"
            >
              Hủy bỏ
            </button>
            <button
              type="submit"
              disabled={submitting || loading}
              className="px-5 py-2 text-xs font-semibold text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50 shadow-sm"
            >
              {submitting ? 'Đang khởi tạo...' : 'Tạo đợt kiểm kê'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
