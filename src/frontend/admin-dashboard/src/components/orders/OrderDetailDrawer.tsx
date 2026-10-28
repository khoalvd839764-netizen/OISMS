import React, { useState, useEffect, useRef, useCallback } from 'react';
import type { OrderDetail } from '../../types/ordersCenter';
import { ORDER_STATUS_LABEL, ORDER_ACTIONS } from '../../types/ordersCenter';
import { orderService } from '../../services/orderService';
import { Printer } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { ComingSoonBadge } from '../ui';

interface OrderDetailDrawerProps {
  orderId: string | null;
  onClose: () => void;
  onRefresh: () => void;
}

export const OrderDetailDrawer: React.FC<OrderDetailDrawerProps> = ({
  orderId,
  onClose,
  onRefresh,
}) => {
  const { account } = useAuth();
  const isOwner = account?.role === 'Owner';
  const [detail, setDetail] = useState<OrderDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);
  const [cancelReason, setCancelReason] = useState('');
  const [showCancelModal, setShowCancelModal] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const activeReqRef = useRef(0);

  const fetchDetail = useCallback(async (targetOrderId: string) => {
    const reqSeq = ++activeReqRef.current;
    setDetail(null);
    setLoading(true);
    setError(null);

    try {
      const data = await orderService.getOrderById(targetOrderId);
      if (reqSeq !== activeReqRef.current || orderId !== targetOrderId) return;
      setDetail(data);
    } catch (err: any) {
      if (reqSeq !== activeReqRef.current || orderId !== targetOrderId) return;
      const msg = err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Không thể tải chi tiết đơn hàng.';
      setError(msg);
    } finally {
      if (reqSeq === activeReqRef.current && orderId === targetOrderId) {
        setLoading(false);
      }
    }
  }, [orderId]);

  useEffect(() => {
    // B01-F04: Luôn tăng sequence generation khi orderId đổi (kể cả khi orderId = null)
    // nhằm vô hiệu hóa ngay lập tức mọi phản hồi muộn / mutation đang chạy từ trước
    const currentSeq = ++activeReqRef.current;
    setShowCancelModal(false);
    setCancelReason('');
    setError(null);
    setDetail(null);
    setLoading(false);
    setActionLoading(false);

    if (!orderId) {
      return;
    }

    setLoading(true);
    orderService.getOrderById(orderId)
      .then((data) => {
        if (currentSeq !== activeReqRef.current || orderId !== orderId) return;
        setDetail(data);
      })
      .catch((err) => {
        if (currentSeq !== activeReqRef.current || orderId !== orderId) return;
        const msg = err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Không thể tải chi tiết đơn hàng.';
        setError(msg);
      })
      .finally(() => {
        if (currentSeq === activeReqRef.current) {
          setLoading(false);
        }
      });
  }, [orderId]);

  if (!orderId) return null;

  const handleAction = async (action: 'confirm' | 'process' | 'complete') => {
    // Identity guard: verify detail is loaded and strictly matches current orderId (B01-F04)
    if (!detail || detail.id !== orderId || loading || actionLoading) return;
    const targetOrderId = detail.id;
    const actionSeq = ++activeReqRef.current;
    setActionLoading(true);
    setError(null);
    try {
      if (action === 'confirm') await orderService.confirmOrder(targetOrderId);
      if (action === 'process') await orderService.processOrder(targetOrderId);
      if (action === 'complete') await orderService.completeOrder(targetOrderId);

      // B01-F04: Kiểm tra sequence và identity sau mutation trước khi gọi getOrderById
      if (actionSeq !== activeReqRef.current || orderId !== targetOrderId) return;
      
      const updated = await orderService.getOrderById(targetOrderId);
      if (actionSeq !== activeReqRef.current || orderId !== targetOrderId) return;

      setDetail(updated);
      onRefresh();
    } catch (err: any) {
      if (actionSeq !== activeReqRef.current || orderId !== targetOrderId) return;
      const msg = err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Thao tác thất bại.';
      alert(`Thao tác thất bại: ${msg}`);
      try {
        const refreshed = await orderService.getOrderById(targetOrderId);
        if (actionSeq !== activeReqRef.current || orderId !== targetOrderId) return;
        setDetail(refreshed);
      } catch {
        // ignore
      }
    } finally {
      if (actionSeq === activeReqRef.current && orderId === targetOrderId) {
        setActionLoading(false);
      }
    }
  };

  const handleCancel = async () => {
    // Identity guard: verify detail is loaded and strictly matches current orderId (B01-F04)
    if (!detail || detail.id !== orderId || loading || actionLoading) return;
    if (!cancelReason.trim()) {
      alert('Vui lòng nhập lý do hủy đơn.');
      return;
    }
    const targetOrderId = detail.id;
    const actionSeq = ++activeReqRef.current;
    setActionLoading(true);
    setError(null);
    try {
      await orderService.cancelOrder(targetOrderId, { reason: cancelReason.trim() });
      if (actionSeq !== activeReqRef.current || orderId !== targetOrderId) return;

      setShowCancelModal(false);
      setCancelReason('');
      const updated = await orderService.getOrderById(targetOrderId);
      if (actionSeq !== activeReqRef.current || orderId !== targetOrderId) return;

      setDetail(updated);
      onRefresh();
    } catch (err: any) {
      if (actionSeq !== activeReqRef.current || orderId !== targetOrderId) return;
      const msg = err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Không thể hủy đơn hàng.';
      alert(`Hủy đơn thất bại: ${msg}`);
      try {
        const refreshed = await orderService.getOrderById(targetOrderId);
        if (actionSeq !== activeReqRef.current || orderId !== targetOrderId) return;
        setDetail(refreshed);
      } catch {
        // ignore
      }
    } finally {
      if (actionSeq === activeReqRef.current && orderId === targetOrderId) {
        setActionLoading(false);
      }
    }
  };

  const actions = detail ? ORDER_ACTIONS[detail.status] || [] : [];

  return (
    <div className="fixed inset-0 z-50 overflow-hidden bg-black/40 flex justify-end print:static print:bg-transparent print:p-0">
      <div className="w-full max-w-2xl bg-white h-full shadow-2xl flex flex-col print:w-full print:max-w-none print:shadow-none print:h-auto">
        {/* Header Drawer */}
        <div className="p-4 border-b border-gray-200 flex justify-between items-center bg-gray-50">
          <div>
            <h2 className="text-lg font-bold text-gray-800">
              Chi tiết đơn hàng {detail?.number || ''}
            </h2>
            {detail && (
              <span className="text-xs text-gray-500">
                Kênh: <b className="text-gray-700">{detail.channel}</b> | Ngày tạo:{' '}
                {new Date(detail.createdAt).toLocaleString('vi-VN')}
              </span>
            )}
          </div>
          <div className="flex items-center gap-2 print:hidden">
            <button
              disabled
              className="px-2.5 py-1 text-xs font-semibold text-gray-700 bg-white border border-gray-300 rounded hover:bg-gray-100 flex items-center gap-1 shadow-sm"
              title="Bản in phiếu xuất kho / giao hàng đang hoàn thiện."
            >
              <Printer className="w-3.5 h-3.5" />
              In chứng từ <ComingSoonBadge />
            </button>
            <button
              onClick={onClose}
              className="text-gray-400 hover:text-gray-600 text-xl font-bold p-1"
            >
              ✕
            </button>
          </div>
        </div>

        {/* Nội dung chi tiết */}
        <div className="flex-1 overflow-y-auto p-4 space-y-4">
          {error && (
            <div className="p-4 bg-rose-50 border border-rose-200 rounded-lg flex items-center justify-between text-xs text-rose-700">
              <div className="flex items-center gap-1.5 font-medium">
                <span>⚠️ {error}</span>
              </div>
              <button
                onClick={() => {
                  if (orderId) {
                    fetchDetail(orderId);
                  }
                }}
                className="px-2.5 py-1 bg-rose-600 text-white rounded hover:bg-rose-700 font-semibold"
              >
                Thử lại
              </button>
            </div>
          )}

          {loading ? (
            <div className="text-center py-12 text-gray-500">Đang tải chi tiết đơn hàng...</div>
          ) : detail ? (
            <>
              {/* Thông tin khách hàng & Trạng thái */}
              <div className="grid grid-cols-2 gap-4 bg-gray-50 p-3 rounded-lg border border-gray-100 text-sm">
                <div>
                  <span className="text-xs text-gray-400 block">Khách hàng</span>
                  <p className="font-semibold text-gray-800">{detail.customerName || 'Khách lẻ'}</p>
                  <p className="text-xs text-gray-500">{detail.customerPhone || 'Chưa có SĐT'}</p>
                </div>
                <div>
                  <span className="text-xs text-gray-400 block">Trạng thái</span>
                  <span className="inline-block px-2.5 py-0.5 rounded text-xs font-bold bg-blue-100 text-blue-800 mt-1">
                    {ORDER_STATUS_LABEL[detail.status] || detail.status}
                  </span>
                </div>
              </div>

              {/* Bảng Mặt Hàng SKU */}
              <div>
                <h3 className="font-semibold text-gray-700 text-sm mb-2">Danh sách mặt hàng SKU</h3>
                <div className="border border-gray-200 rounded-lg overflow-hidden">
                  <table className="w-full text-left text-xs">
                    <thead className="bg-gray-100 text-gray-600 font-semibold border-b border-gray-200">
                      <tr>
                        <th className="p-2">Sản phẩm</th>
                        <th className="p-2 text-center">SL</th>
                        <th className="p-2 text-right">Giá niêm yết</th>
                        {isOwner && <th className="p-2 text-right print:hidden">Giá vốn (WAC)</th>}
                        <th className="p-2 text-right">Tổng tiền</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-200">
                      {detail.items?.map((item) => (
                        <tr key={item.id}>
                          <td className="p-2">
                            <div className="font-semibold text-gray-800">{item.skuCode}</div>
                            <div className="text-gray-500">{item.skuName}</div>
                          </td>
                          <td className="p-2 text-center font-medium">{item.quantity}</td>
                          <td className="p-2 text-right">
                            {item.unitPrice.toLocaleString('vi-VN')} ₫
                          </td>
                          {isOwner && (
                            <td className="p-2 text-right text-gray-500 print:hidden">
                              {item.costPrice !== null
                                ? `${item.costPrice.toLocaleString('vi-VN')} ₫`
                                : 'Chưa chốt'}
                            </td>
                          )}
                          <td className="p-2 text-right font-semibold">
                            {(item.quantity * item.unitPrice).toLocaleString('vi-VN')} ₫
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>

              {/* Tổng cộng & WAC */}
              <div className="bg-slate-50 p-3 rounded-lg border border-slate-200 text-sm space-y-1">
                <div className="flex justify-between">
                  <span className="text-gray-600">Tổng tiền bán:</span>
                  <span className="font-bold text-gray-900 text-base">
                    {detail.totalAmount.toLocaleString('vi-VN')} ₫
                  </span>
                </div>
                {isOwner && (
                  <div className="flex justify-between text-xs print:hidden">
                    <span className="text-gray-500">Tổng giá vốn snapshot (WAC):</span>
                    <span className="font-medium text-gray-700">
                      {detail.totalCogs !== null
                        ? `${detail.totalCogs.toLocaleString('vi-VN')} ₫`
                        : 'Chưa chốt giá vốn'}
                    </span>
                  </div>
                )}
              </div>
            </>
          ) : null}
        </div>

        {/* Footer chứa các Nút Thao Tác Nghiệp Vụ */}
        {detail && actions.length > 0 && (
          <div className="p-4 border-t border-gray-200 bg-gray-50 flex gap-2 justify-end print:hidden">
            {actions.includes('cancel') && (
              <button
                disabled={actionLoading}
                onClick={() => setShowCancelModal(true)}
                className="px-4 py-2 text-xs font-semibold text-rose-700 bg-rose-50 border border-rose-200 rounded-md hover:bg-rose-100 disabled:opacity-50"
              >
                Hủy đơn
              </button>
            )}
            {actions.includes('confirm') && (
              <button
                disabled={actionLoading}
                onClick={() => handleAction('confirm')}
                className="px-4 py-2 text-xs font-semibold text-white bg-indigo-600 rounded-md hover:bg-indigo-700 disabled:opacity-50"
              >
                Duyệt đơn (Trừ kho)
              </button>
            )}
            {actions.includes('process') && (
              <button
                disabled={actionLoading}
                onClick={() => handleAction('process')}
                className="px-4 py-2 text-xs font-semibold text-white bg-purple-600 rounded-md hover:bg-purple-700 disabled:opacity-50"
              >
                Xử lý / Đóng gói
              </button>
            )}
            {actions.includes('complete') && (
              <button
                disabled={actionLoading}
                onClick={() => handleAction('complete')}
                className="px-4 py-2 text-xs font-semibold text-white bg-emerald-600 rounded-md hover:bg-emerald-700 disabled:opacity-50"
              >
                Hoàn tất đơn
              </button>
            )}
          </div>
        )}
      </div>

      {/* Modal nhập lý do hủy đơn */}
      {showCancelModal && (
        <div className="fixed inset-0 z-60 bg-black/50 flex items-center justify-center p-4 print:hidden">
          <div className="bg-white rounded-lg p-5 max-w-md w-full space-y-3">
            <h3 className="font-bold text-gray-800">Xác nhận hủy đơn hàng</h3>
            <textarea
              placeholder="Nhập lý do hủy đơn..."
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              className="w-full p-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-rose-500"
              rows={3}
            />
            <div className="flex justify-end gap-2">
              <button
                onClick={() => setShowCancelModal(false)}
                className="px-3 py-1.5 text-xs text-gray-600 bg-gray-100 rounded"
              >
                Đóng
              </button>
              <button
                disabled={actionLoading}
                onClick={handleCancel}
                className="px-3 py-1.5 text-xs text-white bg-rose-600 rounded disabled:opacity-50"
              >
                Xác nhận hủy
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
