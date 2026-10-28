import React, { useState, useEffect, useCallback, useRef } from 'react';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import type { StockTransfer } from '../types/stockTransfer';
import { stockTransferService } from '../services/stockTransferService';
import { StockTransferTable } from '../components/transfers/StockTransferTable';
import { TransferDetailDrawer } from '../components/transfers/TransferDetailDrawer';
import { CreateTransferModal } from '../components/transfers/CreateTransferModal';

export const InventoryTransfersPage: React.FC = () => {
  const [transfers, setTransfers] = useState<StockTransfer[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selectedTransferId, setSelectedTransferId] = useState<string | null>(null);
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);

  // Pagination states (PR26-F04)
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize] = useState(20);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);

  // Sequence ref to prevent race condition
  const transferReqSeqRef = useRef(0);

  const loadTransfers = useCallback(async () => {
    const seq = ++transferReqSeqRef.current;
    setLoading(true);
    setError(null);
    try {
      const data = await stockTransferService.getTransfers({
        pageNumber,
        pageSize,
      });
      if (seq !== transferReqSeqRef.current) return;

      // PR26-F04: Nếu pageNumber vượt quá tổng số trang sau khi lọc/cập nhật, tự động lùi về trang 1
      if (data.totalPages > 0 && pageNumber > data.totalPages) {
        setPageNumber(1);
        return;
      }

      setTransfers(data.items);
      setTotalCount(data.totalCount);
      setTotalPages(Math.max(1, data.totalPages));
    } catch (err: any) {
      if (seq !== transferReqSeqRef.current) return;
      console.error('Lỗi nạp danh sách phiếu chuyển kho:', err);
      const msg = err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Không thể tải danh sách phiếu chuyển kho từ máy chủ.';
      setError(msg);
    } finally {
      if (seq === transferReqSeqRef.current) {
        setLoading(false);
      }
    }
  }, [pageNumber, pageSize]);

  useEffect(() => {
    loadTransfers();
  }, [loadTransfers]);

  return (
    <div className="p-6 max-w-7xl mx-auto space-y-4">
      {/* Header */}
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Điều Chuyển Kho Nội Bộ (2 Bước)</h1>
          <p className="text-sm text-gray-500">
            Quy trình chuyển kho chống thất thoát: Tạo nháp ➔ Xuất gửi (Dispatch) ➔ Xác nhận nhận / Từ chối (Receive/Reject)
          </p>
        </div>
        <div className="flex gap-2">
          <button
            onClick={loadTransfers}
            className="px-3 py-1.5 text-xs font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 shadow-sm"
          >
            🔄 Đồng bộ
          </button>
          <button
            onClick={() => setIsCreateModalOpen(true)}
            className="px-3 py-1.5 text-xs font-semibold text-white bg-indigo-600 rounded-md hover:bg-indigo-700 shadow-sm"
          >
            + Tạo phiếu điều chuyển
          </button>
        </div>
      </div>

      {error && (
        <div className="p-4 bg-rose-50 border border-rose-200 rounded-lg flex items-center justify-between text-sm text-rose-700">
          <div className="flex items-center gap-2">
            <span>⚠️ {error}</span>
          </div>
          <button
            onClick={loadTransfers}
            className="px-3 py-1 bg-rose-600 text-white rounded hover:bg-rose-700 text-xs font-semibold"
          >
            Thử lại
          </button>
        </div>
      )}

      {/* Bảng phiếu điều chuyển */}
      <StockTransferTable
        transfers={transfers}
        loading={loading}
        onSelectTransfer={(id) => setSelectedTransferId(id)}
      />

      {/* Điều khiển phân trang (PR26-F04) */}
      <div className="flex flex-col sm:flex-row items-center justify-between gap-3 px-4 py-3 bg-white border border-gray-200 rounded-xl shadow-sm text-xs text-gray-600">
        <div>
          Hiển thị{' '}
          <span className="font-semibold text-gray-900">
            {transfers.length > 0 ? (pageNumber - 1) * pageSize + 1 : 0}
          </span>{' '}
          -{' '}
          <span className="font-semibold text-gray-900">
            {Math.min(pageNumber * pageSize, totalCount)}
          </span>{' '}
          trong tổng số <span className="font-semibold text-gray-900">{totalCount}</span> phiếu điều chuyển
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

      {/* Drawer Chi tiết & Thao tác 2 Bước */}
      <TransferDetailDrawer
        transferId={selectedTransferId}
        onClose={() => setSelectedTransferId(null)}
        onRefresh={loadTransfers}
      />

      {/* Modal Tạo Phiếu Chuyển */}
      <CreateTransferModal
        isOpen={isCreateModalOpen}
        onClose={() => setIsCreateModalOpen(false)}
        onSuccess={loadTransfers}
      />
    </div>
  );
};

export default InventoryTransfersPage;