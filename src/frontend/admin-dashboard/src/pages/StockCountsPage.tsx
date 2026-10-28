import React, { useState, useEffect, useCallback, useRef } from 'react';
import type { StockCount, StockCountStatus } from '../types/stockCount';
import { stockCountService } from '../services/stockCountService';
import { StockCountsTable } from '../components/stockCounts/StockCountsTable';
import { CreateStockCountModal } from '../components/stockCounts/CreateStockCountModal';
import { StockCountDetailDrawer } from '../components/stockCounts/StockCountDetailDrawer';
import { apiAdapter } from '../services/apiAdapter';
import { Plus, RefreshCw, ClipboardCheck, Filter, ChevronLeft, ChevronRight } from 'lucide-react';

export const StockCountsPage: React.FC = () => {
  const [counts, setCounts] = useState<StockCount[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Pagination states (PR26-F04)
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize] = useState(20);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);

  // Sequence ref to prevent race condition when switching filters
  const countReqSeqRef = useRef(0);

  // Filters
  const [branches, setBranches] = useState<Array<{ id: string; name: string }>>([]);
  const [warehouses, setWarehouses] = useState<Array<{ id: string; name: string; branchId?: string }>>([]);
  const [selectedBranch, setSelectedBranch] = useState('');
  const [selectedWarehouse, setSelectedWarehouse] = useState('');
  const [selectedStatus, setSelectedStatus] = useState<StockCountStatus | ''>('');

  // Modals & Drawers
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [selectedCountId, setSelectedCountId] = useState<string | null>(null);

  const loadFilterOptions = async () => {
    try {
      const [brRes, whRes] = await Promise.all([
        apiAdapter.getBranches(),
        apiAdapter.getWarehouses(),
      ]);
      if (brRes.data) setBranches(brRes.data);
      if (whRes.data) setWarehouses(whRes.data);
    } catch (err) {
      console.warn('Lỗi nạp danh mục chi nhánh/kho:', err);
    }
  };

  const loadCounts = useCallback(async () => {
    const seq = ++countReqSeqRef.current;
    setLoading(true);
    setError(null);
    try {
      const data = await stockCountService.getStockCounts({
        branchId: selectedBranch || undefined,
        warehouseId: selectedWarehouse || undefined,
        status: selectedStatus || undefined,
        pageNumber,
        pageSize,
      });
      if (seq !== countReqSeqRef.current) return;

      // PR26-F04: Nếu pageNumber vượt quá tổng số trang sau khi lọc, tự động lùi về trang 1
      if (data.totalPages > 0 && pageNumber > data.totalPages) {
        setPageNumber(1);
        return;
      }

      setCounts(data.items);
      setTotalCount(data.totalCount);
      setTotalPages(Math.max(1, data.totalPages));
    } catch (err: any) {
      if (seq !== countReqSeqRef.current) return;
      console.error('Lỗi nạp danh sách kiểm kê kho:', err);
      const msg = err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Không thể tải danh sách kiểm kê kho từ máy chủ.';
      setError(msg);
    } finally {
      if (seq === countReqSeqRef.current) {
        setLoading(false);
      }
    }
  }, [selectedBranch, selectedWarehouse, selectedStatus, pageNumber, pageSize]);

  useEffect(() => {
    loadFilterOptions();
  }, []);

  useEffect(() => {
    loadCounts();
  }, [loadCounts]);

  const filteredWarehouses = warehouses.filter(
    (w) => !selectedBranch || !w.branchId || w.branchId === selectedBranch
  );

  return (
    <div className="p-6 max-w-7xl mx-auto space-y-5">
      {/* Page Header */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-3">
        <div>
          <div className="flex items-center gap-2">
            <ClipboardCheck className="w-7 h-7 text-indigo-600" />
            <h1 className="text-2xl font-bold text-gray-900">Kiểm Kê Kho & Cân Bằng Tồn</h1>
          </div>
          <p className="text-xs text-gray-500 mt-1">
            Chụp snapshot số dư sổ sách, nhập số lượng đếm thực tế, phát hiện Sequence Drift và tự động hạch toán cân bằng tồn kho.
          </p>
        </div>

        <div className="flex gap-2">
          <button
            onClick={loadCounts}
            className="px-3 py-2 text-xs font-semibold text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 shadow-sm flex items-center gap-1.5"
          >
            <RefreshCw className="w-3.5 h-3.5" />
            Đồng bộ
          </button>
          <button
            onClick={() => setIsCreateModalOpen(true)}
            className="px-4 py-2 text-xs font-semibold text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg shadow-sm flex items-center gap-1.5"
          >
            <Plus className="w-4 h-4" />
            Tạo đợt kiểm kê
          </button>
        </div>
      </div>

      {/* Filter Bar */}
      <div className="bg-white p-4 rounded-xl border border-gray-200 shadow-sm flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-1.5 text-xs font-semibold text-gray-600 uppercase">
          <Filter className="w-4 h-4 text-gray-400" />
          <span>Bộ lọc:</span>
        </div>

        {/* Chi nhánh */}
        <select
          value={selectedBranch}
          onChange={(e) => {
            setSelectedBranch(e.target.value);
            setSelectedWarehouse('');
            setPageNumber(1);
          }}
          className="border border-gray-300 rounded-lg px-3 py-1.5 text-xs bg-white focus:ring-1 focus:ring-indigo-500 outline-none"
        >
          <option value="">Tất cả chi nhánh</option>
          {branches.map((b) => (
            <option key={b.id} value={b.id}>
              {b.name}
            </option>
          ))}
        </select>

        {/* Kho */}
        <select
          value={selectedWarehouse}
          onChange={(e) => { setSelectedWarehouse(e.target.value); setPageNumber(1); }}
          className="border border-gray-300 rounded-lg px-3 py-1.5 text-xs bg-white focus:ring-1 focus:ring-indigo-500 outline-none"
        >
          <option value="">Tất cả kho hàng</option>
          {filteredWarehouses.map((w) => (
            <option key={w.id} value={w.id}>
              {w.name}
            </option>
          ))}
        </select>

        {/* Trạng thái */}
        <select
          value={selectedStatus}
          onChange={(e) => { setSelectedStatus(e.target.value as StockCountStatus | ''); setPageNumber(1); }}
          className="border border-gray-300 rounded-lg px-3 py-1.5 text-xs bg-white focus:ring-1 focus:ring-indigo-500 outline-none"
        >
          <option value="">Tất cả trạng thái</option>
          <option value="Draft">Bản nháp</option>
          <option value="Counting">Đang kiểm đếm</option>
          <option value="Confirmed">Đã cân bằng (Hoàn tất)</option>
          <option value="Cancelled">Đã hủy</option>
        </select>

        {(selectedBranch || selectedWarehouse || selectedStatus) && (
          <button
            onClick={() => {
              setSelectedBranch('');
              setSelectedWarehouse('');
              setSelectedStatus('');
              setPageNumber(1);
            }}
            className="text-xs text-indigo-600 hover:underline ml-auto"
          >
            Xóa bộ lọc
          </button>
        )}
      </div>

      {error && (
        <div className="p-4 bg-rose-50 border border-rose-200 rounded-lg flex items-center justify-between text-sm text-rose-700">
          <div className="flex items-center gap-2">
            <span>⚠️ {error}</span>
          </div>
          <button
            onClick={loadCounts}
            className="px-3 py-1 bg-rose-600 text-white rounded hover:bg-rose-700 text-xs font-semibold"
          >
            Thử lại
          </button>
        </div>
      )}

      {/* Bảng danh sách đợt kiểm kê */}
      <StockCountsTable
        counts={counts}
        loading={loading}
        onSelectCount={(id) => setSelectedCountId(id)}
      />

      {/* Điều khiển phân trang (PR26-F04) */}
      <div className="flex flex-col sm:flex-row items-center justify-between gap-3 px-4 py-3 bg-white border border-gray-200 rounded-xl shadow-sm text-xs text-gray-600">
        <div>
          Hiển thị{' '}
          <span className="font-semibold text-gray-900">
            {counts.length > 0 ? (pageNumber - 1) * pageSize + 1 : 0}
          </span>{' '}
          -{' '}
          <span className="font-semibold text-gray-900">
            {Math.min(pageNumber * pageSize, totalCount)}
          </span>{' '}
          trong tổng số <span className="font-semibold text-gray-900">{totalCount}</span> đợt kiểm kê
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => setPageNumber((p) => Math.max(1, p - 1))}
            disabled={pageNumber <= 1 || loading}
            className="px-2.5 py-1.5 border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-1 font-medium"
          >
            <ChevronLeft className="w-3.5 h-3.5" /> Trước
          </button>
          <span className="font-medium text-gray-800">
            Trang {pageNumber} / {Math.max(1, totalPages)}
          </span>
          <button
            onClick={() => setPageNumber((p) => (p < totalPages ? p + 1 : p))}
            disabled={pageNumber >= totalPages || loading}
            className="px-2.5 py-1.5 border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-1 font-medium"
          >
            Sau <ChevronRight className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Modal Khởi tạo */}
      <CreateStockCountModal
        isOpen={isCreateModalOpen}
        onClose={() => setIsCreateModalOpen(false)}
        onSuccess={loadCounts}
      />

      {/* Drawer Chi tiết & Kiểm đếm */}
      <StockCountDetailDrawer
        countId={selectedCountId}
        onClose={() => setSelectedCountId(null)}
        onRefresh={loadCounts}
      />
    </div>
  );
};

export default StockCountsPage;
