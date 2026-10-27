import React from 'react';
import type { OrderSummary, OrderStatus, OrderChannel } from '../types/ordersCenter';
import { ORDER_STATUS_LABEL } from '../types/ordersCenter';

interface OrdersTableProps {
  orders: OrderSummary[];
  loading: boolean;
  totalCount: number;
  pageIndex: number;
  pageSize: number;
  onPageChange: (newPage: number) => void;
  onSelectOrder: (orderId: string) => void;
}

const STATUS_COLOR: Record<OrderStatus, string> = {
  Draft: 'bg-gray-100 text-gray-700 border-gray-300',
  Reserved: 'bg-amber-50 text-amber-700 border-amber-200',
  Confirmed: 'bg-blue-50 text-blue-700 border-blue-200',
  Processing: 'bg-purple-50 text-purple-700 border-purple-200',
  Completed: 'bg-emerald-50 text-emerald-700 border-emerald-200',
  Cancelled: 'bg-rose-50 text-rose-700 border-rose-200',
};

const CHANNEL_COLOR: Record<OrderChannel, string> = {
  POS: 'bg-slate-800 text-white',
  Admin: 'bg-indigo-600 text-white',
  Shopee: 'bg-orange-500 text-white',
  TikTok: 'bg-black text-white',
  Lazada: 'bg-blue-600 text-white',
};

export const OrdersTable: React.FC<OrdersTableProps> = ({
  orders,
  loading,
  totalCount,
  pageIndex,
  pageSize,
  onPageChange,
  onSelectOrder,
}) => {
  const totalPages = Math.ceil(totalCount / pageSize) || 1;

  if (loading) {
    return (
      <div className="p-8 text-center text-gray-500 bg-white rounded-lg border border-gray-200 shadow-sm">
        Đang tải danh sách đơn hàng...
      </div>
    );
  }

  if (orders.length === 0) {
    return (
      <div className="p-8 text-center text-gray-500 bg-white rounded-lg border border-gray-200 shadow-sm">
        Không tìm thấy đơn hàng nào.
      </div>
    );
  }

  return (
    <div className="bg-white rounded-lg border border-gray-200 shadow-sm overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full text-sm text-left text-gray-600">
          <thead className="bg-gray-50 text-gray-700 font-semibold border-b border-gray-200">
            <tr>
              <th className="px-4 py-3">Mã đơn</th>
              <th className="px-4 py-3">Kênh Bán</th>
              <th className="px-4 py-3">Khách hàng</th>
              <th className="px-4 py-3">Số Lượng SKU</th>
              <th className="px-4 py-3">Tổng tiền</th>
              <th className="px-4 py-3">Trạng thái</th>
              <th className="px-4 py-3">Ngày tạo</th>
              <th className="px-4 py-3 text-right">Thao tác</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {orders.map((ord) => (
              <tr key={ord.id} className="hover:bg-gray-50 transition-colors">
                <td className="px-4 py-3 font-medium text-gray-900">{ord.number}</td>
                <td className="px-4 py-3">
                  <span className={`px-2 py-0.5 text-xs font-medium rounded-full ${CHANNEL_COLOR[ord.channel as OrderChannel] || 'bg-gray-500 text-white'}`}>
                    {ord.channel}
                  </span>
                </td>
                <td className="px-4 py-3">
                  <div>{ord.customerName || 'Khách lẻ'}</div>
                  {ord.customerPhone && <div className="text-xs text-gray-400">{ord.customerPhone}</div>}
                </td>
                <td className="px-4 py-3">{ord.itemCount} sản phẩm</td>
                <td className="px-4 py-3 font-semibold text-gray-900">
                  {ord.totalAmount.toLocaleString('vi-VN')} ₫
                </td>
                <td className="px-4 py-3">
                  <span className={`px-2.5 py-1 text-xs font-semibold rounded-md border ${STATUS_COLOR[ord.status]}`}>
                    {ORDER_STATUS_LABEL[ord.status] || ord.status}
                  </span>
                </td>
                <td className="px-4 py-3 text-xs text-gray-500">
                  {new Date(ord.createdAt).toLocaleString('vi-VN')}
                </td>
                <td className="px-4 py-3 text-right">
                  <button
                    onClick={() => onSelectOrder(ord.id)}
                    className="px-3 py-1 text-xs font-medium text-indigo-600 border border-indigo-200 rounded hover:bg-indigo-50 transition-colors"
                  >
                    Xem chi tiết
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Phân trang Server-side */}
      <div className="flex items-center justify-between px-4 py-3 bg-gray-50 border-t border-gray-200">
        <span className="text-xs text-gray-500">
          Hiển thị trang <b>{pageIndex}</b> / <b>{totalPages}</b> (Tổng <b>{totalCount}</b> đơn hàng)
        </span>
        <div className="flex gap-2">
          <button
            disabled={pageIndex <= 1}
            onClick={() => onPageChange(pageIndex - 1)}
            className="px-3 py-1 text-xs font-medium text-gray-600 bg-white border border-gray-300 rounded disabled:opacity-50 hover:bg-gray-100"
          >
            Trang trước
          </button>
          <button
            disabled={pageIndex >= totalPages}
            onClick={() => onPageChange(pageIndex + 1)}
            className="px-3 py-1 text-xs font-medium text-gray-600 bg-white border border-gray-300 rounded disabled:opacity-50 hover:bg-gray-100"
          >
            Trang sau
          </button>
        </div>
      </div>
    </div>
  );
};