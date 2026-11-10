import React, { useState, useEffect, useRef } from 'react';
import {
  AlertCircle,
  Boxes,
  ExternalLink,
  GitBranch,
  Loader2,
  RotateCcw,
  Warehouse,
  X,
} from 'lucide-react';
import { Link } from 'react-router-dom';
import { Button, Badge } from '../ui';
import { useAuth } from '../../context/AuthContext';
import {
  salesReturnService,
  getSalesReturnErrorMessage,
} from '../../services/salesReturnService';
import { ConfirmSalesReturnDialog } from './ConfirmSalesReturnDialog';
import type { SalesReturn } from '../../types/salesReturns';
import { SALES_RETURN_STATUS_LABEL } from '../../types/salesReturns';

interface SalesReturnDetailDrawerProps {
  returnId: string | null;
  onClose: () => void;
  onRefresh: () => void;
  onOpenOrder?: (orderId: string) => void;
  onOpenTrace?: (orderId: string, orderNumber: string) => void;
}

export const SalesReturnDetailDrawer: React.FC<SalesReturnDetailDrawerProps> = ({
  returnId,
  onClose,
  onRefresh,
  onOpenOrder,
  onOpenTrace,
}) => {
  const { account } = useAuth();
  const canSeeCost = account?.role === 'Owner' || account?.role === 'Staff';

  const [detail, setDetail] = useState<SalesReturn | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Dialog action state
  const [dialogState, setDialogState] = useState<{
    isOpen: boolean;
    type: 'confirm' | 'cancel';
  }>({ isOpen: false, type: 'confirm' });
  const [actionLoading, setActionLoading] = useState(false);
  const busyIdsRef = useRef<Set<string>>(new Set());

  // Load detail
  useEffect(() => {
    if (!returnId) {
      setDetail(null);
      setError(null);
      return;
    }

    let isMounted = true;
    setLoading(true);
    setError(null);

    salesReturnService
      .getById(returnId)
      .then((data) => {
        if (isMounted) setDetail(data);
      })
      .catch((err) => {
        if (isMounted) {
          const parsed = getSalesReturnErrorMessage(err);
          setError(parsed.message || 'Không thể tải thông tin chi tiết phiếu trả hàng.');
        }
      })
      .finally(() => {
        if (isMounted) setLoading(false);
      });

    return () => {
      isMounted = false;
    };
  }, [returnId]);

  if (!returnId) return null;

  // Xử lý xác nhận hoặc hủy
  const handlePerformAction = async (refundMethod?: string | null) => {
    if (!detail || busyIdsRef.current.has(detail.id)) return;
    busyIdsRef.current.add(detail.id);
    setActionLoading(true);
    setError(null);

    try {
      let updated: SalesReturn;
      if (dialogState.type === 'confirm') {
        updated = await salesReturnService.confirm(detail.id, refundMethod);
      } else {
        updated = await salesReturnService.cancel(detail.id);
      }
      setDetail(updated);
      setDialogState({ isOpen: false, type: 'confirm' });
      onRefresh();
    } catch (err: any) {
      const parsed = getSalesReturnErrorMessage(err);
      setError(parsed.message);
    } finally {
      setActionLoading(false);
      busyIdsRef.current.delete(detail.id);
    }
  };

  const getStatusBadgeTone = (status: string): 'neutral' | 'success' | 'danger' => {
    if (status === 'Confirmed') return 'success';
    if (status === 'Cancelled') return 'danger';
    return 'neutral';
  };

  return (
    <div
      className="fixed inset-0 z-50 overflow-hidden bg-slate-900/40 backdrop-blur-xs flex justify-end"
      role="dialog"
      aria-modal="true"
    >
      <div className="bg-white w-full max-w-2xl h-full shadow-2xl flex flex-col overflow-hidden animate-slide-left">
        {/* Header */}
        <div className="p-4 border-b border-gray-200 flex items-center justify-between bg-gray-50/50">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-indigo-50 text-indigo-600 rounded-xl">
              <RotateCcw className="w-5 h-5" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-base font-bold text-gray-900">
                  {detail?.number || 'Chi tiết phiếu trả hàng'}
                </h2>
                {detail && (
                  <Badge tone={getStatusBadgeTone(detail.status)}>
                    {SALES_RETURN_STATUS_LABEL[detail.status] || detail.status}
                  </Badge>
                )}
              </div>
              <p className="text-xs text-gray-500 mt-0.5">
                {detail ? `Ngày tạo: ${new Date(detail.createdAt).toLocaleString('vi-VN')}` : 'Đang tải...'}
              </p>
            </div>
          </div>
          <Button variant="ghost" aria-label="Đóng" onClick={onClose}>
            <X size={20} />
          </Button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-5 space-y-5">
          {error && (
            <div className="p-3 bg-rose-50 border border-rose-200 rounded-xl flex items-center gap-2 text-xs text-rose-700">
              <AlertCircle className="w-4 h-4 flex-none" />
              <span>{error}</span>
            </div>
          )}

          {loading ? (
            <div className="p-12 text-center text-xs text-gray-500">
              <Loader2 className="w-6 h-6 animate-spin mx-auto mb-2 text-indigo-600" />
              Đang tải chi tiết phiếu trả hàng...
            </div>
          ) : detail ? (
            <>
              {/* Thông tin đơn và kho */}
              <div className="grid grid-cols-2 gap-3 p-4 bg-gray-50 border border-gray-200 rounded-xl text-xs">
                <div>
                  <span className="text-gray-400 block mb-0.5">Đơn hàng gốc:</span>
                  <div className="flex items-center gap-1.5">
                    <span className="font-bold text-indigo-600">{detail.orderNumber}</span>
                    <Badge tone="neutral">{detail.orderChannel}</Badge>
                    {onOpenOrder && (
                      <button
                        type="button"
                        onClick={() => onOpenOrder(detail.orderId)}
                        className="text-gray-400 hover:text-indigo-600 inline-flex items-center ml-1"
                        title="Xem chi tiết đơn hàng gốc"
                      >
                        <ExternalLink size={13} />
                      </button>
                    )}
                  </div>
                </div>

                <div>
                  <span className="text-gray-400 block mb-0.5">Chi nhánh &amp; Kho nhận:</span>
                  <div className="font-semibold text-gray-800">{detail.branchName}</div>
                  <div className="text-[11px] text-gray-500 flex items-center gap-1">
                    <Warehouse size={12} />
                    <span>{detail.warehouseName}</span>
                  </div>
                </div>

                <div>
                  <span className="text-gray-400 block mb-0.5">Lý do trả hàng:</span>
                  <span className="font-medium text-gray-900">{detail.reason}</span>
                  {detail.note && (
                    <div className="text-[11px] text-gray-500 italic mt-0.5">
                      Ghi chú: {detail.note}
                    </div>
                  )}
                </div>

                <div>
                  <span className="text-gray-400 block mb-0.5">Hoàn tiền:</span>
                  <div className="font-bold text-indigo-700 text-sm">
                    {detail.totalRefundAmount.toLocaleString('vi-VN')} đ
                  </div>
                  <div className="text-[11px] text-gray-500">
                    {detail.refundMethod
                      ? `Phương thức: ${detail.refundMethod === 'Cash' ? 'Tiền mặt' : detail.refundMethod}`
                      : 'Tiền sàn / Chưa hoàn'}
                  </div>
                </div>

                <div className="pt-2 border-t border-gray-200 col-span-2 grid grid-cols-2 gap-3 text-[11px] text-gray-500">
                  <div>
                    <span>Người lập: </span>
                    <strong className="text-gray-700">{detail.createdByName || detail.createdBy}</strong>
                  </div>
                  {detail.confirmedAt && (
                    <div>
                      <span>Xác nhận bởi: </span>
                      <strong className="text-gray-700">
                        {detail.confirmedByName || detail.confirmedBy || 'Hệ thống'}
                      </strong>{' '}
                      ({new Date(detail.confirmedAt).toLocaleString('vi-VN')})
                    </div>
                  )}
                </div>
              </div>

              {/* Bảng danh sách dòng sản phẩm trả */}
              <div>
                <h3 className="text-xs font-bold text-gray-700 uppercase tracking-wide mb-2">
                  Danh sách sản phẩm nhập lại kho
                </h3>
                <div className="border border-gray-200 rounded-xl overflow-hidden">
                  <table className="w-full text-xs text-left border-collapse">
                    <thead className="bg-gray-50 border-b border-gray-200 text-gray-600 font-semibold">
                      <tr>
                        <th className="p-2.5">Mã SKU &amp; Tên sản phẩm</th>
                        <th className="p-2.5 text-center">ĐVT</th>
                        <th className="p-2.5 text-center">SL trả</th>
                        <th className="p-2.5 text-center">SL gốc</th>
                        <th className="p-2.5 text-right">Tiền hoàn</th>
                        {canSeeCost && (
                          <th className="p-2.5 text-right text-gray-500">Giá vốn gốc (WAC)</th>
                        )}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {detail.items?.map((it) => (
                        <tr key={it.id} className="hover:bg-gray-50">
                          <td className="p-2.5">
                            <div className="font-bold text-gray-900">{it.skuCode}</div>
                            <div className="text-gray-500 text-[11px] truncate max-w-xs">
                              {it.skuName}
                            </div>
                          </td>
                          <td className="p-2.5 text-center text-gray-600">{it.unitName || '—'}</td>
                          <td className="p-2.5 text-center font-bold text-indigo-700">
                            {it.quantity}
                          </td>
                          <td className="p-2.5 text-center text-gray-500">
                            {it.baseQuantity || it.quantity}
                          </td>
                          <td className="p-2.5 text-right font-semibold text-gray-900">
                            {it.refundAmount.toLocaleString('vi-VN')} đ
                          </td>
                          {canSeeCost && (
                            <td className="p-2.5 text-right text-gray-600">
                              {it.originalUnitCost.toLocaleString('vi-VN')} đ
                            </td>
                          )}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>

              {/* Liên kết điều hướng tiện ích cho phiếu Confirmed */}
              {detail.status === 'Confirmed' && (
                <div className="p-3 bg-indigo-50/50 border border-indigo-100 rounded-xl text-xs space-y-2">
                  <div className="font-semibold text-indigo-900 flex items-center gap-1.5">
                    <GitBranch size={14} className="text-indigo-600" />
                    Truy vết chứng từ &amp; sổ kho
                  </div>
                  <div className="flex flex-wrap gap-2 pt-1">
                    <Link
                      to="/inventory?source=SalesReturn"
                      className="px-2.5 py-1 bg-white border border-indigo-200 rounded-lg text-indigo-700 hover:bg-indigo-50 font-medium inline-flex items-center gap-1"
                    >
                      <Boxes size={12} />
                      Xem sổ kho (Lọc Khách trả hàng)
                    </Link>
                    {onOpenTrace && (
                      <button
                        type="button"
                        onClick={() => onOpenTrace(detail.orderId, detail.orderNumber)}
                        className="px-2.5 py-1 bg-white border border-indigo-200 rounded-lg text-indigo-700 hover:bg-indigo-50 font-medium inline-flex items-center gap-1"
                      >
                        <GitBranch size={12} />
                        Truy vết đơn hàng gốc
                      </button>
                    )}
                  </div>
                </div>
              )}
            </>
          ) : null}
        </div>

        {/* Footer actions */}
        <div className="p-4 border-t border-gray-200 bg-gray-50/50 flex items-center justify-between">
          <Button variant="secondary" onClick={onClose} disabled={actionLoading}>
            Đóng
          </Button>

          {detail && detail.status === 'Draft' && (
            <div className="flex items-center gap-2">
              <Button
                variant="danger"
                onClick={() => setDialogState({ isOpen: true, type: 'cancel' })}
                disabled={actionLoading}
              >
                Hủy phiếu
              </Button>
              <Button
                variant="primary"
                onClick={() => setDialogState({ isOpen: true, type: 'confirm' })}
                disabled={actionLoading}
              >
                Xác nhận nhập kho
              </Button>
            </div>
          )}
        </div>
      </div>

      {/* Confirmation Dialog */}
      <ConfirmSalesReturnDialog
        isOpen={dialogState.isOpen}
        type={dialogState.type}
        salesReturn={detail}
        onClose={() => setDialogState({ isOpen: false, type: 'confirm' })}
        onConfirm={handlePerformAction}
        loading={actionLoading}
      />
    </div>
  );
};
