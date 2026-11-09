import React, { useState, useEffect, useRef, useCallback } from 'react';
import {
  AlertCircle,
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  ChevronRight,
  Loader2,
  RotateCcw,
  Search,
  X,
} from 'lucide-react';
import { Button, Input, Select, Badge } from '../ui';
import { NumberField } from '../NumberField';
import {
  salesReturnService,
  getSalesReturnErrorMessage,
  clearSalesReturnIntent,
} from '../../services/salesReturnService';
import { orderService } from '../../services/orderService';
import { apiAdapter } from '../../services/apiAdapter';
import type {
  ReturnableItem,
  CreateSalesReturnRequest,
  CreateSalesReturnItem,
  SalesReturn,
} from '../../types/salesReturns';
import type { OrderSummary, OrderDetail } from '../../types/ordersCenter';

interface CreateSalesReturnModalProps {
  isOpen: boolean;
  preselectedOrder?: OrderSummary | OrderDetail | null;
  onClose: () => void;
  onSuccess: (salesReturn: SalesReturn, shouldOpenDetail?: boolean) => void;
}

interface ItemReturnDraft {
  item: ReturnableItem;
  quantity: number;
  refundAmount: number;
  maxRefund: number;
}

const COMMON_REASONS = [
  'Lỗi sản phẩm',
  'Sai hàng',
  'Khách đổi ý',
  'Hư hỏng do vận chuyển',
  'Khác',
];

