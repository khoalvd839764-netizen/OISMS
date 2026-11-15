import React, { useState } from 'react';
import { Printer, Truck, X } from 'lucide-react';
import type { OrderDetail } from '../../types/ordersCenter';
import { PrintArea } from '../print/PrintArea';

interface ShippingSlipPrintModalProps {
  isOpen: boolean;
  order: OrderDetail | null;
  tenantName?: string;
  onClose: () => void;
}

export const ShippingSlipPrintModal: React.FC<ShippingSlipPrintModalProps> = ({
  isOpen,
  order,
  tenantName,
  onClose,
}) => {
  const [paper, setPaper] = useState<'a4' | '80'>('a4');

  if (!isOpen || !order) return null;

  const formatCurrency = (val: number) =>
    new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);

  const is80 = paper === '80';

  return (
    <div className="fixed inset-0 z-60 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs no-print-bg">
      <style>{
        is80
          ? `@media print { html, body { width: 80mm !important; } .print-container, .shipping-slip-paper { width: 80mm !important; max-width: 80mm !important; margin: 0 !important; padding: 2mm 3mm !important; font-size: 11px !important; border: none !important; box-shadow: none !important; } .shipping-slip-paper table { font-size: 10px !important; table-layout: fixed; width: 100%; } .shipping-slip-paper td { overflow-wrap: anywhere; white-space: normal !important; } } @page { size: 80mm auto; margin: 0; }`
          : `@media print { html, body { width: auto !important; } .print-container, .shipping-slip-paper { width: 100% !important; max-width: 190mm !important; margin: 0 auto !important; padding: 0 !important; border: none !important; box-shadow: none !important; } .shipping-slip-paper tr { break-inside: avoid; page-break-inside: avoid; } } @page { size: A4 portrait; margin: 12mm; }`
      }</style>

      <div className="bg-white rounded-2xl shadow-2xl border border-slate-200 max-w-3xl w-full overflow-hidden flex flex-col max-h-[95vh] no-print-border">
        {/* Header */}
        <div className="px-5 py-3.5 text-white flex items-center justify-between no-print bg-slate-900">
          <div className="flex items-center gap-2">
            <Truck className="w-5 h-5 text-emerald-400" />
            <span className="font-bold text-sm">
              In Phiếu Giao Hàng ({is80 ? 'Khổ 80mm' : 'Khổ A4'})
            </span>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-1 rounded-lg text-slate-300 hover:text-white hover:bg-slate-800 transition-colors"
            aria-label="Đóng"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Paper size & Print action */}
        <div className="p-3 border-b flex items-center justify-between no-print bg-indigo-50 border-indigo-100">
          <div className="flex items-center gap-1.5 bg-white p-1 rounded-lg border border-slate-200 text-xs font-semibold">
            <button
              type="button"
              onClick={() => setPaper('a4')}
              className={`px-3 py-1 rounded-md transition-colors ${
                !is80 ? 'bg-indigo-600 text-white shadow-xs' : 'text-slate-600 hover:text-slate-900'
              }`}
            >
              Khổ A4
            </button>
            <button
              type="button"
              onClick={() => setPaper('80')}
              className={`px-3 py-1 rounded-md transition-colors ${
                is80 ? 'bg-indigo-600 text-white shadow-xs' : 'text-slate-600 hover:text-slate-900'
              }`}
            >
              Khổ 80mm
            </button>
          </div>

          <button
            type="button"
            onClick={() => window.print()}
            className="inline-flex items-center gap-1.5 px-4 py-1.5 text-white bg-indigo-600 hover:bg-indigo-700 rounded-xl font-bold text-xs shadow-md transition-all"
          >
            <Printer className="w-4 h-4" />
            <span>In Phiếu (Ctrl + P)</span>
          </button>
        </div>

        {/* Printable slip */}
        <div className="p-6 overflow-y-auto bg-slate-100/60 flex justify-center">
          <PrintArea>
          <div
            className={`shipping-slip-paper bg-white border border-slate-300 text-slate-900 font-sans shadow-sm w-full ${
              is80 ? 'max-w-[320px] p-4 text-xs' : 'max-w-[720px] p-8 text-sm'
            }`}
          >
            {/* Store & Slip Header */}
            <div className="text-center pb-3 border-b border-dashed border-slate-300">
              <h2 className={`font-extrabold uppercase tracking-tight text-slate-900 m-0 ${is80 ? 'text-base' : 'text-lg'}`}>
                {tenantName || 'CỬA HÀNG OISMS'}
              </h2>
              {order.branchName && (
                <div className="text-xs text-slate-600 font-medium mt-0.5">
                  Chi nhánh: {order.branchName}
                </div>
              )}
              <div className={`font-black uppercase tracking-wider text-slate-900 mt-2 ${is80 ? 'text-sm' : 'text-base'}`}>
                PHIẾU GIAO HÀNG
              </div>
              <div className="text-xs text-slate-600 mt-0.5">
                Mã đơn: <span className="font-mono font-bold text-slate-900">{order.number}</span>
                {' · '}Kênh: <span className="font-semibold">{order.channel}</span>
              </div>
              {order.externalOrderId && (
                <div className="text-xs text-slate-500">
                  Mã đơn sàn: <span className="font-mono">{order.externalOrderId}</span>
                </div>
              )}
              <div className="text-xs text-slate-500">
                Ngày tạo: {new Date(order.createdAt).toLocaleString('vi-VN')}
              </div>
            </div>

            {/* Recipient Info */}
            <div className="py-3 border-b border-dashed border-slate-300 space-y-1 text-xs">
              <div className="font-bold uppercase text-slate-700">Thông tin người nhận</div>
              <div>
                <span className="text-slate-500">Khách hàng: </span>
                <span className="font-semibold text-slate-900">{order.customerName || 'Khách lẻ'}</span>
              </div>
              <div>
                <span className="text-slate-500">Số điện thoại: </span>
                <span className="font-mono font-semibold text-slate-900">{order.customerPhone || '—'}</span>
              </div>
              <div>
                <span className="text-slate-500">Địa chỉ giao: </span>
                <span className="text-slate-900">{order.shippingAddress || '—'}</span>
              </div>
            </div>

            {/* Items Table (NEVER render costPrice/totalCogs) */}
            <div className="py-3 border-b border-dashed border-slate-300">
              <table className="w-full text-xs border-collapse">
                <thead>
                  <tr className="border-b border-slate-300 text-slate-600 font-bold">
                    <th className="text-left py-1.5 pr-2">Sản phẩm</th>
                    <th className="text-center py-1.5 px-1">SL</th>
                    <th className="text-right py-1.5 px-1">Đơn giá</th>
                    <th className="text-right py-1.5 pl-1">Thành tiền</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {order.items.map((item) => {
                    const lineTotal = item.quantity * item.unitPrice;
                    return (
                      <tr key={item.id} className="align-top">
                        <td className="py-1.5 pr-2">
                          <div className="font-semibold text-slate-900 leading-tight">{item.skuName}</div>
                          <div className="text-[10px] text-slate-500 font-mono">{item.skuCode}</div>
                        </td>
                        <td className="py-1.5 px-1 text-center font-semibold text-slate-800 whitespace-nowrap">
                          {item.quantity}
                          {item.unitName ? ` ${item.unitName}` : ''}
                        </td>
                        <td className="py-1.5 px-1 text-right text-slate-700 whitespace-nowrap">
                          {item.unitPrice.toLocaleString('vi-VN')}
                        </td>
                        <td className="py-1.5 pl-1 text-right font-bold text-slate-900 whitespace-nowrap">
                          {lineTotal.toLocaleString('vi-VN')}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {/* COD Total */}
            <div className="py-3 border-b border-dashed border-slate-300">
              <div className="flex justify-between items-center font-black text-slate-900">
                <span className={is80 ? 'text-xs' : 'text-sm'}>TỔNG THU (COD):</span>
                <span className={`font-mono ${is80 ? 'text-sm' : 'text-base'}`}>
                  {formatCurrency(order.totalAmount)}
                </span>
              </div>
            </div>

            {/* Footer / Signature */}
            <div className={`pt-4 text-xs text-slate-600 ${is80 ? 'text-center space-y-1' : 'grid grid-cols-2 gap-6 text-center'}`}>
              {!is80 && (
                <div>
                  <div className="font-bold text-slate-800">Người giao hàng</div>
                  <div className="text-[10px] text-slate-400 mt-1">(Ký, ghi rõ họ tên)</div>
                </div>
              )}
              <div>
                <div className="font-bold text-slate-800">Người nhận hàng</div>
                <div className="text-[10px] text-slate-400 mt-1">(Ký xác nhận đã nhận đủ hàng)</div>
              </div>
            </div>
          </div>
          </PrintArea>
        </div>
      </div>
    </div>
  );
};
