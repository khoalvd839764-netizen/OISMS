import type { PosGoodsIssue, ReceiptData } from '../types/pos';

/**
 * Nút "In phiếu xuất kho" chỉ hiển thị khi checkout đã thành công trên server
 * (không hiển thị trên phiếu tạm tính, khi lỗi, hoặc khi kết quả chưa rõ).
 */
export function canPrintGoodsIssue(
  receipt: Pick<ReceiptData, 'isDraft' | 'orderId'> | null | undefined
): boolean {
  if (!receipt || receipt.isDraft) return false;
  return typeof receipt.orderId === 'string' && receipt.orderId.trim().length > 0;
}

export interface GoodsIssuePrintPage<T = PosGoodsIssue> {
  issue: T;
  pageNumber: number;
  totalPages: number;
  hasPageBreakAfter: boolean;
}

/**
 * Tách danh sách phiếu xuất kho theo từng kho thành từng trang riêng biệt (mỗi kho một trang).
 */
export function splitGoodsIssuePages<T>(issues: T[] | null | undefined): GoodsIssuePrintPage<T>[] {
  if (!Array.isArray(issues) || issues.length === 0) return [];
  const totalPages = issues.length;
  return issues.map((issue, idx) => ({
    issue,
    pageNumber: idx + 1,
    totalPages,
    hasPageBreakAfter: idx < totalPages - 1,
  }));
}

export function getGoodsIssueFetchErrorMessage(status?: number, detail?: string): string {
  if (status === 404) {
    return 'Không tìm thấy phiếu xuất kho của đơn này';
  }
  if (detail && detail.trim()) {
    return detail.trim();
  }
  return 'Không thể tải phiếu xuất kho. Vui lòng kiểm tra kết nối và thử lại.';
}