export const CreateSalesReturnModal: React.FC<CreateSalesReturnModalProps> = ({
  isOpen,
  preselectedOrder,
  onClose,
  onSuccess,
}) => {
  // 3-step state
  const [step, setStep] = useState<1 | 2 | 3>(1);

  // Step 1: Order selection
  const [searchOrderTerm, setSearchOrderTerm] = useState('');
  const [searchingOrders, setSearchingOrders] = useState(false);
  const [foundOrders, setFoundOrders] = useState<OrderSummary[]>([]);
  const [selectedOrder, setSelectedOrder] = useState<OrderSummary | OrderDetail | null>(null);
  const [orderError, setOrderError] = useState<string | null>(null);

  // Step 2: Line items & return details
  const [loadingItems, setLoadingItems] = useState(false);
  const [returnableItems, setReturnableItems] = useState<ReturnableItem[]>([]);
  const [itemDrafts, setItemDrafts] = useState<Record<string, ItemReturnDraft>>({});
  const [warehouses, setWarehouses] = useState<Array<{ id: string; name: string }>>([]);
  const [selectedWarehouseId, setSelectedWarehouseId] = useState('');
  const [reason, setReason] = useState('');
  const [customReason, setCustomReason] = useState('');
  const [note, setNote] = useState('');
  const [refundMethod, setRefundMethod] = useState<'Cash' | 'BankTransfer'>('Cash');
  const [stepError, setStepError] = useState<string | null>(null);

  // Step 3: Submitting
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const busyRef = useRef(false);

  // Initialize or reset when modal opens
  useEffect(() => {
    if (!isOpen) {
      setStep(1);
      setSearchOrderTerm('');
      setFoundOrders([]);
      setSelectedOrder(null);
      setOrderError(null);
      setReturnableItems([]);
      setItemDrafts({});
      setWarehouses([]);
      setSelectedWarehouseId('');
      setReason('');
      setCustomReason('');
      setNote('');
      setRefundMethod('Cash');
      setStepError(null);
      setSubmitError(null);
      return;
    }

    if (preselectedOrder) {
      handleSelectOrder(preselectedOrder);
    }
  }, [isOpen, preselectedOrder]);

  // Order Search
  const handleSearchOrders = useCallback(async (query: string) => {
    const q = query.trim();
    if (!q) {
      setFoundOrders([]);
      return;
    }
    setSearchingOrders(true);
    setOrderError(null);
    try {
      const res = await orderService.getOrders({
        search: q,
        pageSize: 10,
        pageIndex: 1,
      });
      setFoundOrders(res.items || []);
    } catch {
      setOrderError('Không thể tìm kiếm đơn hàng. Vui lòng thử lại.');
    } finally {
      setSearchingOrders(false);
    }
  }, []);

  // Handle choosing an order
  const handleSelectOrder = async (order: OrderSummary | OrderDetail) => {
    setOrderError(null);
    // Kiểm tra trạng thái đơn: Phải là Confirmed hoặc Completed
    if (order.status !== 'Confirmed' && order.status !== 'Completed') {
      setOrderError('Đơn chưa xuất kho, không trả được. Chỉ trả hàng cho đơn đã duyệt hoặc hoàn tất.');
      return;
    }

    setSelectedOrder(order);
    setStep(2);
    setLoadingItems(true);
    setStepError(null);

    try {
      // 1. Tải danh sách dòng hàng có thể trả
      const [items, whRes] = await Promise.all([
        salesReturnService.getReturnableItems(order.id),
        apiAdapter.getWarehouses(order.branchId),
      ]);

      setReturnableItems(items);

      // Khởi tạo draft cho từng dòng
      const drafts: Record<string, ItemReturnDraft> = {};
      items.forEach((it) => {
        drafts[it.orderItemId] = {
          item: it,
          quantity: 0,
          refundAmount: 0,
          maxRefund: 0,
        };
      });
      setItemDrafts(drafts);

      // 2. Tải danh sách kho
      const whList = whRes.data || [];
      setWarehouses(whList);
      if (whList.length > 0) {
        setSelectedWarehouseId(whList[0].id);
      }
    } catch (err: any) {
      const parsed = getSalesReturnErrorMessage(err);
      setStepError(parsed.message || 'Không thể tải thông tin mặt hàng có thể trả.');
    } finally {
      setLoadingItems(false);
    }
  };

  // Tiền hoàn tối đa của dòng = SL × đơn giá, cắt xuống 2 chữ số (backend chặn tiền hoàn quá 2 chữ số thập phân
  // và chặn vượt SL × đơn giá; dùng số nguyên xu để tránh sai số dấu phẩy động như 1.1 × 3).
  const lineMaxRefund = (qty: number, unitPrice: number) =>
    Math.floor(Math.round(qty * 1000) * Math.round(unitPrice * 100) / 1000 + 1e-6) / 100;

  // Nút "Trả hết"
  const handleReturnAll = () => {
    const newDrafts = { ...itemDrafts };
    const isMarketplace = ['Shopee', 'TikTok', 'Lazada'].includes(selectedOrder?.channel || '');

    returnableItems.forEach((it) => {
      if (it.returnableQuantity > 0) {
        const qty = it.returnableQuantity;
        const maxRef = lineMaxRefund(qty, it.unitPrice);
        newDrafts[it.orderItemId] = {
          item: it,
          quantity: qty,
          refundAmount: isMarketplace ? 0 : maxRef,
          maxRefund: maxRef,
        };
      }
    });
    setItemDrafts(newDrafts);
  };

  // Cập nhật số lượng trả của một dòng
  const handleQuantityChange = (orderItemId: string, val: number | null) => {
    const current = itemDrafts[orderItemId];
    if (!current) return;

    const qty = Math.max(0, Math.min(val ?? 0, current.item.returnableQuantity));
    const maxRef = lineMaxRefund(qty, current.item.unitPrice);
    const isMarketplace = ['Shopee', 'TikTok', 'Lazada'].includes(selectedOrder?.channel || '');

    setItemDrafts({
      ...itemDrafts,
      [orderItemId]: {
        ...current,
        quantity: qty,
        refundAmount: isMarketplace ? 0 : maxRef,
        maxRefund: maxRef,
      },
    });
  };

  // Cập nhật tiền hoàn của một dòng
  const handleRefundAmountChange = (orderItemId: string, val: number | null) => {
    const current = itemDrafts[orderItemId];
    if (!current) return;

    const amount = Math.max(0, Math.min(val ?? 0, current.maxRefund));
    setItemDrafts({
      ...itemDrafts,
      [orderItemId]: {
        ...current,
        refundAmount: amount,
      },
    });
  };

  // Tính toán tổng số lượng và tổng tiền hoàn
  const isMarketplaceOrder = ['Shopee', 'TikTok', 'Lazada'].includes(selectedOrder?.channel || '');
  const totalReturnQty = Object.values(itemDrafts).reduce((sum, d) => sum + d.quantity, 0);
  const totalMaxRefund = Object.values(itemDrafts).reduce((sum, d) => sum + d.maxRefund, 0);
  const totalActualRefund = isMarketplaceOrder
    ? 0
    : Object.values(itemDrafts).reduce((sum, d) => sum + d.refundAmount, 0);
  const returnFee = totalMaxRefund - totalActualRefund;

  // Validate step 2 để chuyển sang step 3
  const handleGoToReview = () => {
    setStepError(null);

    if (totalReturnQty <= 0) {
      setStepError('Vui lòng chọn ít nhất một mặt hàng và số lượng trả lớn hơn 0.');
      return;
    }

    if (!selectedWarehouseId) {
      setStepError('Vui lòng chọn kho nhận hàng trả.');
      return;
    }

    const effectiveReason = (reason === 'Khác' ? customReason : reason).trim();
    if (!effectiveReason) {
      setStepError('Lý do trả hàng là bắt buộc. Vui lòng chọn hoặc nhập lý do.');
      return;
    }

    setStep(3);
  };

  // Thực hiện submit (Lưu nháp hoặc Lưu và xác nhận)
  const handleSubmit = async (confirmImmediately: boolean) => {
    if (busyRef.current || submitting || !selectedOrder) return;
    busyRef.current = true;
    setSubmitting(true);
    setSubmitError(null);

    const effectiveReason = (reason === 'Khác' ? customReason : reason).trim();
    const returnItems: CreateSalesReturnItem[] = Object.values(itemDrafts)
      .filter((d) => d.quantity > 0)
      .map((d) => ({
        orderItemId: d.item.orderItemId,
        quantity: d.quantity,
        refundAmount: isMarketplaceOrder ? 0 : d.refundAmount,
      }));

    const payload: CreateSalesReturnRequest = {
      orderId: selectedOrder.id,
      warehouseId: selectedWarehouseId,
      reason: effectiveReason,
      note: note.trim() || undefined,
      items: returnItems,
    };

    let createdTicket: SalesReturn | null = null;

    try {
      // 1. Tạo phiếu trả hàng (Draft)
      createdTicket = await salesReturnService.create(payload);

      // 2. Nếu chọn "Lưu và xác nhận"
      if (confirmImmediately && createdTicket) {
        try {
          const confirmedTicket = await salesReturnService.confirm(
            createdTicket.id,
            isMarketplaceOrder ? null : refundMethod
          );
          onSuccess(confirmedTicket, true);
          onClose();
          return;
        } catch (confirmErr: any) {
          // AC-05: Tạo thành công nhưng xác nhận bị lỗi -> giữ phiếu Draft, mở drawer chi tiết để người dùng xác nhận lại
          const errDetail = getSalesReturnErrorMessage(confirmErr);
          setSubmitError(
            `Đã lập phiếu nháp ${createdTicket.number} thành công, nhưng xác nhận nhập kho thất bại: ${errDetail.message}. Phiếu đang ở trạng thái Nháp.`
          );
          onSuccess(createdTicket, true);
          return;
        }
      }

      // Lưu nháp thành công
      onSuccess(createdTicket, false);
      onClose();
    } catch (createErr: any) {
      const errInfo = getSalesReturnErrorMessage(createErr);

      // Khóa idempotency đã gắn với phiếu khác: bỏ khóa cũ để lần gửi sau dùng khóa mới
      if (errInfo.code === 'IDEMPOTENCY_PAYLOAD_MISMATCH') {
        clearSalesReturnIntent();
      }

      // AC-06: Khi gặp RETURN_QUANTITY_EXCEEDED, tự động tải lại số còn được trả
      if (errInfo.code === 'RETURN_QUANTITY_EXCEEDED') {
        try {
          const freshItems = await salesReturnService.getReturnableItems(selectedOrder.id);
          setReturnableItems(freshItems);
          const updatedDrafts = { ...itemDrafts };
          freshItems.forEach((fi) => {
            const prev = updatedDrafts[fi.orderItemId];
            if (prev) {
              const qty = Math.min(prev.quantity, fi.returnableQuantity);
              const maxRef = lineMaxRefund(qty, fi.unitPrice);
              updatedDrafts[fi.orderItemId] = {
                ...prev,
                item: fi,
                quantity: qty,
                maxRefund: maxRef,
                refundAmount: isMarketplaceOrder ? 0 : Math.min(prev.refundAmount, maxRef),
              };
            }
          });
          setItemDrafts(updatedDrafts);
        } catch {}
      }

      setSubmitError(errInfo.message);
    } finally {
      setSubmitting(false);
      busyRef.current = false;
    }
  };

  if (!isOpen) return null;

  return (
    <div className="modal-backdrop" role="presentation">
      <div
        className="modal modal--wide flex flex-col max-h-[92vh] overflow-hidden"
        role="dialog"
        aria-modal="true"
        style={{ maxWidth: 900 }}
      >
        {/* Modal Header */}
        <div className="modal-head">
          <div className="flex items-center gap-2">
            <RotateCcw className="w-5 h-5 text-indigo-600" />
            <div>
              <h2 className="text-base font-bold text-gray-900 leading-tight">
                Lập phiếu trả hàng (Khách trả hàng)
              </h2>
              <div className="flex items-center gap-2 text-xs text-gray-500 mt-0.5">
                <span className={step === 1 ? 'font-bold text-indigo-600' : ''}>
                  1. Chọn đơn gốc
                </span>
                <ChevronRight size={14} />
                <span className={step === 2 ? 'font-bold text-indigo-600' : ''}>
                  2. Nhập dòng trả &amp; kho nhận
                </span>
                <ChevronRight size={14} />
                <span className={step === 3 ? 'font-bold text-indigo-600' : ''}>
                  3. Xem lại &amp; lưu
                </span>
              </div>
            </div>
          </div>
          <Button variant="ghost" aria-label="Đóng" onClick={onClose} disabled={submitting}>
            <X size={20} />
          </Button>
        </div>

        {/* Modal Body */}
        <div className="flex-1 overflow-y-auto p-5 space-y-4">
          {/* ================= STEP 1: CHỌN ĐƠN GỐC ================= */}
          {step === 1 && (
            <div className="space-y-4">
              <p className="text-xs text-gray-600">
                Tìm kiếm và chọn đơn hàng xuất bán ban đầu cần lập phiếu trả (chỉ áp dụng cho các
                đơn hàng ở trạng thái <strong>Đã duyệt</strong> hoặc <strong>Hoàn tất</strong>).
              </p>

              <div className="flex gap-2">
                <div className="relative flex-1">
                  <Search className="w-4 h-4 absolute left-3 top-3 text-gray-400" />
                  <Input
                    type="text"
                    placeholder="Nhập mã đơn hàng (ví dụ: ORD-...), tên khách hoặc SĐT..."
                    value={searchOrderTerm}
                    onChange={(e) => setSearchOrderTerm(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') handleSearchOrders(searchOrderTerm);
                    }}
                    style={{ paddingLeft: 34 }}
                  />
                </div>
                <Button
                  variant="secondary"
                  onClick={() => handleSearchOrders(searchOrderTerm)}
                  disabled={searchingOrders || !searchOrderTerm.trim()}
                >
                  {searchingOrders ? <Loader2 className="w-4 h-4 animate-spin" /> : 'Tìm kiếm'}
                </Button>
              </div>

              {orderError && (
                <div className="p-3 bg-rose-50 border border-rose-200 rounded-lg flex items-center gap-2 text-xs text-rose-700">
                  <AlertCircle className="w-4 h-4 flex-none" />
                  <span>{orderError}</span>
                </div>
              )}

              {/* Danh sách kết quả tìm kiếm */}
              <div className="border border-gray-200 rounded-xl overflow-hidden max-h-72 overflow-y-auto">
                <table className="w-full text-xs text-left border-collapse">
                  <thead className="bg-gray-50 border-b border-gray-200 text-gray-600 font-semibold sticky top-0">
                    <tr>
                      <th className="p-2.5">Mã đơn</th>
                      <th className="p-2.5">Kênh</th>
                      <th className="p-2.5">Khách hàng</th>
                      <th className="p-2.5 text-right">Tổng tiền</th>
                      <th className="p-2.5 text-center">Trạng thái</th>
                      <th className="p-2.5 text-right">Thao tác</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {foundOrders.length === 0 ? (
                      <tr>
                        <td colSpan={6} className="p-6 text-center text-gray-400">
                          {searchingOrders
                            ? 'Đang tìm kiếm đơn hàng...'
                            : searchOrderTerm
                            ? 'Không tìm thấy đơn hàng nào phù hợp.'
                            : 'Nhập từ khóa và bấm Tìm kiếm để tra cứu đơn.'}
                        </td>
                      </tr>
                    ) : (
                      foundOrders.map((ord) => {
                        const canReturn =
                          ord.status === 'Confirmed' || ord.status === 'Completed';
                        return (
                          <tr key={ord.id} className="hover:bg-gray-50 transition-colors">
                            <td className="p-2.5 font-bold text-indigo-600">{ord.number}</td>
                            <td className="p-2.5">
                              <Badge tone="neutral">{ord.channel}</Badge>
                            </td>
                            <td className="p-2.5">
                              <div className="font-medium text-gray-900">
                                {ord.customerName || 'Khách lẻ'}
                              </div>
                              <div className="text-[11px] text-gray-500">
                                {ord.customerPhone || '—'}
                              </div>
                            </td>
                            <td className="p-2.5 text-right font-semibold text-gray-900">
                              {ord.totalAmount.toLocaleString('vi-VN')} đ
                            </td>
                            <td className="p-2.5 text-center">
                              <span
                                className={`inline-block px-2 py-0.5 rounded text-[11px] font-bold ${
                                  canReturn
                                    ? 'bg-emerald-100 text-emerald-800'
                                    : 'bg-gray-100 text-gray-600'
                                }`}
                              >
                                {ord.status}
                              </span>
                            </td>
                            <td className="p-2.5 text-right">
                              <Button
                                variant={canReturn ? 'primary' : 'secondary'}
                                disabled={!canReturn}
                                onClick={() => handleSelectOrder(ord)}
                                style={{ padding: '4px 10px', fontSize: 11 }}
                              >
                                Chọn đơn
                              </Button>
                            </td>
                          </tr>
                        );
                      })
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {/* ================= STEP 2: NHẬP DÒNG TRẢ & KHO NHẬN ================= */}
          {step === 2 && selectedOrder && (
            <div className="space-y-4">
              {/* Tóm tắt đơn gốc */}
              <div className="grid grid-cols-2 md:grid-cols-4 gap-3 p-3 bg-gray-50 border border-gray-200 rounded-xl text-xs">
                <div>
                  <span className="text-gray-400 block">Đơn hàng gốc:</span>
                  <span className="font-bold text-indigo-600">{selectedOrder.number}</span>
                </div>
                <div>
                  <span className="text-gray-400 block">Kênh bán:</span>
                  <span className="font-semibold text-gray-800">{selectedOrder.channel}</span>
                </div>
                <div>
                  <span className="text-gray-400 block">Khách hàng:</span>
                  <span className="font-semibold text-gray-800">
                    {selectedOrder.customerName || 'Khách lẻ'}
                  </span>
                </div>
                <div>
                  <span className="text-gray-400 block">Tổng tiền đơn:</span>
                  <span className="font-semibold text-gray-900">
                    {selectedOrder.totalAmount.toLocaleString('vi-VN')} đ
                  </span>
                </div>
              </div>

              {stepError && (
                <div className="p-3 bg-rose-50 border border-rose-200 rounded-lg flex items-center gap-2 text-xs text-rose-700">
                  <AlertCircle className="w-4 h-4 flex-none" />
                  <span>{stepError}</span>
                </div>
              )}

              {/* Bảng dòng hàng có thể trả */}
              <div>
                <div className="flex items-center justify-between mb-2">
                  <h3 className="text-xs font-bold text-gray-700 uppercase tracking-wide">
                    Danh sách sản phẩm có thể trả
                  </h3>
                  <Button
                    variant="secondary"
                    onClick={handleReturnAll}
                    style={{ padding: '4px 10px', fontSize: 11 }}
                  >
                    Trả hết toàn bộ
                  </Button>
                </div>

                {loadingItems ? (
                  <div className="p-8 text-center text-xs text-gray-500">
                    <Loader2 className="w-5 h-5 animate-spin mx-auto mb-2 text-indigo-600" />
                    Đang tải danh sách mặt hàng...
                  </div>
                ) : returnableItems.length === 0 ? (
                  <div className="p-6 text-center text-xs text-gray-400 border border-dashed rounded-xl">
                    Đơn hàng này không còn sản phẩm nào có thể trả (đã trả hết).
                  </div>
                ) : (
                  <div className="border border-gray-200 rounded-xl overflow-hidden">
                    <table className="w-full text-xs text-left border-collapse">
                      <thead className="bg-gray-50 border-b border-gray-200 text-gray-600 font-semibold">
                        <tr>
                          <th className="p-2.5">Sản phẩm / SKU</th>
                          <th className="p-2.5 text-center">ĐVT</th>
                          <th className="p-2.5 text-right">Đơn giá</th>
                          <th className="p-2.5 text-center">Đã bán</th>
                          <th className="p-2.5 text-center">Đã trả</th>
                          <th className="p-2.5 text-center">Còn được trả</th>
                          <th className="p-2.5 text-center w-28">SL trả</th>
                          <th className="p-2.5 text-right w-36">Tiền hoàn</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-100">
                        {returnableItems.map((it) => {
                          const draft = itemDrafts[it.orderItemId] || {
                            quantity: 0,
                            refundAmount: 0,
                            maxRefund: 0,
                          };
                          const isExhausted = it.returnableQuantity <= 0;

                          return (
                            <tr
                              key={it.orderItemId}
                              className={isExhausted ? 'bg-gray-50/50 opacity-60' : ''}
                            >
                              <td className="p-2.5">
                                <div className="font-bold text-gray-900">{it.skuCode}</div>
                                <div className="text-gray-500 text-[11px] truncate max-w-xs">
                                  {it.skuName}
                                </div>
                              </td>
                              <td className="p-2.5 text-center text-gray-600">
                                {it.unitName || '—'}
                              </td>
                              <td className="p-2.5 text-right text-gray-800 font-medium">
                                {it.unitPrice.toLocaleString('vi-VN')} đ
                              </td>
                              <td className="p-2.5 text-center text-gray-600">{it.soldQuantity}</td>
                              <td className="p-2.5 text-center text-rose-600 font-medium">
                                {it.returnedQuantity}
                              </td>
                              <td className="p-2.5 text-center text-emerald-700 font-bold">
                                {it.returnableQuantity}
                              </td>
                              <td className="p-2.5 text-center">
                                <NumberField
                                  value={draft.quantity}
                                  onChange={(val) =>
                                    handleQuantityChange(it.orderItemId, val)
                                  }
                                  decimals={3}
                                  disabled={isExhausted}
                                  placeholder="0"
                                />
                              </td>
                              <td className="p-2.5 text-right">
                                {isMarketplaceOrder ? (
                                  <span className="text-[11px] text-gray-400 italic">
                                    Sàn hoàn tiền
                                  </span>
                                ) : (
                                  <NumberField
                                    value={draft.refundAmount}
                                    onChange={(val) =>
                                      handleRefundAmountChange(it.orderItemId, val)
                                    }
                                    decimals={0}
                                    grouping={true}
                                    disabled={draft.quantity <= 0}
                                    placeholder="0"
                                  />
                                )}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>

              {/* Thông tin hoàn tiền & Phí trả hàng */}
              <div className="flex flex-wrap items-center justify-between p-3 bg-indigo-50/50 border border-indigo-100 rounded-xl text-xs">
                <div>
                  <span className="text-gray-600">Tổng SL trả: </span>
                  <strong className="text-gray-900 font-bold text-sm">{totalReturnQty}</strong>
                </div>
                <div>
                  <span className="text-gray-600">Tổng tiền hoàn cho khách: </span>
                  <strong className="text-indigo-700 font-bold text-sm">
                    {totalActualRefund.toLocaleString('vi-VN')} đ
                  </strong>
                  {returnFee > 0 && (
                    <span className="ml-2 text-rose-600 font-medium">
                      (Phí trả hàng khấu trừ: {returnFee.toLocaleString('vi-VN')} đ)
                    </span>
                  )}
                </div>
              </div>

              {/* Form bổ sung: Kho nhận, Lý do, Phương thức hoàn */}
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-xs">
                {/* Kho nhận */}
                <div>
                  <label className="block font-semibold text-gray-700 mb-1">
                    Kho nhận hàng hoàn lại: <span className="text-rose-500">*</span>
                  </label>
                  <Select
                    value={selectedWarehouseId}
                    onChange={(e) => setSelectedWarehouseId(e.target.value)}
                  >
                    {warehouses.map((wh) => (
                      <option key={wh.id} value={wh.id}>
                        {wh.name}
                      </option>
                    ))}
                  </Select>
                </div>

                {/* Phương thức hoàn tiền */}
                <div>
                  <label className="block font-semibold text-gray-700 mb-1">
                    Phương thức hoàn tiền:
                  </label>
                  {isMarketplaceOrder ? (
                    <div className="p-2 bg-amber-50 border border-amber-200 rounded-lg text-amber-800 text-xs">
                      ℹ️ Đơn hàng từ sàn TMĐT ({selectedOrder.channel}) - Tiền do sàn hoàn cho khách.
                    </div>
                  ) : (
                    <Select
                      value={refundMethod}
                      onChange={(e) => setRefundMethod(e.target.value as any)}
                    >
                      <option value="Cash">Tiền mặt (Cash)</option>
                      <option value="BankTransfer">Chuyển khoản (BankTransfer)</option>
                    </Select>
                  )}
                </div>

                {/* Lý do trả hàng */}
                <div className="md:col-span-2">
                  <label className="block font-semibold text-gray-700 mb-1">
                    Lý do trả hàng: <span className="text-rose-500">*</span>
                  </label>
                  <div className="flex flex-wrap gap-2 mb-2">
                    {COMMON_REASONS.map((r) => (
                      <button
                        key={r}
                        type="button"
                        onClick={() => setReason(r)}
                        className={`px-2.5 py-1 rounded-lg border text-xs font-medium transition-colors ${
                          reason === r
                            ? 'bg-indigo-600 text-white border-indigo-600'
                            : 'bg-white text-gray-700 border-gray-300 hover:bg-gray-50'
                        }`}
                      >
                        {r}
                      </button>
                    ))}
                  </div>
                  {reason === 'Khác' && (
                    <Input
                      type="text"
                      placeholder="Nhập lý do cụ thể..."
                      value={customReason}
                      onChange={(e) => setCustomReason(e.target.value)}
                      className="mb-2"
                    />
                  )}
                  <textarea
                    rows={2}
                    placeholder="Ghi chú thêm về phiếu trả hàng (tùy chọn)..."
                    value={note}
                    onChange={(e) => setNote(e.target.value)}
                    className="input w-full p-2 text-xs"
                  />
                </div>
              </div>
            </div>
          )}

          {/* ================= STEP 3: XEM LẠI & LƯU ================= */}
          {step === 3 && selectedOrder && (
            <div className="space-y-4">
              <div className="p-4 bg-emerald-50 border border-emerald-200 rounded-xl space-y-2 text-xs text-emerald-900">
                <div className="font-bold text-sm text-emerald-800 flex items-center gap-2">
                  <CheckCircle2 className="w-5 h-5 text-emerald-600" />
                  Xác nhận thông tin phiếu trả hàng
                </div>
                <p>
                  Vui lòng kiểm tra kỹ số lượng sản phẩm nhập lại kho và số tiền hoàn trước khi lưu.
                </p>
              </div>

              {submitError && (
                <div className="p-3 bg-rose-50 border border-rose-200 rounded-lg flex items-center gap-2 text-xs text-rose-700">
                  <AlertCircle className="w-4 h-4 flex-none" />
                  <span>{submitError}</span>
                </div>
              )}

              <div className="grid grid-cols-2 md:grid-cols-4 gap-3 p-3 bg-gray-50 border border-gray-200 rounded-xl text-xs">
                <div>
                  <span className="text-gray-400 block">Đơn hàng:</span>
                  <span className="font-bold text-indigo-600">{selectedOrder.number}</span>
                </div>
                <div>
                  <span className="text-gray-400 block">Kho nhận hàng:</span>
                  <span className="font-semibold text-gray-800">
                    {warehouses.find((w) => w.id === selectedWarehouseId)?.name || '—'}
                  </span>
                </div>
                <div>
                  <span className="text-gray-400 block">Tổng số lượng trả:</span>
                  <span className="font-bold text-gray-900 text-sm">{totalReturnQty} sp</span>
                </div>
                <div>
                  <span className="text-gray-400 block">Tổng tiền hoàn:</span>
                  <span className="font-bold text-indigo-700 text-sm">
                    {totalActualRefund.toLocaleString('vi-VN')} đ
                  </span>
                </div>
              </div>

              {/* Bảng các dòng trả sẽ tạo */}
              <div className="border border-gray-200 rounded-xl overflow-hidden">
                <table className="w-full text-xs text-left border-collapse">
                  <thead className="bg-gray-50 border-b border-gray-200 text-gray-600 font-semibold">
                    <tr>
                      <th className="p-2.5">Sản phẩm</th>
                      <th className="p-2.5 text-center">ĐVT</th>
                      <th className="p-2.5 text-center">Số lượng trả</th>
                      <th className="p-2.5 text-right">Đơn giá</th>
                      <th className="p-2.5 text-right">Tiền hoàn</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {Object.values(itemDrafts)
                      .filter((d) => d.quantity > 0)
                      .map((d) => (
                        <tr key={d.item.orderItemId}>
                          <td className="p-2.5">
                            <span className="font-bold text-gray-900">{d.item.skuCode}</span> -{' '}
                            <span className="text-gray-600">{d.item.skuName}</span>
                          </td>
                          <td className="p-2.5 text-center text-gray-600">{d.item.unitName}</td>
                          <td className="p-2.5 text-center font-bold text-indigo-700">
                            {d.quantity}
                          </td>
                          <td className="p-2.5 text-right text-gray-700">
                            {d.item.unitPrice.toLocaleString('vi-VN')} đ
                          </td>
                          <td className="p-2.5 text-right font-semibold text-gray-900">
                            {d.refundAmount.toLocaleString('vi-VN')} đ
                          </td>
                        </tr>
                      ))}
                  </tbody>
                </table>
              </div>

              <div className="p-3 bg-gray-50 border border-gray-200 rounded-xl text-xs space-y-1">
                <div>
                  <strong>Lý do: </strong>
                  <span>{(reason === 'Khác' ? customReason : reason).trim()}</span>
                </div>
                {note.trim() && (
                  <div>
                    <strong>Ghi chú: </strong>
                    <span>{note.trim()}</span>
                  </div>
                )}
                {!isMarketplaceOrder && (
                  <div>
                    <strong>Phương thức hoàn: </strong>
                    <span>{refundMethod === 'Cash' ? 'Tiền mặt' : 'Chuyển khoản'}</span>
                  </div>
                )}
              </div>
            </div>
          )}
        </div>

        {/* Modal Footer */}
        <div className="p-4 border-t border-gray-100 flex items-center justify-between bg-gray-50/50 rounded-b-2xl">
          <div>
            {step === 2 && !preselectedOrder && (
              <Button variant="secondary" onClick={() => setStep(1)} disabled={submitting}>
                <ArrowLeft className="w-4 h-4 mr-1" />
                Quay lại chọn đơn
              </Button>
            )}
            {step === 3 && (
              <Button variant="secondary" onClick={() => setStep(2)} disabled={submitting}>
                <ArrowLeft className="w-4 h-4 mr-1" />
                Sửa dòng trả
              </Button>
            )}
          </div>

          <div className="flex items-center gap-2">
            <Button variant="ghost" onClick={onClose} disabled={submitting}>
              Đóng
            </Button>

            {step === 2 && (
              <Button
                variant="primary"
                onClick={handleGoToReview}
                disabled={totalReturnQty <= 0 || loadingItems}
              >
                Tiếp tục xem lại
                <ArrowRight className="w-4 h-4 ml-1" />
              </Button>
            )}

            {step === 3 && (
              <>
                <Button
                  variant="secondary"
                  onClick={() => handleSubmit(false)}
                  disabled={submitting}
                >
                  {submitting ? 'Đang lưu...' : 'Lưu nháp'}
                </Button>
                <Button
                  variant="primary"
                  onClick={() => handleSubmit(true)}
                  disabled={submitting}
                >
                  {submitting ? (
                    <>
                      <Loader2 className="w-4 h-4 animate-spin mr-1" />
                      Đang xử lý...
                    </>
                  ) : (
                    'Lưu và xác nhận'
                  )}
                </Button>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
