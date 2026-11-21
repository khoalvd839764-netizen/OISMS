import React, { useEffect, useState } from 'react';
import {
  AlertCircle,
  ArrowRight,
  Boxes,
  Clock,
  FileCheck,
  FileText,
  GitBranch,
  Layers,
  Loader2,
  Package,
  Printer,
  RotateCcw,
  ShoppingBag,
  User,
  Warehouse,
  X,
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { goodsIssueService } from '../../services/goodsIssueService';
import { orderService } from '../../services/orderService';
import { salesInvoiceService } from '../../services/salesInvoiceService';
import { salesReturnService } from '../../services/salesReturnService';
import type { GoodsIssue } from '../../types/goodsIssues';
import type { OrderDetail } from '../../types/ordersCenter';
import type { DocumentTrace, SalesInvoiceReceipt } from '../../types/salesInvoices';
import type { SalesReturn } from '../../types/salesReturns';
import type { LedgerDocumentInput } from '../../utils/resolveLedgerDocument';
import { DocumentPrintModal } from '../print/DocumentPrintModal';
import { GoodsIssuePrintPaper } from '../print/GoodsIssuePrintPaper';
import { LegacyOrderPrintPaper } from '../print/LegacyOrderPrintPaper';
import { PrintArea } from '../print/PrintArea';
import { SalesReturnPrintPaper } from '../print/SalesReturnPrintPaper';
import { SalesInvoiceReprintModal } from './SalesInvoiceReprintModal';

interface SalesInvoiceTraceModalProps {
  invoiceId: string;
  invoiceNumber: string;
  /** 'order' khi mã truyền vào là mã đơn hàng, ví dụ mở từ phiếu trả hàng. */
  sourceType?: 'invoice' | 'order';
  onClose: () => void;
}

export const SalesInvoiceTraceModal: React.FC<SalesInvoiceTraceModalProps> = ({
  invoiceId,
  invoiceNumber,
  sourceType = 'invoice',
  onClose,
}) => {
  const { account } = useAuth();
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [trace, setTrace] = useState<DocumentTrace | null>(null);
  const [reprintInvoiceId, setReprintInvoiceId] = useState<string | null>(null);
  const [printDocEntry, setPrintDocEntry] = useState<LedgerDocumentInput | null>(null);
  const [goodsIssues, setGoodsIssues] = useState<GoodsIssue[]>([]);

  // In tất cả (gộp bộ chứng từ trong 1 lần in, mỗi chứng từ sang trang mới - Q8)
  const [bundleOpen, setBundleOpen] = useState(false);
  const [bundleLoading, setBundleLoading] = useState(false);
  const [bundleError, setBundleError] = useState<string | null>(null);
  const [bundleReceipt, setBundleReceipt] = useState<SalesInvoiceReceipt | null>(null);
  const [bundleOrder, setBundleOrder] = useState<OrderDetail | null>(null);
  const [bundleIssues, setBundleIssues] = useState<GoodsIssue[]>([]);
  const [bundleReturns, setBundleReturns] = useState<SalesReturn[]>([]);

  useEffect(() => {
    let isMounted = true;
    const fetchTrace = async () => {
      setLoading(true);
      setError(null);
      try {
        const data = sourceType === 'order'
          ? await salesInvoiceService.traceOrderById(invoiceId)
          : await salesInvoiceService.traceInvoiceById(invoiceId);
        if (isMounted) {
          setTrace(data);
          if (data?.source?.id) {
            goodsIssueService
              .getByOrderId(data.source.id)
              .then((issues) => {
                if (isMounted) setGoodsIssues(issues || []);
              })
              .catch(() => {
                if (isMounted) setGoodsIssues([]);
              });
          }
        }
      } catch (err: any) {
        if (isMounted) {
          const detail =
            err.response?.data?.detail ||
            err.message ||
            'Không thể truy vết chuỗi chứng từ và dòng sổ kho.';
          setError(detail);
        }
      } finally {
        if (isMounted) {
          setLoading(false);
        }
      }
    };

    fetchTrace();
    return () => {
      isMounted = false;
    };
  }, [invoiceId, sourceType]);

  const handlePrintAll = async () => {
    if (!trace) return;
    setBundleOpen(true);
    setBundleLoading(true);
    setBundleError(null);
    setBundleReceipt(null);
    setBundleOrder(null);
    setBundleIssues([]);
    setBundleReturns([]);

    try {
      const salesReturnIds: string[] = ((trace as any).salesReturns || []).map((sr: any) => sr.id);
      const [receiptRes, orderRes, issuesRes, returnsRes] = await Promise.all([
        trace.salesInvoice?.id
          ? salesInvoiceService.getReceiptById(trace.salesInvoice.id).catch(() => null)
          : Promise.resolve(null),
        trace.source?.id
          ? orderService.getOrderById(trace.source.id).catch(() => null)
          : Promise.resolve(null),
        trace.source?.id
          ? goodsIssueService.getByOrderId(trace.source.id).catch(() => [])
          : Promise.resolve([]),
        Promise.all(salesReturnIds.map((id) => salesReturnService.getById(id).catch(() => null))),
      ]);

      setBundleReceipt(receiptRes);
      setBundleOrder(orderRes);
      setBundleIssues(issuesRes || []);
      setBundleReturns(returnsRes.filter((r): r is SalesReturn => r !== null));
    } catch (err: any) {
      setBundleError(err?.message || 'Không thể tải đầy đủ bộ chứng từ để in.');
    } finally {
      setBundleLoading(false);
    }
  };

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-fade-in">
      <div className="bg-white rounded-3xl shadow-2xl border border-slate-200 max-w-4xl w-full overflow-hidden flex flex-col max-h-[92vh]">
        {/* Header */}
        <div className="px-6 py-4 bg-slate-900 text-white flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-indigo-500/20 rounded-xl text-indigo-400">
              <GitBranch className="w-5 h-5" />
            </div>
            <div>
              <h3 className="font-bold text-base leading-tight">
                Truy Vết Chứng Từ & Dòng Sổ Kho Xuất (OUT)
              </h3>
              <p className="text-xs text-slate-400 mt-0.5">
                {sourceType === 'order' ? 'Đơn hàng' : 'Hóa đơn'}: <span className="font-mono text-indigo-300 font-bold">{invoiceNumber}</span>
              </p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            {!loading && !error && trace && (
              <button
                type="button"
                onClick={handlePrintAll}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-bold text-xs transition-colors"
              >
                <Printer className="w-4 h-4" />
                <span>In tất cả</span>
              </button>
            )}
            <button
              type="button"
              onClick={onClose}
              className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition-colors"
            >
              <X className="w-5 h-5" />
            </button>
          </div>
        </div>

        {/* Content */}
        <div className="p-6 overflow-y-auto space-y-6">
          {loading && (
            <div className="flex flex-col items-center justify-center py-16 text-slate-500">
              <Loader2 className="w-10 h-10 animate-spin text-indigo-600 mb-3" />
              <p className="text-sm font-semibold">Đang liên kết chuỗi chứng từ và truy vấn sổ kho...</p>
            </div>
          )}

          {error && (
            <div className="p-5 bg-rose-50 border border-rose-200 rounded-2xl text-rose-800 flex items-start gap-3">
              <AlertCircle className="w-6 h-6 shrink-0 mt-0.5" />
              <div>
                <h4 className="font-bold text-sm">Truy vết không thành công</h4>
                <p className="text-xs mt-1 text-rose-700">{error}</p>
              </div>
            </div>
          )}

          {!loading && !error && trace && (
            <>
              {/* Document Flow Pipeline Visual */}
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4 relative">
                {/* Step 1: Hóa đơn */}
                <div className="bg-gradient-to-br from-indigo-50 to-blue-50/50 border border-indigo-100 rounded-2xl p-4 shadow-xs relative">
                  <div className="flex items-center justify-between mb-3">
                    <span className="text-[11px] font-bold uppercase tracking-wider text-indigo-600 bg-indigo-100/80 px-2.5 py-0.5 rounded-full">
                      1. Hóa Đơn Bán Lẻ
                    </span>
                    <FileText className="w-4 h-4 text-indigo-500" />
                  </div>
                  <div className="font-mono font-bold text-base text-slate-900 mb-1">
                    {trace.salesInvoice?.number || invoiceNumber}
                  </div>
                  <div className="space-y-1 text-xs text-slate-600">
                    <div className="flex items-center gap-1.5">
                      <Clock className="w-3.5 h-3.5 text-slate-400" />
                      <span>{trace.salesInvoice?.issuedAt ? new Date(trace.salesInvoice.issuedAt).toLocaleString('vi-VN') : '-'}</span>
                    </div>
                    {trace.salesInvoice?.issuedByName && (
                      <div className="flex items-center gap-1.5">
                        <User className="w-3.5 h-3.5 text-slate-400" />
                        <span>Người lập: {trace.salesInvoice.issuedByName}</span>
                      </div>
                    )}
                    <div className="pt-2 font-bold text-indigo-900 text-sm flex items-center justify-between gap-2">
                      <span>Tổng tiền: {formatCurrency(trace.salesInvoice?.totalAmount || 0)}</span>
                      {trace.salesInvoice?.id && (
                        <button
                          type="button"
                          onClick={() => setReprintInvoiceId(trace.salesInvoice!.id)}
                          className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg bg-indigo-600 hover:bg-indigo-700 text-white text-[11px] font-semibold"
                        >
                          <Printer className="w-3 h-3" />
                          <span>Xem/In</span>
                        </button>
                      )}
                    </div>
                  </div>
                </div>

                {/* Arrow desktop */}
                <div className="hidden md:flex items-center justify-center absolute left-[31.5%] top-1/2 -translate-y-1/2 z-10">
                  <div className="w-7 h-7 rounded-full bg-white shadow-md border border-slate-200 flex items-center justify-center text-slate-400">
                    <ArrowRight className="w-4 h-4" />
                  </div>
                </div>

                {/* Step 2: Đơn hàng nguồn */}
                <div className="bg-gradient-to-br from-amber-50 to-orange-50/50 border border-amber-100 rounded-2xl p-4 shadow-xs relative">
                  <div className="flex items-center justify-between mb-3">
                    <span className="text-[11px] font-bold uppercase tracking-wider text-amber-700 bg-amber-100 px-2.5 py-0.5 rounded-full">
                      2. Đơn Hàng Nguồn ({trace.source?.type || 'Order'})
                    </span>
                    <ShoppingBag className="w-4 h-4 text-amber-600" />
                  </div>
                  <div className="font-mono font-bold text-base text-slate-900 mb-1">
                    {trace.source?.number || 'Chưa liên kết'}
                  </div>
                  <div className="space-y-1 text-xs text-slate-600">
                    <div className="flex items-center gap-1.5">
                      <Clock className="w-3.5 h-3.5 text-slate-400" />
                      <span>{trace.source?.actedAt ? new Date(trace.source.actedAt).toLocaleString('vi-VN') : '-'}</span>
                    </div>
                    {trace.source?.actorName && (
                      <div className="flex items-center gap-1.5">
                        <User className="w-3.5 h-3.5 text-slate-400" />
                        <span>Người tạo: {trace.source.actorName}</span>
                      </div>
                    )}
                    <div className="pt-2 font-semibold text-amber-900 text-xs flex items-center justify-between gap-2">
                      <span>
                        Trạng thái: <span className="font-bold">{trace.source?.status || 'Completed'}</span>
                      </span>
                      {trace.source?.id && (
                        <button
                          type="button"
                          onClick={() =>
                            setPrintDocEntry({
                              direction: 'OUT',
                              documentType: 'Order',
                              documentId: trace.source!.id,
                              documentNumber: trace.source!.number,
                            })
                          }
                          className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg bg-amber-600 hover:bg-amber-700 text-white text-[11px] font-semibold"
                        >
                          <Printer className="w-3 h-3" />
                          <span>Xem/In</span>
                        </button>
                      )}
                    </div>
                  </div>
                </div>

                {/* Arrow desktop */}
                <div className="hidden md:flex items-center justify-center absolute left-[65%] top-1/2 -translate-y-1/2 z-10">
                  <div className="w-7 h-7 rounded-full bg-white shadow-md border border-slate-200 flex items-center justify-center text-slate-400">
                    <ArrowRight className="w-4 h-4" />
                  </div>
                </div>

                {/* Step 3: Dòng sổ kho */}
                <div className="bg-gradient-to-br from-emerald-50 to-teal-50/50 border border-emerald-100 rounded-2xl p-4 shadow-xs">
                  <div className="flex items-center justify-between mb-3">
                    <span className="text-[11px] font-bold uppercase tracking-wider text-emerald-700 bg-emerald-100 px-2.5 py-0.5 rounded-full">
                      3. Sổ Kho Xuất (OUT)
                    </span>
                    <Boxes className="w-4 h-4 text-emerald-600" />
                  </div>
                  <div className="font-mono font-bold text-base text-slate-900 mb-1">
                    {trace.lines?.length || 0} bút toán OUT
                  </div>
                  <div className="space-y-1 text-xs text-slate-600">
                    <div className="flex items-center gap-1.5">
                      <Layers className="w-3.5 h-3.5 text-slate-400" />
                      <span>Loại bút toán: Xuất bán hàng (OUT)</span>
                    </div>
                    <div className="pt-2 font-semibold text-emerald-900 text-xs space-y-1.5">
                      <div>Tồn kho đã cập nhật tự động</div>
                      {goodsIssues.length > 0 && (
                        <div className="flex flex-wrap gap-1.5">
                          {goodsIssues.map((gi) => (
                            <button
                              key={gi.id}
                              type="button"
                              onClick={() =>
                                setPrintDocEntry({
                                  direction: 'OUT',
                                  documentType: 'Order',
                                  documentId: gi.orderId,
                                  goodsIssueId: gi.id,
                                  documentNumber: gi.number,
                                })
                              }
                              className="inline-flex items-center gap-1 px-2 py-1 rounded-lg bg-emerald-600 hover:bg-emerald-700 text-white text-[11px] font-semibold"
                            >
                              <Printer className="w-3 h-3" />
                              <span>02-VT ({gi.warehouseName})</span>
                            </button>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              </div>

              {/* Step 3 Detail Table: Dòng sổ kho OUT */}
              {(() => {
                const hasCost = trace.lines?.some((l) => l.unitCost !== null && l.unitCost !== undefined);
                return (
                  <div className="border border-slate-200 rounded-2xl overflow-hidden shadow-xs">
                    <div className="bg-slate-50 px-5 py-3 border-b border-slate-200 flex items-center justify-between">
                      <div className="flex items-center gap-2">
                        <FileCheck className="w-4 h-4 text-emerald-600" />
                        <h4 className="font-bold text-sm text-slate-800">
                          Chi Tiết Dòng Sổ Kho Xuất Hàng (Inventory Ledger - OUT)
                        </h4>
                      </div>
                      {hasCost && (
                        <span className="text-xs text-slate-500 font-medium">
                          (Giá vốn WAC chỉ hiển thị cho Chủ cửa hàng)
                        </span>
                      )}
                    </div>

                    {(!trace.lines || trace.lines.length === 0) ? (
                      <div className="p-8 text-center text-slate-500 text-xs">
                        Không tìm thấy dòng sổ kho nào được liên kết với hóa đơn này.
                      </div>
                    ) : (
                      <div className="overflow-x-auto">
                        <table className="w-full text-left text-xs">
                          <thead className="bg-slate-100/70 border-b border-slate-200 text-slate-600 font-bold uppercase text-[10px] tracking-wider">
                            <tr>
                              <th className="py-2.5 px-3">Bút toán #</th>
                              <th className="py-2.5 px-3">Mặt hàng / SKU</th>
                              <th className="py-2.5 px-3">Kho hàng</th>
                              <th className="py-2.5 px-3 text-right">Lượng giao dịch</th>
                              <th className="py-2.5 px-3 text-right">Lượng xuất gốc</th>
                              {hasCost && <th className="py-2.5 px-3 text-right">Giá vốn (WAC)</th>}
                              <th className="py-2.5 px-3 text-right">Tồn sau xuất</th>
                              <th className="py-2.5 px-3 text-right">Thời gian ghi sổ</th>
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-slate-100 font-mono">
                            {trace.lines.map((line, idx) => (
                              <tr key={line.ledgerId || idx} className="hover:bg-slate-50/80 transition-colors">
                                <td className="py-2.5 px-3 text-slate-700">
                                  <span className="font-bold text-slate-900">#{line.sequenceNo}</span>
                                  <div className="text-[10px] text-slate-400 truncate max-w-[100px]" title={line.ledgerId}>
                                    {line.ledgerId.slice(0, 8)}...
                                  </div>
                                </td>
                                <td className="py-2.5 px-3 font-sans">
                                  <div className="flex items-center gap-1.5 font-bold font-mono text-slate-800">
                                    <Package className="w-3.5 h-3.5 text-slate-400 shrink-0" />
                                    <span>{line.skuCode}</span>
                                  </div>
                                </td>
                                <td className="py-2.5 px-3 font-sans text-slate-700">
                                  <div className="flex items-center gap-1.5">
                                    <Warehouse className="w-3.5 h-3.5 text-slate-400 shrink-0" />
                                    <span>{line.warehouseName}</span>
                                  </div>
                                </td>
                                <td className="py-2.5 px-3 text-right font-sans">
                                  {line.orderItemQuantity != null ? (
                                    <span className="font-semibold text-slate-800">
                                      {line.orderItemQuantity.toLocaleString('vi-VN')} {line.unitName || ''}
                                    </span>
                                  ) : (
                                    <span className="text-slate-400">-</span>
                                  )}
                                </td>
                                <td className="py-2.5 px-3 text-right font-bold text-rose-600">
                                  -{Math.abs(line.quantity).toLocaleString('vi-VN')} (gốc)
                                </td>
                                {hasCost && (
                                  <td className="py-2.5 px-3 text-right text-slate-700">
                                    {line.unitCost != null ? formatCurrency(line.unitCost) : '—'}
                                  </td>
                                )}
                                <td className="py-2.5 px-3 text-right font-semibold text-slate-900">
                                  {line.balanceAfter.toLocaleString('vi-VN')}
                                </td>
                                <td className="py-2.5 px-3 text-right font-sans text-slate-500 text-[11px]">
                                  {new Date(line.createdAt).toLocaleString('vi-VN')}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    )}
                  </div>
                );
              })()}

              {/* Step 4: Phiếu trả hàng & Dòng sổ kho hoàn lại (IN - CS-01) */}
              {(trace as any).salesReturns && (trace as any).salesReturns.length > 0 && (
                <div className="space-y-4">
                  <div className="flex items-center gap-2">
                    <RotateCcw className="w-5 h-5 text-indigo-600" />
                    <h4 className="font-bold text-sm text-slate-800">
                      Phiếu Trả Hàng &amp; Dòng Sổ Kho Nhập Lại (IN)
                    </h4>
                  </div>

                  {(trace as any).salesReturns.map((sr: any) => (
                    <div key={sr.id} className="border border-indigo-200 rounded-2xl overflow-hidden shadow-xs bg-white">
                      <div className="bg-indigo-50/70 px-5 py-3 border-b border-indigo-200 flex flex-wrap items-center justify-between gap-2 text-xs">
                        <div className="flex items-center gap-2">
                          <span className="font-bold text-indigo-900 text-sm">{sr.number}</span>
                          <span className={`px-2 py-0.5 rounded text-[10px] font-bold ${
                            sr.status === 'Confirmed' ? 'bg-emerald-100 text-emerald-800' :
                            sr.status === 'Cancelled' ? 'bg-rose-100 text-rose-800' : 'bg-gray-100 text-gray-700'
                          }`}>
                            {sr.status === 'Confirmed' ? 'Đã xác nhận' : sr.status === 'Cancelled' ? 'Đã hủy' : 'Nháp'}
                          </span>
                          <span className="text-slate-600">
                            Hoàn: <strong className="text-indigo-700">{formatCurrency(sr.totalRefundAmount || 0)}</strong>
                          </span>
                        </div>
                        <div className="text-slate-500 text-[11px] flex items-center gap-2">
                          <span>
                            {sr.confirmedAt
                              ? `Xác nhận lúc: ${new Date(sr.confirmedAt).toLocaleString('vi-VN')} (${sr.confirmedByName || sr.confirmedBy || 'Hệ thống'})`
                              : `Tạo lúc: ${new Date(sr.createdAt).toLocaleString('vi-VN')}`}
                          </span>
                          <button
                            type="button"
                            onClick={() =>
                              setPrintDocEntry({
                                direction: 'IN',
                                documentType: 'SalesReturn',
                                documentId: sr.id,
                                documentNumber: sr.number,
                              })
                            }
                            className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg bg-indigo-600 hover:bg-indigo-700 text-white text-[11px] font-semibold"
                          >
                            <Printer className="w-3 h-3" />
                            <span>Xem/In</span>
                          </button>
                        </div>
                      </div>

                      {(!sr.lines || sr.lines.length === 0) ? (
                        <div className="p-4 text-center text-slate-400 text-xs">
                          Chưa có dòng sổ kho IN (phiếu chưa được xác nhận nhập kho).
                        </div>
                      ) : (() => {
                        const srHasCost = sr.lines.some((l: any) => l.unitCost !== null && l.unitCost !== undefined);
                        return (
                          <div className="overflow-x-auto">
                            <table className="w-full text-left text-xs">
                              <thead className="bg-slate-100/70 border-b border-slate-200 text-slate-600 font-bold uppercase text-[10px] tracking-wider">
                                <tr>
                                  <th className="py-2.5 px-3">Bút toán #</th>
                                  <th className="py-2.5 px-3">Mặt hàng / SKU</th>
                                  <th className="py-2.5 px-3">Kho hàng</th>
                                  <th className="py-2.5 px-3 text-right">Lượng nhập lại (gốc)</th>
                                  {srHasCost && <th className="py-2.5 px-3 text-right">Giá vốn (WAC)</th>}
                                  <th className="py-2.5 px-3 text-right">Tồn sau nhập</th>
                                  <th className="py-2.5 px-3 text-right">Thời gian ghi sổ</th>
                                </tr>
                              </thead>
                              <tbody className="divide-y divide-slate-100 font-mono">
                                {sr.lines.map((line: any, idx: number) => (
                                  <tr key={line.ledgerId || idx} className="hover:bg-slate-50/80 transition-colors">
                                    <td className="py-2.5 px-3 text-slate-700">
                                      <span className="font-bold text-slate-900">#{line.sequenceNo}</span>
                                      <div className="text-[10px] text-slate-400 truncate max-w-[100px]" title={line.ledgerId}>
                                        {line.ledgerId.slice(0, 8)}...
                                      </div>
                                    </td>
                                    <td className="py-2.5 px-3 font-sans">
                                      <div className="flex items-center gap-1.5 font-bold font-mono text-slate-800">
                                        <Package className="w-3.5 h-3.5 text-slate-400 shrink-0" />
                                        <span>{line.skuCode}</span>
                                      </div>
                                    </td>
                                    <td className="py-2.5 px-3 font-sans text-slate-700">
                                      <div className="flex items-center gap-1.5">
                                        <Warehouse className="w-3.5 h-3.5 text-slate-400 shrink-0" />
                                        <span>{line.warehouseName}</span>
                                      </div>
                                    </td>
                                    <td className="py-2.5 px-3 text-right font-bold text-emerald-600">
                                      +{Math.abs(line.quantity).toLocaleString('vi-VN')} (gốc)
                                    </td>
                                    {srHasCost && (
                                      <td className="py-2.5 px-3 text-right text-slate-700">
                                        {line.unitCost != null ? formatCurrency(line.unitCost) : '—'}
                                      </td>
                                    )}
                                    <td className="py-2.5 px-3 text-right font-semibold text-slate-900">
                                      {line.balanceAfter.toLocaleString('vi-VN')}
                                    </td>
                                    <td className="py-2.5 px-3 text-right font-sans text-slate-500 text-[11px]">
                                      {new Date(line.createdAt).toLocaleString('vi-VN')}
                                    </td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        );
                      })()}
                    </div>
                  ))}
                </div>
              )}
            </>
          )}
        </div>

        {/* Footer */}
        <div className="px-6 py-3.5 bg-slate-50 border-t border-slate-200 flex justify-end">
          <button
            type="button"
            onClick={onClose}
            className="px-5 py-2 bg-slate-200 hover:bg-slate-300 text-slate-800 font-semibold rounded-xl text-xs transition-colors"
          >
            Đóng
          </button>
        </div>
      </div>

      {reprintInvoiceId && (
        <SalesInvoiceReprintModal
          invoiceId={reprintInvoiceId}
          onClose={() => setReprintInvoiceId(null)}
        />
      )}

      <DocumentPrintModal
        isOpen={Boolean(printDocEntry)}
        entry={printDocEntry}
        tenantName={account?.tenantName}
        printedByName={account?.name || account?.email}
        onClose={() => setPrintDocEntry(null)}
      />

      {bundleOpen && (
        <div className="fixed inset-0 z-60 flex items-center justify-center p-4 bg-slate-900/70 backdrop-blur-xs no-print-bg">
          <style>{`
            @media print {
              html, body { width: auto !important; }
              .print-container, .doc-print-wrapper {
                width: 100% !important;
                max-width: 190mm !important;
                margin: 0 auto !important;
                padding: 0 !important;
              }
              .doc-print-paper {
                border: none !important;
                box-shadow: none !important;
                padding: 0 !important;
                margin: 0 !important;
              }
              .doc-print-page-break {
                break-after: page;
                page-break-after: always;
              }
            }
            @page { size: A4 portrait; margin: 12mm; }
          `}</style>
          <div className="bg-white rounded-2xl shadow-2xl border border-slate-200 max-w-4xl w-full overflow-hidden flex flex-col max-h-[95vh] no-print-border">
            <div className="px-5 py-3.5 text-white flex items-center justify-between no-print bg-slate-900">
              <div className="flex items-center gap-2 font-bold text-sm">
                <Printer className="w-5 h-5 text-emerald-400" />
                <span>In gộp bộ chứng từ truy vết ({invoiceNumber})</span>
              </div>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  disabled={bundleLoading}
                  onClick={() => window.print()}
                  className="inline-flex items-center gap-1.5 px-4 py-1.5 text-white bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50 rounded-xl font-bold text-xs"
                >
                  <Printer className="w-4 h-4" />
                  <span>In tất cả</span>
                </button>
                <button
                  type="button"
                  onClick={() => setBundleOpen(false)}
                  className="p-1 rounded-lg text-slate-300 hover:text-white hover:bg-slate-800"
                >
                  <X className="w-5 h-5" />
                </button>
              </div>
            </div>
            <div className="p-6 overflow-y-auto bg-slate-100/60 flex justify-center min-h-[320px]">
              {bundleLoading ? (
                <div className="flex flex-col items-center justify-center py-14 text-slate-600">
                  <Loader2 className="w-8 h-8 animate-spin text-indigo-600 mb-2" />
                  <span className="text-sm font-medium">Đang tải bộ chứng từ để in gộp...</span>
                </div>
              ) : bundleError ? (
                <div className="p-4 bg-rose-50 border border-rose-200 rounded-xl text-rose-700 text-xs font-medium self-center">
                  {bundleError}
                </div>
              ) : (
                <PrintArea>
                  <div className="doc-print-wrapper space-y-6 w-full max-w-[760px]">
                    {(() => {
                      const hasOrder = Boolean(bundleOrder && bundleOrder.channel !== 'POS');
                      const hasIssues = bundleIssues.length > 0;
                      const hasReturns = bundleReturns.length > 0;
                      const printedBy = account?.name || account?.email;
                      return (
                        <>
                          {bundleReceipt && (
                            <LegacyOrderPrintPaper
                              receipt={bundleReceipt}
                              title="HÓA ĐƠN BÁN HÀNG"
                              tenantName={account?.tenantName}
                              printedByName={printedBy}
                              pageBreakAfter={hasOrder || hasIssues || hasReturns}
                            />
                          )}
                          {hasOrder && bundleOrder && (
                            <LegacyOrderPrintPaper
                              order={bundleOrder}
                              title="PHIẾU GIAO HÀNG"
                              tenantName={account?.tenantName}
                              printedByName={printedBy}
                              pageBreakAfter={hasIssues || hasReturns}
                            />
                          )}
                          {bundleIssues.map((gi, idx) => (
                            <GoodsIssuePrintPaper
                              key={gi.id}
                              issue={gi}
                              tenantName={account?.tenantName}
                              printedByName={printedBy}
                              pageBreakAfter={idx < bundleIssues.length - 1 || hasReturns}
                            />
                          ))}
                          {bundleReturns.map((sr, idx) => (
                            <SalesReturnPrintPaper
                              key={sr.id}
                              salesReturn={sr}
                              tenantName={account?.tenantName}
                              printedByName={printedBy}
                              pageBreakAfter={idx < bundleReturns.length - 1}
                            />
                          ))}
                        </>
                      );
                    })()}
                  </div>
                </PrintArea>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
