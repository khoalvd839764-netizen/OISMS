import React from 'react';
import type { OrderFilter, OrderStatus, OrderChannel } from '../../types/ordersCenter';
import { ORDER_STATUSES, ORDER_CHANNELS, ORDER_STATUS_LABEL } from '../../types/ordersCenter';

interface OrdersFilterProps {
  filter: OrderFilter;
  onChange: (newFilter: OrderFilter) => void;
  onReset: () => void;
}

export const OrdersFilter: React.FC<OrdersFilterProps> = ({ filter, onChange, onReset }) => {
  const handleChange = (key: keyof OrderFilter, value: unknown) => {
    onChange({
      ...filter,
      [key]: value === '' ? undefined : value,
      pageIndex: 1, // Reset về trang 1 khi thay đổi bộ lọc
    });
  };

  return (
    <div className="bg-white p-4 rounded-lg border border-gray-200 shadow-sm mb-4">
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-3">
        {/* Tìm kiếm */}
        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">Tìm kiếm</label>
          <input
            type="text"
            placeholder="Mã đơn, SĐT..."
            value={filter.search || ''}
            onChange={(e) => handleChange('search', e.target.value)}
            className="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
        </div>

        {/* Trạng thái */}
        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">Trạng thái</label>
          <select
            value={filter.status || ''}
            onChange={(e) => handleChange('status', e.target.value as OrderStatus)}
            className="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-indigo-500"
          >
            <option value="">Tất cả trạng thái</option>
            {ORDER_STATUSES.map((st) => (
              <option key={st} value={st}>
                {ORDER_STATUS_LABEL[st]}
              </option>
            ))}
          </select>
        </div>

        {/* Kênh bán */}
        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">Kênh bán</label>
          <select
            value={filter.channel || ''}
            onChange={(e) => handleChange('channel', e.target.value as OrderChannel)}
            className="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-indigo-500"
          >
            <option value="">Tất cả kênh</option>
            {ORDER_CHANNELS.map((ch) => (
              <option key={ch} value={ch}>
                {ch}
              </option>
            ))}
          </select>
        </div>

        {/* Từ ngày */}
        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">Từ ngày</label>
          <input
            type="date"
            value={filter.fromDate || ''}
            onChange={(e) => handleChange('fromDate', e.target.value)}
            className="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
        </div>

        {/* Nút Đặt lại */}
        <div className="flex items-end">
          <button
            onClick={onReset}
            className="w-full px-3 py-1.5 text-sm font-medium text-gray-600 bg-gray-100 hover:bg-gray-200 rounded-md transition-colors"
          >
            Đặt lại bộ lọc
          </button>
        </div>
      </div>
    </div>
  );
};