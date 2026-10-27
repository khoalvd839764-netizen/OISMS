import axios from 'axios';
import type {
  OrderFilter,
  Paged,
  OrderSummary,
  OrderDetail,
  CancelOrderBody,
} from '../types/ordersCenter';
import {
  normalizePaged,
  normalizeSummary,
  normalizeDetail,
} from '../types/ordersCenter';

// Cấu hình axios cơ bản (có thể tích hợp vào axios instance chung của dự án nếu có)
const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api',
  headers: {
    'Content-Type': 'application/json',
  },
});

export const orderService = {
  /**
   * Lấy danh sách đơn hàng có phân trang và bộ lọc
   */
  async getOrders(filter: OrderFilter): Promise<Paged<OrderSummary>> {
    const response = await apiClient.get('/orders', { params: filter });
    return normalizePaged(response.data, normalizeSummary);
  },

  /**
   * Lấy chi tiết đơn hàng
   */
  async getOrderById(id: string): Promise<OrderDetail> {
    const response = await apiClient.get(`/orders/${id}`);
    return normalizeDetail(response.data);
  },

  /**
   * Duyệt đơn trực tuyến (Trừ tồn kho thật on_hand)
   */
  async confirmOrder(id: string): Promise<void> {
    await apiClient.post(`/orders/${id}/confirm`);
  },

  /**
   * Hủy đơn trực tuyến (Hoàn trả tồn giữ chỗ)
   */
  async cancelOrder(id: string, body: CancelOrderBody): Promise<void> {
    await apiClient.post(`/orders/${id}/cancel`, body);
  },

  /**
   * Đóng gói / Xử lý đơn hàng
   */
  async processOrder(id: string): Promise<void> {
    await apiClient.post(`/orders/${id}/process`);
  },

  /**
   * Hoàn tất đơn hàng
   */
  async completeOrder(id: string): Promise<void> {
    await apiClient.post(`/orders/${id}/complete`);
  }
};