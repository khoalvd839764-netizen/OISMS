import { useEffect, useState } from "react";
import { goodsReceiptService } from "../../services/goodsReceiptService";
import { readSupplierInvoiceError, supplierInvoiceService } from "../../services/supplierInvoiceService";
import type { GoodsReceiptResponse } from "../../types/goodsReceipt";
import type { SupplierInvoice } from "../../types/supplierInvoice";
import { Badge, Button } from "../ui";
import { InvoiceStatus, formatInvoiceAmount, formatInvoiceDate } from "./SupplierInvoiceDetails";
import "./supplierInvoices.css";

interface Props {
  purchaseOrderId: string;
  onOpenReceipt: (receiptId: string) => void;
}

interface Loaded {
  receipts: GoodsReceiptResponse[];
  invoices: SupplierInvoice[];
}

const receiptStatus = (status: string) =>
  status === "Confirmed" ? <Badge tone="success">Đã nhập kho</Badge>
    : status === "Cancelled" ? <Badge tone="danger">Đã hủy</Badge>
      : <Badge tone="warning">Nháp</Badge>;

/** Các đợt nhận hàng của một phiếu mua và hóa đơn NCC tương ứng. Chỉ đọc; nhập hóa đơn ở chi tiết phiếu nhập. */
export function PurchaseOrderInvoices({ purchaseOrderId, onOpenReceipt }: Props) {
  const [state, setState] = useState<{ data: Loaded } | { error: string } | null>(null);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    let active = true;
    Promise.all([
      goodsReceiptService.getGoodsReceipts({ purchaseOrderId, pageSize: 100 }),
      supplierInvoiceService.list({ purchaseOrderId, pageSize: 100 }),
    ])
      .then(([receipts, invoices]) => {
        if (active) setState({ data: { receipts: receipts.items, invoices: invoices.items } });
      })
      .catch((error) => {
        if (active) setState({ error: readSupplierInvoiceError(error, "Không thể tải phiếu nhập và hóa đơn NCC.").message });
      });
    return () => { active = false; };
  }, [purchaseOrderId, reload]);

  const data = state && "data" in state ? state.data : null;
  const inactive = data?.invoices.filter((i) => i.status === "Cancelled" || i.status === "Rejected") ?? [];

  return (
    <div className="supplier-invoice__po">
      <div className="supplier-invoice__bar">
        <h4>Phiếu nhập và hóa đơn NCC</h4>
        <Button variant="secondary" disabled={!state} onClick={() => { setState(null); setReload((v) => v + 1); }}>Tải lại</Button>
      </div>
      {!state && <p role="status">Đang tải phiếu nhập và hóa đơn...</p>}
      {state && "error" in state && <p role="alert" className="supplier-invoice__error">{state.error}</p>}
      {data && data.receipts.length === 0 && <p>Chưa có phiếu nhập nào từ đơn mua này.</p>}
      {data && data.receipts.length > 0 && (
        <div className="table-wrap supplier-invoice__table">
          <table>
            <thead>
              <tr><th>Phiếu nhập</th><th>Trạng thái nhập</th><th>Hóa đơn NCC</th><th>Trạng thái hóa đơn</th><th>Tổng hóa đơn</th><th>Chênh lệch</th><th></th></tr>
            </thead>
            <tbody>
              {data.receipts.map((receipt) => {
                const invoice = data.invoices.find((i) => i.id === receipt.invoiceId);
                const diff = invoice?.variance?.totalAmountDiff;
                return (
                  <tr key={receipt.id} className={diff ? "supplier-invoice__variance" : undefined}>
                    <td><strong>{receipt.number}</strong><small>{new Date(receipt.createdAt).toLocaleDateString("vi-VN")}</small></td>
                    <td>{receiptStatus(receipt.status)}</td>
                    <td>{invoice ? <><strong>{invoice.invoiceSeries} / {invoice.invoiceNumber}</strong><small>{formatInvoiceDate(invoice.invoiceDate)}</small></> : "Chưa có hóa đơn"}</td>
                    <td>{invoice ? <InvoiceStatus status={invoice.status} /> : "—"}</td>
                    <td>{invoice ? formatInvoiceAmount(invoice.totalAmount) : "—"}</td>
                    <td>{diff == null ? (invoice?.status === "Matched" ? "Khớp" : "—") : formatInvoiceAmount(diff)}</td>
                    <td><Button variant="secondary" onClick={() => onOpenReceipt(receipt.id)}>Xem phiếu nhập</Button></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {inactive.length > 0 && (
        <small>
          Hóa đơn đã hủy hoặc bị từ chối: {inactive.map((i) => `${i.invoiceSeries} / ${i.invoiceNumber} (${i.status === "Cancelled" ? "đã hủy" : "từ chối"})`).join(", ")}
        </small>
      )}
    </div>
  );
}
