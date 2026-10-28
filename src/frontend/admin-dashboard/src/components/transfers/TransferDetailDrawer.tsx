import React, { useState, useEffect, useRef } from 'react';
import type { StockTransfer } from '../../types/stockTransfer';
import { TRANSFER_STATUS_LABEL } from '../../types/stockTransfer';
import { stockTransferService } from '../../services/stockTransferService';

interface TransferDetailDrawerProps {
  transferId: string | null;
  onClose: () => void;
  onRefresh: () => void;
}

export const TransferDetailDrawer: React.FC<TransferDetailDrawerProps> = ({
  transferId,
  onClose,
  onRefresh,
}) => {
  const [detail, setDetail] = useState<StockTransfer | null>(null);
  const [loading, setLoading] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const [showRejectModal, setShowRejectModal] = useState(false);
  const activeReqRef = useRef(0);

  useEffect(() => {
    setDetail(null);
    if (!transferId) {
      setLoading(false);
      return;
    }

    const reqSeq = ++activeReqRef.current;
    setLoading(true);

    const fetchDetail = async () => {
      try {
        const data = await stockTransferService.getTransferById(transferId);
        if (reqSeq === activeReqRef.current) {
          setDetail(data);
        }
      } catch (error) {
        if (reqSeq === activeReqRef.current) {
          alert('Không thể tải chi tiết phiếu chuyển kho.');
        }
      } finally {
        if (reqSeq === activeReqRef.current) {
          setLoading(false);
        }
      }
    };

    fetchDetail();

    return () => {
      activeReqRef.current++;
    };
  }, [transferId]);

  if (!transferId) return null;

  // Bước 1 -> 2: Xuất gửi hàng (Dispatch)
  const handleDispatch = async () => {
    if (!detail || detail.id !== transferId || loading || actionLoading) return;
    setActionLoading(true);
    try {
      await stockTransferService.dispatchTransfer(detail.id);
      const updated = await stockTransferService.getTransferById(detail.id);
      setDetail(updated);
      onRefresh();
    } catch (error) {
      alert('Không thể xuất gửi hàng.');
    } finally {
      setActionLoading(false);
    }
  };

  // Bước 2 -> 3A: Nhận hàng (Receive)
  const handleReceive = async () => {
    if (!detail || detail.id !== transferId || loading || actionLoading) return;
    setActionLoading(true);
    try {
      await stockTransferService.receiveTransfer(detail.id);
      const updated = await stockTransferService.getTransferById(detail.id);
      setDetail(updated);
      onRefresh();
    } catch (error) {
      alert('Không thể nhận hàng.');
    } finally {
      setActionLoading(false);
    }
  };

  // Bước 2 -> 3B: Từ chối nhận hàng (Reject)
  const handleReject = async () => {
    if (!detail || detail.id !== transferId || loading || actionLoading) return;
    if (!rejectReason.trim()) {
      alert('Vui lòng nhập lý do từ chối.');
      return;
    }
    setActionLoading(true);
    try {
      await stockTransferService.rejectTransfer(detail.id, { reason: rejectReason });
      setShowRejectModal(false);
      const updated = await stockTransferService.getTransferById(detail.id);
      setDetail(updated);
      onRefresh();
    } catch (error) {
      alert('Không thể từ chối nhận hàng.');
    } finally {
      setActionLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 overflow-hidden bg-black/40 flex justify-end">
      <div className="w-full max-w-2xl bg-white h-full shadow-2xl flex flex-col">
        {/* Header */}
        <div className="p-4 border-b border-gray-200 flex justify-between items-center bg-gray-50">
          <div>
            <h2 className="text-lg font-bold text-gray-800">
              Phiếu điều chuyển {detail?.number || detail?.transferNumber || ''}
            </h2>
            {detail && (
              <span className="text-xs text-gray-500">
                Ngày tạo: {new Date(detail.createdAt).toLocaleString('vi-VN')}
              </span>
            )}
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 text-xl font-bold p-1"
          >
            ✕
          </button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-4 space-y-4">
          {loading ? (
            <div className="text-center py-12 text-gray-500">Đang tải chi tiết phiếu...</div>
          ) : detail ? (
            <>
              {/* Lộ trình Chuyển kho */}
              <div className="grid grid-cols-3 gap-3 bg-gray-50 p-3 rounded-lg border border-gray-200 text-xs">
                <div>
                  <span className="text-gray-400 block">Kho xuất (Nguồn)</span>
                  <strong className="text-sm text-indigo-700 block mt-0.5">
                    {detail.fromWarehouseName || detail.fromWarehouseId}
                  </strong>
                </div>
                <div className="text-center flex flex-col justify-center">
                  <span className="text-gray-400 font-bold">➔ ➔ ➔</span>
                  <span className="text-gray-500 font-semibold">{TRANSFER_STATUS_LABEL[detail.status]}</span>
                </div>
                <div className="text-right">
                  <span className="text-gray-400 block">Kho nhận (Đích)</span>
                  <strong className="text-sm text-emerald-700 block mt-0.5">
                    {detail.toWarehouseName || detail.toWarehouseId}
                  </strong>
                </div>
              </div>

              {detail.note && (
                <div className="text-xs bg-amber-50 text-amber-800 p-2.5 rounded border border-amber-200">
                  <b>Ghi chú:</b> {detail.note}
                </div>
              )}

              {/* Danh sách mặt hàng SKU */}
              <div>
                <h3 className="text-sm font-semibold text-gray-700 mb-2">Danh sách mặt hàng điều chuyển</h3>
                <div className="border border-gray-200 rounded-lg overflow-hidden">
                  <table className="w-full text-xs text-left">
                    <thead className="bg-gray-50 border-b border-gray-200 text-gray-600">
                      <tr>
                        <th className="p-2">Mã SKU</th>
                        <th className="p-2">Tên sản phẩm</th>
                        <th className="p-2 text-right">Số lượng chuyển</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {detail.items.map((item, idx) => (
                        <tr key={item.id || idx}>
                          <td className="p-2 font-medium text-gray-800">{item.skuCode}</td>
                          <td className="p-2 text-gray-500">{item.productName || item.skuName || '-'}</td>
                          <td className="p-2 text-right font-bold text-gray-900">{item.quantity}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            </>
          ) : null}
        </div>

        {/* Actions theo Máy trạng thái 2 Bước */}
        {detail && (
          <div className="p-4 border-t border-gray-200 bg-gray-50 flex gap-2 justify-end">
            {detail.status === 'Draft' && (
              <button
                disabled={actionLoading}
                onClick={handleDispatch}
                className="px-4 py-2 text-xs font-semibold text-white bg-indigo-600 rounded-md hover:bg-indigo-700 disabled:opacity-50"
              >
                Xuất gửi hàng (Dispatch)
              </button>
            )}

            {detail.status === 'InTransit' && (
              <>
                <button
                  disabled={actionLoading}
                  onClick={() => setShowRejectModal(true)}
                  className="px-4 py-2 text-xs font-semibold text-rose-700 bg-rose-50 border border-rose-200 rounded-md hover:bg-rose-100 disabled:opacity-50"
                >
                  Từ chối nhận (Reject)
                </button>
                <button
                  disabled={actionLoading}
                  onClick={handleReceive}
                  className="px-4 py-2 text-xs font-semibold text-white bg-emerald-600 rounded-md hover:bg-emerald-700 disabled:opacity-50"
                >
                  Xác nhận Nhận hàng (Receive)
                </button>
              </>
            )}
          </div>
        )}
      </div>

      {/* Modal từ chối nhận */}
      {showRejectModal && (
        <div className="fixed inset-0 z-60 bg-black/50 flex items-center justify-center p-4">
          <div className="bg-white rounded-lg p-5 max-w-md w-full space-y-3">
            <h3 className="font-bold text-gray-800">Từ chối nhận hàng điều chuyển</h3>
            <textarea
              placeholder="Nhập lý do từ chối (hư hỏng, sai lệch...)..."
              value={rejectReason}
              onChange={(e) => setRejectReason(e.target.value)}
              className="w-full p-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-rose-500"
              rows={3}
            />
            <div className="flex justify-end gap-2">
              <button
                onClick={() => setShowRejectModal(false)}
                className="px-3 py-1.5 text-xs text-gray-600 bg-gray-100 rounded"
              >
                Đóng
              </button>
              <button
                disabled={actionLoading}
                onClick={handleReject}
                className="px-3 py-1.5 text-xs text-white bg-rose-600 rounded disabled:opacity-50"
              >
                Xác nhận từ chối
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};