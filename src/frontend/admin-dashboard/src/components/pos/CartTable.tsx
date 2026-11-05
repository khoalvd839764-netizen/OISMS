import React from 'react';
import { Trash2, Plus, Minus, ShoppingCart, AlertTriangle, ArrowRight } from 'lucide-react';
import type { CartItem } from '../../types/pos';

interface CartTableProps {
  items: CartItem[];
  onUpdateQuantity: (variantId: string, delta: number) => void;
  onSetQuantity: (variantId: string, quantity: number) => void;
  onRemoveItem: (variantId: string) => void;
  onClearCart: () => void;
  onFocusSearch: () => void;
  onUpdateUnit?: (lineKey: string, newUnitId: string) => void;
}

export const CartTable: React.FC<CartTableProps> = ({
  items,
  onUpdateQuantity,
  onSetQuantity,
  onRemoveItem,
  onClearCart,
  onFocusSearch,
  onUpdateUnit,
}) => {
  const formatCurrency = (amount: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(amount);
  };

  if (items.length === 0) {
    return (
      <div className="flex-1 bg-white rounded-2xl border border-slate-200/80 shadow-sm p-8 flex flex-col items-center justify-center text-center">
        <div className="w-20 h-20 rounded-3xl bg-indigo-50 flex items-center justify-center text-indigo-500 mb-4 shadow-inner">
          <ShoppingCart className="w-10 h-10 stroke-[1.5]" />
        </div>
        <h3 className="text-xl font-bold text-slate-800 mb-2">Giỏ hàng hiện đang trống</h3>
        <p className="text-slate-500 max-w-md text-sm mb-6 leading-relaxed">
          Gõ tên sản phẩm, mã SKU hoặc dùng máy quét mã vạch để thêm sản phẩm vào đơn hàng bán lẻ.
        </p>
        <button
          type="button"
          onClick={onFocusSearch}
          className="inline-flex items-center gap-2 px-5 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-700 text-white font-semibold text-sm shadow-md shadow-indigo-600/20 transition-all hover:gap-3"
        >
          <span>Bắt đầu tìm kiếm hoặc quét mã (F2)</span>
          <ArrowRight className="w-4 h-4" />
        </button>
      </div>
    );
  }

  return (
    <div className="flex-1 bg-white rounded-2xl border border-slate-200/80 shadow-sm flex flex-col overflow-hidden">
      {/* Table Header Controls */}
      <div className="px-5 py-3.5 bg-slate-50/80 border-b border-slate-200/80 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <span className="font-bold text-slate-800 text-sm">Danh sách mặt hàng</span>
          <span className="px-2 py-0.5 rounded-full bg-indigo-100 text-indigo-700 text-xs font-bold">
            {items.reduce((sum, item) => sum + item.quantity, 0)} sản phẩm ({items.length} dòng)
          </span>
        </div>
        <button
          type="button"
          onClick={onClearCart}
          className="text-xs font-semibold text-rose-600 hover:text-rose-700 hover:bg-rose-50 px-2.5 py-1.5 rounded-lg transition-colors flex items-center gap-1.5"
        >
          <Trash2 className="w-3.5 h-3.5" />
          <span>Làm mới giỏ</span>
        </button>
      </div>

      {/* Cart Items List */}
      <div className="flex-1 overflow-y-auto divide-y divide-slate-100">
        <table className="w-full text-left border-collapse">
          <thead>
            <tr className="text-[11px] font-bold text-slate-400 uppercase tracking-wider bg-slate-50/50 sticky top-0 z-10 border-b border-slate-100">
              <th className="py-2.5 pl-5 pr-2 w-10 text-center">#</th>
              <th className="py-2.5 px-3">Sản phẩm & SKU</th>
              <th className="py-2.5 px-3 text-right">Đơn giá</th>
              <th className="py-2.5 px-3 text-center w-36">Số lượng</th>
              <th className="py-2.5 px-3 text-right">Thành tiền</th>
              <th className="py-2.5 pr-5 pl-2 w-12 text-center">Xóa</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 text-sm">
            {items.map((item, index) => {
              const isOutOfStock = item.available <= 0;
              const isOverStock = item.available > 0 && item.quantity > item.available;
              const isMaxStockReached = item.available > 0 && item.quantity >= item.available;
              const subtotal = item.retailPrice * item.quantity;

              return (
                <tr key={item.lineKey || `${item.variantId}:${item.unitId || ''}`} className="hover:bg-slate-50/60 transition-colors group">
                  {/* Row Index */}
                  <td className="py-3 pl-5 pr-2 text-center text-xs text-slate-400 font-mono">
                    {index + 1}
                  </td>

                  {/* Product Details */}
                  <td className="py-3 px-3">
                    <div className="font-bold text-slate-800 leading-snug">
                      {item.productName}
                    </div>
                    <div className="flex flex-wrap items-center gap-2 mt-1 text-xs text-slate-500">
                      <span className="font-mono font-semibold text-slate-600 bg-slate-100 px-1.5 py-0.5 rounded">
                        {item.sku}
                      </span>
                      {item.variantName && <span>Biến thể: {item.variantName}</span>}
                      {item.barcode && <span className="text-slate-400">Barcode: {item.barcode}</span>}
                      {item.unitName && (
                        <span className="font-semibold text-indigo-700 bg-indigo-50 border border-indigo-200 px-1.5 py-0.5 rounded text-[11px]">
                          ĐVT: {item.unitName}
                        </span>
                      )}
                      {item.units && item.units.length > 1 && (
                        <select
                          value={item.unitId || ''}
                          onChange={(e) => onUpdateUnit?.(item.lineKey || item.variantId, e.target.value)}
                          className="text-[11px] bg-slate-100 hover:bg-slate-200 border border-slate-300 rounded px-1.5 py-0.5 font-semibold text-slate-700 cursor-pointer focus:outline-none"
                          title="Thay đổi đơn vị tính cho dòng này"
                        >
                          {item.units.map((u) => (
                            <option key={u.unitId} value={u.unitId}>
                              {u.unitName} (x{u.factor}{u.isBase ? ' - gốc' : ''}){u.salePrice == null ? ' - chưa có giá' : ''}
                            </option>
                          ))}
                        </select>
                      )}
                    </div>
                    {isOutOfStock && (
                      <div className="flex items-center gap-1 text-[11px] text-rose-600 font-bold mt-1">
                        <AlertTriangle className="w-3.5 h-3.5 text-rose-500 shrink-0" />
                        <span>Hết hàng tại chi nhánh này (Tồn: 0)</span>
                      </div>
                    )}
                    {isOverStock && (
                      <div className="flex items-center gap-1 text-[11px] text-amber-600 font-medium mt-1">
                        <AlertTriangle className="w-3.5 h-3.5 text-amber-500 shrink-0" />
                        <span>Vượt tồn khả dụng (hiện có: {item.available})</span>
                      </div>
                    )}
                  </td>

                  {/* Unit Price */}
                  <td className="py-3 px-3 text-right font-medium text-slate-600 whitespace-nowrap">
                    {formatCurrency(item.retailPrice)}
                  </td>

                  {/* Quantity Controller */}
                  <td className="py-3 px-3 text-center">
                    <div className="inline-flex items-center rounded-lg border border-slate-200 bg-white p-0.5 shadow-xs">
                      <button
                        type="button"
                        onClick={() => onUpdateQuantity(item.lineKey || item.variantId, -1)}
                        className="w-7 h-7 flex items-center justify-center text-slate-600 hover:bg-slate-100 rounded-md transition-colors"
                        title="Giảm 1"
                      >
                        <Minus className="w-3.5 h-3.5" />
                      </button>
                      <input
                        type="number"
                        min="1"
                        max={item.available > 0 ? item.available : 1}
                        value={item.quantity}
                        onChange={(e) => {
                          const val = parseInt(e.target.value, 10);
                          if (!isNaN(val) && val >= 1) {
                            const maxLimit = item.available > 0 ? item.available : 1;
                            onSetQuantity(item.lineKey || item.variantId, Math.min(val, maxLimit));
                          }
                        }}
                        className="w-12 text-center font-bold text-slate-800 text-sm focus:outline-none"
                      />
                      <button
                        type="button"
                        disabled={isMaxStockReached || isOutOfStock}
                        onClick={() => onUpdateQuantity(item.lineKey || item.variantId, 1)}
                        className={`w-7 h-7 flex items-center justify-center rounded-md transition-colors ${
                          isMaxStockReached || isOutOfStock
                            ? 'text-slate-300 cursor-not-allowed bg-slate-50'
                            : 'text-slate-600 hover:bg-slate-100'
                        }`}
                        title={isMaxStockReached ? `Đã đạt giới hạn tồn khả dụng (${item.available})` : 'Tăng 1'}
                      >
                        <Plus className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  </td>

                  {/* Subtotal */}
                  <td className="py-3 px-3 text-right font-bold text-indigo-600 whitespace-nowrap">
                    {formatCurrency(subtotal)}
                  </td>

                  {/* Remove Button */}
                  <td className="py-3 pr-5 pl-2 text-center">
                    <button
                      type="button"
                      onClick={() => onRemoveItem(item.lineKey || item.variantId)}
                      className="p-1.5 text-slate-400 hover:text-rose-600 hover:bg-rose-50 rounded-lg transition-colors"
                      title="Xóa sản phẩm"
                    >
                      <Trash2 className="w-4 h-4" />
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
