export type PrintDocumentTemplate =
  | 'goods-receipt'
  | 'goods-issue'
  | 'legacy-order'
  | 'stock-transfer-out'
  | 'stock-transfer-in'
  | 'stock-count'
  | 'sales-return'
  | 'inventory-adjustment'
  | 'unknown';

export interface LedgerDocumentInput {
  id?: string;
  direction: 'IN' | 'OUT';
  documentType?: string | null;
  documentId?: string | null;
  documentNumber?: string | null;
  goodsIssueId?: string | null;
  salesInvoiceId?: string | null;
  skuId?: string | null;
}

export interface ResolvedLedgerDocument {
  template: PrintDocumentTemplate;
  supported: boolean;
  targetId: string | null;
  fallbackOrderId?: string | null;
  documentNumber?: string | null;
  highlightSkuId?: string | null;
  direction: 'IN' | 'OUT';
  note?: string;
  disabledReason?: string;
}

const UNKNOWN_REASON = 'Không xác định được chứng từ nguồn';

/**
 * Hàm thuần ánh xạ một dòng sổ kho (LedgerEntry) sang mẫu chứng từ in tương ứng theo Upgrade 14 (Q6).
 */
export function resolveLedgerDocument(entry: LedgerDocumentInput | null | undefined): ResolvedLedgerDocument {
  const direction: 'IN' | 'OUT' = entry?.direction === 'IN' ? 'IN' : 'OUT';
  const documentType = (entry?.documentType ?? '').trim();
  const documentId = entry?.documentId?.trim() || null;
  const documentNumber = entry?.documentNumber?.trim() || null;
  const goodsIssueId = entry?.goodsIssueId?.trim() || null;
  const salesInvoiceId = entry?.salesInvoiceId?.trim() || null;

  if (!entry || !documentType || documentType === 'Unknown') {
    return {
      template: 'unknown',
      supported: false,
      targetId: null,
      documentNumber,
      direction,
      disabledReason: UNKNOWN_REASON,
    };
  }

  switch (documentType) {
    case 'GoodsReceipt':
      if (!documentId) {
        return { template: 'unknown', supported: false, targetId: null, documentNumber, direction, disabledReason: UNKNOWN_REASON };
      }
      return {
        template: 'goods-receipt',
        supported: true,
        targetId: documentId,
        documentNumber,
        direction,
      };

    case 'Order':
      if (goodsIssueId) {
        return {
          template: 'goods-issue',
          supported: true,
          targetId: goodsIssueId,
          documentNumber,
          direction,
        };
      }
      if (salesInvoiceId || documentId) {
        return {
          template: 'legacy-order',
          supported: true,
          targetId: salesInvoiceId || documentId,
          fallbackOrderId: documentId,
          documentNumber,
          direction,
          note: 'Chứng từ trước khi có phiếu xuất kho',
        };
      }
      return { template: 'unknown', supported: false, targetId: null, documentNumber, direction, disabledReason: UNKNOWN_REASON };

    case 'StockTransfer':
      if (!documentId) {
        return { template: 'unknown', supported: false, targetId: null, documentNumber, direction, disabledReason: UNKNOWN_REASON };
      }
      return {
        template: direction === 'IN' ? 'stock-transfer-in' : 'stock-transfer-out',
        supported: true,
        targetId: documentId,
        documentNumber,
        direction,
      };

    case 'StockCount':
      if (!documentId) {
        return { template: 'unknown', supported: false, targetId: null, documentNumber, direction, disabledReason: UNKNOWN_REASON };
      }
      return {
        template: 'stock-count',
        supported: true,
        targetId: documentId,
        documentNumber,
        highlightSkuId: entry.skuId?.trim() || null,
        direction,
      };

    case 'SalesReturn':
      if (!documentId) {
        return { template: 'unknown', supported: false, targetId: null, documentNumber, direction, disabledReason: UNKNOWN_REASON };
      }
      return {
        template: 'sales-return',
        supported: true,
        targetId: documentId,
        documentNumber,
        direction,
      };

    case 'InventoryAdjustment':
      if (!documentId) {
        return { template: 'unknown', supported: false, targetId: null, documentNumber, direction, disabledReason: UNKNOWN_REASON };
      }
      return {
        template: 'inventory-adjustment',
        supported: true,
        targetId: documentId,
        documentNumber,
        direction,
      };

    default:
      return {
        template: 'unknown',
        supported: false,
        targetId: null,
        documentNumber,
        direction,
        disabledReason: UNKNOWN_REASON,
      };
  }
}
