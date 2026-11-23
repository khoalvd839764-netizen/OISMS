import React, { useEffect, useMemo, useState } from 'react';
import { AlertCircle, FileText, Loader2, Printer, X } from 'lucide-react';
import { goodsIssueService } from '../../services/goodsIssueService';
import { goodsReceiptService } from '../../services/goodsReceiptService';
import { inventoryAdjustmentService } from '../../services/inventoryAdjustmentService';
import { orderService } from '../../services/orderService';
import { salesInvoiceService } from '../../services/salesInvoiceService';
import { salesReturnService } from '../../services/salesReturnService';
import { stockCountService } from '../../services/stockCountService';
import { stockTransferService } from '../../services/stockTransferService';
import type { GoodsIssue } from '../../types/goodsIssues';
import type { GoodsReceiptResponse } from '../../types/goodsReceipt';
import type { InventoryAdjustment } from '../../types/inventoryAdjustment';
import type { OrderDetail } from '../../types/ordersCenter';
import type { SalesInvoiceReceipt } from '../../types/salesInvoices';
import type { SalesReturn } from '../../types/salesReturns';
import type { StockCount } from '../../types/stockCount';
import type { StockTransfer } from '../../types/stockTransfer';
import { resolveLedgerDocument, type LedgerDocumentInput } from '../../utils/resolveLedgerDocument';
import { GoodsIssuePrintPaper } from './GoodsIssuePrintPaper';
import { GoodsReceiptPrintPaper } from './GoodsReceiptPrintPaper';
import { InventoryAdjustmentPrintPaper } from './InventoryAdjustmentPrintPaper';
import { LegacyOrderPrintPaper } from './LegacyOrderPrintPaper';
import { PrintArea } from './PrintArea';
import { SalesReturnPrintPaper } from './SalesReturnPrintPaper';
import { StockCountPrintPaper } from './StockCountPrintPaper';
import { StockTransferInPrintPaper } from './StockTransferInPrintPaper';
import { StockTransferOutPrintPaper } from './StockTransferOutPrintPaper';

interface DocumentPrintModalProps {
  isOpen: boolean;
  entry: LedgerDocumentInput | null;
  tenantName?: string;
  printedByName?: string | null;
  onClose: () => void;
  onOpenPurchaseOrder?: (purchaseOrderId: string) => void;
}

const TEMPLATE_TITLES: Record<string, string> = {
  'goods-receipt': 'Phiếu nhập kho (Mẫu 01-VT)',
  'goods-issue': 'Phiếu xuất kho (Mẫu 02-VT)',
  'legacy-order': 'Chứng từ xuất bán (Trước khi có phiếu 02-VT)',
  'stock-transfer-out': 'Phiếu xuất kho kiêm vận chuyển nội bộ',
  'stock-transfer-in': 'Phiếu nhập kho nhận chuyển kho',
  'stock-count': 'Biên bản kiểm kê (Mẫu 05-VT)',
  'sales-return': 'Phiếu nhập kho hàng trả lại',
  'inventory-adjustment': 'Phiếu điều chỉnh cân bằng kho',
  unknown: 'Chứng từ nguồn',
};

