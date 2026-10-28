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

  // Bước 1: Xuất gửi hàng (Draft -> InTransit)
  const handleDispatch = async () => {
    if (!detail || detail.id !== transferId || loading || actionLoading) return;
    const targetId = detail.id;
    const actionSeq = ++activeReqRef.current;
    setActionLoading(true);
    try {
      await stockTransferService.dispatchTransfer(targetId);
      if (actionSeq !== activeReqRef.current || transferId !== targetId) return;
      const updated = await stockTransferService.getTransferById(targetId);
      if (actionSeq !== activeReqRef.current || transferId !== targetId) return;
      setDetail(updated);
      onRefresh();
    } catch (error: any) {
      if (actionSeq !== activeReqRef.current || transferId !== targetId) return;
      const msg = error?.response?.data?.detail || error?.message || 'Không thể xuất gửi hàng.';
      alert(msg);
    } finally {
      if (actionSeq === activeReqRef.current && transferId === targetId) {
        setActionLoading(false);
      }
    }
  };

  // Bước 2: Nhận hàng (InTransit -> Received)
  const handleReceive = async () => {
    if (!detail || detail.id !== transferId || loading || actionLoading) return;
    const targetId = detail.id;
    const actionSeq = ++activeReqRef.current;
    setActionLoading(true);
    try {
      await stockTransferService.receiveTransfer(targetId);
      if (actionSeq !== activeReqRef.current || transferId !== targetId) return;
      const updated = await stockTransferService.getTransferById(targetId);
      if (actionSeq !== activeReqRef.current || transferId !== targetId) return;
      setDetail(updated);
      onRefresh();
    } catch (error: any) {
      if (actionSeq !== activeReqRef.current || transferId !== targetId) return;
      const msg = error?.response?.data?.detail || error?.message || 'Không thể nhận hàng.';
      alert(msg);
    } finally {
      if (actionSeq === activeReqRef.current && transferId === targetId) {
        setActionLoading(false);
      }
    }
  };

  // Hủy phiếu chuyển kho khi còn ở Draft (Draft -> Cancelled)
  const handleCancel = async () => {
    if (!detail || detail.id !== transferId || loading || actionLoading) return;
    const reason = prompt('Nhập lý do hủy phiếu chuyển kho:');
    if (reason === null) return;

    const targetId = detail.id;
    const actionSeq = ++activeReqRef.current;
    setActionLoading(true);
    try {
      await stockTransferService.cancelTransfer(targetId, reason);
      if (actionSeq !== activeReqRef.current || transferId !== targetId) return;
      const updated = await stockTransferService.getTransferById(targetId);
      if (actionSeq !== activeReqRef.current || transferId !== targetId) return;
      setDetail(updated);
      onRefresh();
    } catch (error: any) {
      if (actionSeq !== activeReqRef.current || transferId !== targetId) return;
      const msg = error?.response?.data?.detail || error?.message || 'Không thể hủy phiếu chuyển kho.';
      alert(msg);
    } finally {
      if (actionSeq === activeReqRef.current && transferId === targetId) {
        setActionLoading(false);
      }
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
                  <span className="text-gray-500 font-semibold">{TRANSFER_STATUS_LABEL[detail.status] || detail.status}</span>
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

        {/* Actions theo Máy trạng thái 2 Bước (Draft -> InTransit -> Received hoặc Draft -> Cancelled) */}
        {detail && (
          <div className="p-4 border-t border-gray-200 bg-gray-50 flex gap-2 justify-end">
            {detail.status === 'Draft' && (
              <>
                <button
                  disabled={actionLoading}
                  onClick={handleCancel}
                  className="px-4 py-2 text-xs font-semibold text-rose-700 bg-rose-50 border border-rose-200 rounded-md hover:bg-rose-100 disabled:opacity-50"
                >
                  Hủy phiếu (Cancel)
                </button>
                <button
                  disabled={actionLoading}
                  onClick={handleDispatch}
                  className="px-4 py-2 text-xs font-semibold text-white bg-indigo-600 rounded-md hover:bg-indigo-700 disabled:opacity-50"
                >
                  Xuất gửi hàng (Dispatch)
                </button>
              </>
            )}

            {detail.status === 'InTransit' && (
              <button
                disabled={actionLoading}
                onClick={handleReceive}
                className="px-4 py-2 text-xs font-semibold text-white bg-emerald-600 rounded-md hover:bg-emerald-700 disabled:opacity-50"
              >
                Xác nhận Nhận hàng (Receive)
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  );
};
