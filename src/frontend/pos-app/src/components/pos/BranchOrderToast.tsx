import React from 'react';
import { PackageCheck, X } from 'lucide-react';
import type { BranchOrderCreated } from '../../services/realtimeService';

interface BranchOrderToastProps {
  toasts: BranchOrderCreated[];
  onDismiss: (orderId: string) => void;
}

export const BranchOrderToast: React.FC<BranchOrderToastProps> = ({ toasts, onDismiss }) => {
  if (toasts.length === 0) return null;

  return (
    <div
      aria-live="polite"
      className="fixed bottom-4 left-4 z-40 flex flex-col gap-2 max-w-sm w-full pointer-events-none no-print"
    >
      {toasts.map((order) => {
        const previewItems = order.items.slice(0, 3);
        const remainingCount = order.items.length - previewItems.length;
        return (
          <div
            key={order.orderId}
            role="status"
            className="pointer-events-auto bg-slate-900 text-white rounded-xl shadow-xl border border-slate-700 px-3.5 py-2.5 text-xs"
          >
            <div className="flex items-start justify-between gap-2">
              <div className="flex items-center gap-1.5 font-bold text-emerald-300">
                <PackageCheck className="w-4 h-4 shrink-0" />
                <span>Đơn mới #{order.orderNumber}</span>
                <span className="px-1.5 py-0.5 rounded bg-slate-800 text-slate-200 text-[10px] font-semibold">
                  {order.channel}
                </span>
              </div>
              <button
                type="button"
                onClick={() => onDismiss(order.orderId)}
                className="text-slate-400 hover:text-white p-0.5 rounded"
                aria-label="Đóng thông báo"
              >
                <X className="w-3.5 h-3.5" />
              </button>
            </div>
            <ul className="mt-1.5 space-y-0.5 text-slate-200">
              {previewItems.map((item, idx) => (
                <li key={`${item.skuId}-${idx}`} className="flex justify-between gap-2">
                  <span className="truncate">{item.productName || item.skuCode}</span>
                  <span className="font-mono font-semibold text-amber-300 whitespace-nowrap">
                    ×{item.quantity}
                    {item.unitName ? ` ${item.unitName}` : ''}
                  </span>
                </li>
              ))}
              {remainingCount > 0 && (
                <li className="text-[11px] text-slate-400">
                  +{remainingCount} mặt hàng khác
                </li>
              )}
            </ul>
          </div>
        );
      })}
    </div>
  );
};