export const DocumentPrintModal: React.FC<DocumentPrintModalProps> = ({
  isOpen,
  entry,
  tenantName,
  printedByName,
  onClose,
  onOpenPurchaseOrder,
}) => {
  const resolved = useMemo(() => resolveLedgerDocument(entry), [entry]);

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [goodsReceipt, setGoodsReceipt] = useState<GoodsReceiptResponse | null>(null);
  const [goodsIssue, setGoodsIssue] = useState<GoodsIssue | null>(null);
  const [legacyReceipt, setLegacyReceipt] = useState<SalesInvoiceReceipt | null>(null);
  const [legacyOrder, setLegacyOrder] = useState<OrderDetail | null>(null);
  const [stockTransfer, setStockTransfer] = useState<StockTransfer | null>(null);
  const [stockCount, setStockCount] = useState<StockCount | null>(null);
  const [salesReturn, setSalesReturn] = useState<SalesReturn | null>(null);
  const [adjustment, setAdjustment] = useState<InventoryAdjustment | null>(null);

  useEffect(() => {
    setGoodsReceipt(null);
    setGoodsIssue(null);
    setLegacyReceipt(null);
    setLegacyOrder(null);
    setStockTransfer(null);
    setStockCount(null);
    setSalesReturn(null);
    setAdjustment(null);
    setError(null);

    if (!isOpen || !entry) return;

    if (!resolved.supported || !resolved.targetId) {
      setError(resolved.disabledReason || 'Không xác định được chứng từ nguồn.');
      return;
    }

    let isMounted = true;
    setLoading(true);

    const load = async () => {
      try {
        switch (resolved.template) {
          case 'goods-receipt': {
            const data = await goodsReceiptService.getGoodsReceiptById(resolved.targetId!);
            if (isMounted) setGoodsReceipt(data);
            break;
          }
          case 'goods-issue': {
            const data = await goodsIssueService.getById(resolved.targetId!);
            if (isMounted) setGoodsIssue(data);
            break;
          }
          case 'legacy-order': {
            if (resolved.targetId && resolved.targetId !== resolved.fallbackOrderId) {
              const receiptData = await salesInvoiceService.getReceiptById(resolved.targetId);
              if (isMounted) setLegacyReceipt(receiptData);
            } else if (resolved.fallbackOrderId || resolved.targetId) {
              const orderData = await orderService.getOrderById(
                (resolved.fallbackOrderId || resolved.targetId)!
              );
              if (isMounted) setLegacyOrder(orderData);
            }
            break;
          }
          case 'stock-transfer-out':
          case 'stock-transfer-in': {
            const data = await stockTransferService.getTransferById(resolved.targetId!);
            if (isMounted) setStockTransfer(data);
            break;
          }
          case 'stock-count': {
            const data = await stockCountService.getStockCountById(resolved.targetId!);
            if (isMounted) setStockCount(data);
            break;
          }
          case 'sales-return': {
            const data = await salesReturnService.getById(resolved.targetId!);
            if (isMounted) setSalesReturn(data);
            break;
          }
          case 'inventory-adjustment': {
            const data = await inventoryAdjustmentService.getById(resolved.targetId!);
            if (isMounted) setAdjustment(data);
            break;
          }
        }
      } catch (err: any) {
        if (!isMounted) return;
        const status = err?.response?.status;
        if (status === 403) {
          setError('Bạn không có quyền xem chứng từ này (403 Forbidden).');
        } else if (status === 404) {
          setError('Không tìm thấy chứng từ trong cửa hàng hiện tại (404 Not Found).');
        } else {
          setError(
            err?.response?.data?.detail ||
              err?.response?.data?.message ||
              err?.message ||
              'Không thể tải dữ liệu chứng từ để xem trước.'
          );
        }
      } finally {
        if (isMounted) setLoading(false);
      }
    };

    void load();
    return () => {
      isMounted = false;
    };
  }, [isOpen, entry, resolved]);

  if (!isOpen || !entry) return null;

  const hasLoadedDoc = Boolean(
    goodsReceipt ||
      goodsIssue ||
      legacyReceipt ||
      legacyOrder ||
      stockTransfer ||
      stockCount ||
      salesReturn ||
      adjustment
  );

  return (
    <div className="fixed inset-0 z-60 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs no-print-bg">
      <style>{`
        @media print {
          html, body { width: auto !important; }
          .print-container, .doc-print-wrapper {
            width: 100% !important;
            max-width: 190mm !important;
            margin: 0 auto !important;
            padding: 0 !important;
            border: none !important;
            box-shadow: none !important;
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
          .doc-print-paper tr {
            break-inside: avoid;
            page-break-inside: avoid;
          }
          .no-print-link {
            color: inherit !important;
            text-decoration: none !important;
          }
        }
        @page { size: A4 portrait; margin: 12mm; }
      `}</style>

      <div className="bg-white rounded-2xl shadow-2xl border border-slate-200 max-w-4xl w-full overflow-hidden flex flex-col max-h-[95vh] no-print-border">
        <div className="px-5 py-3.5 text-white flex items-center justify-between no-print bg-slate-900">
          <div className="flex items-center gap-2">
            <FileText className="w-5 h-5 text-emerald-400" />
            <span className="font-bold text-sm">
              {TEMPLATE_TITLES[resolved.template] || 'Xem & In Chứng Từ'}
              {resolved.documentNumber ? ` — ${resolved.documentNumber}` : ''}
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

        <div className="p-3 border-b flex items-center justify-between no-print bg-indigo-50 border-indigo-100">
          <div className="text-xs text-slate-600 font-medium">
            Xem trước mẫu in chuẩn. Các cột giá nhập/giá vốn tự động ẩn nếu tài khoản không có quyền xem.
          </div>
          <button
            type="button"
            disabled={loading || !hasLoadedDoc}
            onClick={() => window.print()}
            className="inline-flex items-center gap-1.5 px-4 py-1.5 text-white bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50 rounded-xl font-bold text-xs shadow-md transition-all"
          >
            <Printer className="w-4 h-4" />
            <span>In chứng từ</span>
          </button>
        </div>

        <div className="p-6 overflow-y-auto bg-slate-100/60 flex justify-center min-h-[320px]">
          {loading ? (
            <div className="flex flex-col items-center justify-center py-14 text-slate-600">
              <Loader2 className="w-8 h-8 animate-spin text-indigo-600 mb-2" />
              <span className="text-sm font-medium">Đang tải dữ liệu chứng từ...</span>
            </div>
          ) : error ? (
            <div className="p-4 bg-rose-50 border border-rose-200 rounded-xl text-rose-700 text-xs font-medium flex items-start gap-2.5 max-w-md self-center">
              <AlertCircle className="w-5 h-5 shrink-0 mt-0.5" />
              <div>{error}</div>
            </div>
          ) : hasLoadedDoc ? (
            <PrintArea>
              <div className="doc-print-wrapper w-full max-w-[760px]">
                {resolved.template === 'goods-receipt' && goodsReceipt && (
                  <GoodsReceiptPrintPaper
                    receipt={goodsReceipt}
                    tenantName={tenantName}
                    printedByName={printedByName}
                    onOpenPurchaseOrder={onOpenPurchaseOrder}
                  />
                )}
                {resolved.template === 'goods-issue' && goodsIssue && (
                  <GoodsIssuePrintPaper
                    issue={goodsIssue}
                    tenantName={tenantName}
                    printedByName={printedByName}
                  />
                )}
                {resolved.template === 'legacy-order' && (legacyReceipt || legacyOrder) && (
                  <LegacyOrderPrintPaper
                    receipt={legacyReceipt}
                    order={legacyOrder}
                    note={resolved.note}
                    tenantName={tenantName}
                    printedByName={printedByName}
                  />
                )}
                {resolved.template === 'stock-transfer-out' && stockTransfer && (
                  <StockTransferOutPrintPaper
                    transfer={stockTransfer}
                    tenantName={tenantName}
                    printedByName={printedByName}
                  />
                )}
                {resolved.template === 'stock-transfer-in' && stockTransfer && (
                  <StockTransferInPrintPaper
                    transfer={stockTransfer}
                    tenantName={tenantName}
                    printedByName={printedByName}
                  />
                )}
                {resolved.template === 'stock-count' && stockCount && (
                  <StockCountPrintPaper
                    stockCount={stockCount}
                    highlightSkuId={resolved.highlightSkuId}
                    tenantName={tenantName}
                    printedByName={printedByName}
                  />
                )}
                {resolved.template === 'sales-return' && salesReturn && (
                  <SalesReturnPrintPaper
                    salesReturn={salesReturn}
                    tenantName={tenantName}
                    printedByName={printedByName}
                  />
                )}
                {resolved.template === 'inventory-adjustment' && adjustment && (
                  <InventoryAdjustmentPrintPaper
                    adjustment={adjustment}
                    tenantName={tenantName}
                    printedByName={printedByName}
                  />
                )}
              </div>
            </PrintArea>
          ) : (
            <div className="py-12 text-sm text-slate-500">Không có dữ liệu chứng từ.</div>
          )}
        </div>
      </div>
    </div>
  );
};
