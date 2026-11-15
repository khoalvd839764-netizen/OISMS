import { useEffect, useState } from "react";
import { readSupplierInvoiceError, supplierInvoiceService } from "../../services/supplierInvoiceService";
import type { SupplierInvoiceFilter, SupplierInvoicePage, SupplierInvoiceStatus } from "../../types/supplierInvoice";
import { Button, Input, Modal } from "../ui";
import { InvoiceStatus, formatInvoiceAmount, formatInvoiceDate } from "./SupplierInvoiceDetails";
import "./supplierInvoices.css";

interface Props {
  suppliers: ReadonlyArray<{ id: string; name: string }>;
  onOpenReceipt: (receiptId: string) => void;
  onClose: () => void;
}

const statuses: Array<{ value: SupplierInvoiceStatus; label: string }> = [
  { value: "Draft", label: "Nháp" },
  { value: "Matched", label: "Đã khớp" },
  { value: "PendingApproval", label: "Chờ duyệt" },
  { value: "Approved", label: "Đã duyệt" },
  { value: "Rejected", label: "Đã từ chối" },
  { value: "Cancelled", label: "Đã hủy" },
];

const pageSize = 20;

/** Lịch sử hóa đơn nhà cung cấp, lọc theo NCC, trạng thái và ngày hóa đơn. */
export function SupplierInvoiceHistory({ suppliers, onOpenReceipt, onClose }: Props) {
  const [supplierId, setSupplierId] = useState("");
  const [status, setStatus] = useState<SupplierInvoiceStatus | "">("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [filter, setFilter] = useState<SupplierInvoiceFilter>({ pageIndex: 1, pageSize });
  const [state, setState] = useState<{ data: SupplierInvoicePage } | { error: string } | null>(null);

  useEffect(() => {
    let active = true;
    supplierInvoiceService.list(filter)
      .then((data) => { if (active) setState({ data }); })
      .catch((error) => {
        if (active) setState({ error: readSupplierInvoiceError(error, "Không thể tải lịch sử hóa đơn NCC.").message });
      });
    return () => { active = false; };
  }, [filter]);

  const apply = () => {
    if (fromDate && toDate && fromDate > toDate) {
      setState({ error: "Ngày bắt đầu phải trước hoặc bằng ngày kết thúc." });
      return;
    }
    setState(null);
    setFilter({
      supplierId: supplierId || undefined,
      status: status || undefined,
      fromDate: fromDate || undefined,
      toDate: toDate || undefined,
      pageIndex: 1,
      pageSize,
    });
  };
  const goTo = (pageIndex: number) => { setState(null); setFilter((f) => ({ ...f, pageIndex })); };

  const data = state && "data" in state ? state.data : null;
  const page = filter.pageIndex ?? 1;

  return (
    <Modal title="Lịch sử hóa đơn NCC" onClose={onClose} wide>
      <div className="supplier-invoice__queue">
        <form className="supplier-invoice__fields" onSubmit={(e) => { e.preventDefault(); apply(); }}>
          <label>Nhà cung cấp
            <select className="input" value={supplierId} onChange={(e) => setSupplierId(e.target.value)}>
              <option value="">Tất cả</option>
              {suppliers.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          </label>
          <label>Trạng thái
            <select className="input" value={status} onChange={(e) => setStatus(e.target.value as SupplierInvoiceStatus | "")}>
              <option value="">Tất cả</option>
              {statuses.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
            </select>
          </label>
          <label>Từ ngày hóa đơn<Input type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} /></label>
          <label>Đến ngày hóa đơn<Input type="date" value={toDate} onChange={(e) => setToDate(e.target.value)} /></label>
          <div className="supplier-invoice__actions"><Button type="submit" disabled={!state}>Lọc</Button></div>
        </form>
        {!state && <p role="status">Đang tải lịch sử hóa đơn...</p>}
        {state && "error" in state && <p role="alert" className="supplier-invoice__error">{state.error}</p>}
        {data?.items.length === 0 && <p>Không có hóa đơn phù hợp bộ lọc.</p>}
        {Boolean(data?.items.length) && (
          <div className="table-wrap supplier-invoice__table">
            <table>
              <thead>
                <tr><th>Ký hiệu / Số</th><th>Ngày hóa đơn</th><th>Nhà cung cấp</th><th>Phiếu nhập / Đơn mua</th><th>Tổng hóa đơn</th><th>Trạng thái</th><th>Chênh lệch</th><th></th></tr>
              </thead>
              <tbody>{data?.items.map((invoice) => (
                <tr key={invoice.id} className={invoice.variance ? "supplier-invoice__variance" : undefined}>
                  <td><strong>{invoice.invoiceSeries} / {invoice.invoiceNumber}</strong></td>
                  <td>{formatInvoiceDate(invoice.invoiceDate)}</td>
                  <td>{invoice.supplierName ?? "—"}</td>
                  <td><strong>{invoice.goodsReceiptNumber ?? "—"}</strong><small>{invoice.purchaseOrderNumber ?? ""}</small></td>
                  <td>{formatInvoiceAmount(invoice.totalAmount)}</td>
                  <td><InvoiceStatus status={invoice.status} /></td>
                  <td>{invoice.variance
                    ? <>{formatInvoiceAmount(invoice.variance.totalAmountDiff)}<small>{invoice.variance.varianceType === "AtReceipt" ? "Kèm hàng" : "Đến sau"}</small></>
                    : invoice.status === "Matched" ? "Khớp" : "—"}</td>
                  <td><Button variant="secondary" onClick={() => onOpenReceipt(invoice.goodsReceiptId)}>Xem phiếu nhập</Button></td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        )}
        {data && (page > 1 || data.totalCount > pageSize) && (
          <div className="supplier-invoice__actions">
            <Button variant="secondary" disabled={page <= 1} onClick={() => goTo(page - 1)}>Trang trước</Button>
            <span>Trang {page} · {data.totalCount} hóa đơn</span>
            <Button variant="secondary" disabled={page * pageSize >= data.totalCount} onClick={() => goTo(page + 1)}>Trang sau</Button>
          </div>
        )}
      </div>
    </Modal>
  );
}
