import type { GoodsReceiptItemResponse } from "../../types/goodsReceipt";
import type { ComparisonValue, SupplierInvoice, SupplierInvoiceStatus } from "../../types/supplierInvoice";
import { Badge } from "../ui";

export const formatInvoiceAmount = (value: number) =>
  `${value.toLocaleString("vi-VN", { maximumFractionDigits: 9 })} ₫`;

export const formatInvoiceDate = (value: string) => value.split("-").reverse().join("/");

const statusLabels: Record<SupplierInvoiceStatus, string> = {
  Draft: "Nháp",
  Matched: "Đã khớp",
  PendingApproval: "Chờ duyệt",
  Approved: "Đã duyệt",
  Rejected: "Đã từ chối",
  Cancelled: "Đã hủy",
};

export function InvoiceStatus({ status }: { status: SupplierInvoiceStatus }) {
  const tone = status === "PendingApproval" || status === "Draft" ? "warning"
    : status === "Rejected" || status === "Cancelled" ? "danger" : "success";
  return <Badge tone={tone}>{statusLabels[status]}</Badge>;
}

function ComparisonCell({ value }: { value: ComparisonValue | null }) {
  if (value == null) return <>Thiếu dòng hóa đơn</>;
  return (
    <>
      <strong>SL {value.quantity.toLocaleString("vi-VN", { maximumFractionDigits: 3 })}</strong>
      <small>Đơn giá {value.unitPrice == null ? "—" : formatInvoiceAmount(value.unitPrice)}</small>
    </>
  );
}

export function SupplierInvoiceDetails({ invoice, items }: { invoice: SupplierInvoice; items: GoodsReceiptItemResponse[] }) {
  const byId = new Map(items.map((item) => [item.id, item]));
  const variance = invoice.variance;
  return (
    <>
      <div className="supplier-invoice__bar">
        <div><strong>{invoice.invoiceSeries} / {invoice.invoiceNumber}</strong> · {formatInvoiceDate(invoice.invoiceDate)}</div>
        <InvoiceStatus status={invoice.status} />
      </div>
      <div className="supplier-invoice__totals">
        <span>Tiền hàng <strong>{formatInvoiceAmount(invoice.subtotal)}</strong></span>
        <span>Thuế GTGT {formatInvoiceAmount(invoice.vatAmount)}</span>
        <span>Vận chuyển {formatInvoiceAmount(invoice.shippingFee)}</span>
        <span>Tổng hóa đơn <strong>{formatInvoiceAmount(invoice.totalAmount)}</strong></span>
      </div>
      {invoice.submittedAt && (
        <>
          <div className="table-wrap supplier-invoice__table">
            <table aria-label="Đối chiếu hóa đơn ba bên">
              <thead><tr><th>SKU / Đơn vị</th><th>Phiếu mua</th><th>Thực nhận</th><th>Hóa đơn</th><th>Kết quả</th></tr></thead>
              <tbody>
                {invoice.comparison?.map((line) => {
                  const item = byId.get(line.goodsReceiptItemId);
                  const differs = Boolean(line.quantityDiff || line.priceDiff);
                  const received = item ? { quantity: item.quantity, unitPrice: item.unitPrice } : line.goodsReceipt;
                  return (
                    <tr key={line.goodsReceiptItemId} className={differs ? "supplier-invoice__variance" : undefined}>
                      <td><strong>{item?.skuCode || line.skuId}</strong><small>{line.unitName || "Đơn vị gốc"}</small></td>
                      <td><ComparisonCell value={line.purchaseOrder} /></td>
                      <td><ComparisonCell value={received} /></td>
                      <td><ComparisonCell value={line.invoice} /></td>
                      <td>
                        {differs ? (
                          <>
                            <Badge tone="warning">Chênh lệch</Badge>
                            {Boolean(line.quantityDiff) && <small>Lệch SL {line.quantityDiff?.toLocaleString("vi-VN", { maximumFractionDigits: 3 })}</small>}
                            {line.priceDiff != null && line.priceDiff !== 0 && <small>Lệch đơn giá {formatInvoiceAmount(line.priceDiff)}</small>}
                            {line.amountDiff != null && <small>Lệch tiền {formatInvoiceAmount(line.amountDiff)}</small>}
                          </>
                        ) : <Badge tone="success">Khớp</Badge>}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          {!invoice.comparison?.length && <p>Chưa có dòng đối chiếu.</p>}
        </>
      )}
      {variance && (
        <p><strong>{variance.varianceType === "AtReceipt" ? "Hóa đơn kèm hàng" : "Hóa đơn đến sau"}</strong>
          {" · "}Chênh lệch tiền hàng {formatInvoiceAmount(variance.totalAmountDiff)}
          {variance.decisionNote && <><br />Ghi chú quyết định: {variance.decisionNote}</>}
        </p>
      )}
      {invoice.cancelReason && <p>Lý do hủy: {invoice.cancelReason}</p>}
    </>
  );
}
