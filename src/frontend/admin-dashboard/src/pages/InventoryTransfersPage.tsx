import React, { useState, useEffect, useCallback } from 'react';
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

  const loadTransfers = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await stockTransferService.getTransfers();
      setTransfers(data);
    } catch (err: any) {
      console.error('Lỗi nạp danh sách phiếu chuyển kho:', err);
      const msg = err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Không thể tải danh sách phiếu chuyển kho từ máy chủ.';
      setError(msg);
    } finally {
      setLoading(false);
    }
  }, []);

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