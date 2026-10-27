import React from 'react';
import type { StockTransfer, TransferStatus } from '../../types/stockTransfer';
import { TRANSFER_STATUS_LABEL } from '../../types/stockTransfer';

interface StockTransferTableProps {
  transfers: StockTransfer[];
  loading: boolean;
  onSelectTransfer: (id: string) => void;
}

const STATUS_COLOR: Record<TransferStatus, string> = {
  Draft: 'bg-gray-100 text-gray-700 border-gray-300',
  InTransit: 'bg-amber-50 text-amber-700 border-amber-200',
  Received: 'bg-emerald-50 text-emerald-700 border-emerald-200',
  Rejected: 'bg-rose-50 text-rose-700 border-rose-200',
  Cancelled: 'bg-slate-100 text-slate-500 border-slate-200',
};

export const StockTransferTable: React.FC<StockTransferTableProps> = ({
  transfers,
  loading,
  onSelectTransfer,
}) => {
  if (loading) {
    return (
      <div className="p-8 text-center text-gray-500 bg-white rounded-lg border border-gray-200 shadow-sm">
        Đang tải danh sách phiếu chuyển kho...
      </div>
    );
  }

  if (transfers.length === 0) {
    return (
      <div className="p-8 text-center text-gray-500 bg-white rounded-lg border border-gray-200 shadow-sm">
        Chưa có phiếu chuyển kho nào.
      </div>
    );
  }

  return (
    <div className="bg-white rounded-lg border border-gray-200 shadow-sm overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full text-sm text-left text-gray-600">
          <thead className="bg-gray-50 text-gray-700 font-semibold border-b border-gray-200">
            <tr>
              <th className="px-4 py-3">Mã phiếu</th>
              <th className="px-4 py-3">Kho xuất (Nguồn)</th>
              <th className="px-4 py-3">Kho nhận (Đích)</th>
              <th className="px-4 py-3">Số lượng mặt hàng</th>
              <th className="px-4 py-3">Trạng thái</th>
              <th className="px-4 py-3">Ngày tạo</th>
              <th className="px-4 py-3 text-right">Thao tác</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {transfers.map((item) => (
              <tr key={item.id} className="hover:bg-gray-50 transition-colors">
                <td className="px-4 py-3 font-medium text-gray-900">{item.transferNumber}</td>
                <td className="px-4 py-3 font-medium text-indigo-600">
                  {item.fromWarehouseName || item.fromWarehouseId}
                </td>
                <td className="px-4 py-3 font-medium text-emerald-600">
                  {item.toWarehouseName || item.toWarehouseId}
                </td>
                <td className="px-4 py-3">{item.items?.length || 0} SKU</td>
                <td className="px-4 py-3">
                  <span className={`px-2.5 py-1 text-xs font-semibold rounded-md border ${STATUS_COLOR[item.status]}`}>
                    {TRANSFER_STATUS_LABEL[item.status] || item.status}
                  </span>
                </td>
                <td className="px-4 py-3 text-xs text-gray-500">
                  {new Date(item.createdAt).toLocaleString('vi-VN')}
                </td>
                <td className="px-4 py-3 text-right">
                  <button
                    onClick={() => onSelectTransfer(item.id)}
                    className="px-3 py-1 text-xs font-medium text-indigo-600 border border-indigo-200 rounded hover:bg-indigo-50 transition-colors"
                  >
                    Chi tiết & Xử lý
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
};