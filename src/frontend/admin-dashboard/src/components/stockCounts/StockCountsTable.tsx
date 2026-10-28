import React from 'react';
import type { StockCount, StockCountStatus } from '../../types/stockCount';
import { STOCK_COUNT_STATUS_LABEL, STOCK_COUNT_STATUS_COLOR } from '../../types/stockCount';
import { Eye, Clock, CheckCircle2, AlertCircle, XCircle } from 'lucide-react';

interface StockCountsTableProps {
  counts: StockCount[];
  loading: boolean;
  onSelectCount: (id: string) => void;
}

export const StockCountsTable: React.FC<StockCountsTableProps> = ({
  counts,
  loading,
  onSelectCount,
}) => {
  if (loading) {
    return (
      <div className="bg-white rounded-lg border border-gray-200 p-12 text-center text-gray-500 shadow-sm">
        <div className="inline-block animate-spin rounded-full h-8 w-8 border-4 border-indigo-500 border-t-transparent mb-2"></div>
        <p className="text-sm">Đang tải danh sách đợt kiểm kê...</p>
      </div>
    );
  }

  if (counts.length === 0) {
    return (
      <div className="bg-white rounded-lg border border-gray-200 p-12 text-center text-gray-500 shadow-sm">
        <AlertCircle className="mx-auto h-12 w-12 text-gray-400 mb-3" />
        <p className="text-base font-semibold text-gray-700">Chưa có đợt kiểm kê nào</p>
        <p className="text-sm text-gray-500 mt-1">
          Bấm nút "+ Tạo đợt kiểm kê" phía trên để snapshot tồn kho và bắt đầu kiểm đếm.
        </p>
      </div>
    );
  }

  const getStatusIcon = (status: StockCountStatus) => {
    switch (status) {
      case 'Draft':
        return <Clock className="w-3.5 h-3.5 mr-1 text-gray-600" />;
      case 'Counting':
        return <Clock className="w-3.5 h-3.5 mr-1 text-amber-600 animate-pulse" />;
      case 'Confirmed':
        return <CheckCircle2 className="w-3.5 h-3.5 mr-1 text-emerald-600" />;
      case 'Cancelled':
        return <XCircle className="w-3.5 h-3.5 mr-1 text-rose-600" />;
    }
  };

  return (
    <div className="bg-white rounded-lg border border-gray-200 shadow-sm overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full text-left border-collapse text-sm">
          <thead>
            <tr className="bg-gray-50 border-b border-gray-200 text-gray-600 uppercase text-xs font-semibold">
              <th className="py-3 px-4">Mã kiểm kê</th>
              <th className="py-3 px-4">Chi nhánh / Kho</th>
              <th className="py-3 px-4 text-center">Trạng thái</th>
              <th className="py-3 px-4 text-right">Số SKU</th>
              <th className="py-3 px-4">Thời gian tạo</th>
              <th className="py-3 px-4">Thời gian chốt</th>
              <th className="py-3 px-4 text-center">Hành động</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {counts.map((count) => {
              const statusCfg = STOCK_COUNT_STATUS_COLOR[count.status] || {
                bg: 'bg-gray-100',
                text: 'text-gray-700',
                border: 'border-gray-200',
              };

              return (
                <tr
                  key={count.id}
                  className="hover:bg-indigo-50/40 transition-colors cursor-pointer"
                  onClick={() => onSelectCount(count.id)}
                >
                  <td className="py-3.5 px-4 font-semibold text-indigo-600">
                    {count.number || count.id.slice(0, 8)}
                  </td>
                  <td className="py-3.5 px-4">
                    <div className="font-medium text-gray-900">{count.warehouseName || 'Kho mặc định'}</div>
                    <div className="text-xs text-gray-500">{count.branchName || 'Chi nhánh'}</div>
                  </td>
                  <td className="py-3.5 px-4 text-center">
                    <span
                      className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium border ${statusCfg.bg} ${statusCfg.text} ${statusCfg.border}`}
                    >
                      {getStatusIcon(count.status)}
                      {STOCK_COUNT_STATUS_LABEL[count.status] || count.status}
                    </span>
                  </td>
                  <td className="py-3.5 px-4 text-right font-medium text-gray-800">
                    {count.items?.length || 0} SKU
                  </td>
                  <td className="py-3.5 px-4 text-gray-600 text-xs">
                    {count.createdAt
                      ? new Date(count.createdAt).toLocaleString('vi-VN', {
                          year: 'numeric',
                          month: '2-digit',
                          day: '2-digit',
                          hour: '2-digit',
                          minute: '2-digit',
                        })
                      : '—'}
                  </td>
                  <td className="py-3.5 px-4 text-gray-600 text-xs">
                    {count.confirmedAt
                      ? new Date(count.confirmedAt).toLocaleString('vi-VN', {
                          year: 'numeric',
                          month: '2-digit',
                          day: '2-digit',
                          hour: '2-digit',
                          minute: '2-digit',
                        })
                      : '—'}
                  </td>
                  <td className="py-3.5 px-4 text-center">
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        onSelectCount(count.id);
                      }}
                      className="inline-flex items-center gap-1 px-2.5 py-1 text-xs font-medium text-indigo-700 bg-indigo-50 hover:bg-indigo-100 rounded border border-indigo-200 transition-colors"
                    >
                      <Eye className="w-3.5 h-3.5" />
                      Chi tiết
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
};
