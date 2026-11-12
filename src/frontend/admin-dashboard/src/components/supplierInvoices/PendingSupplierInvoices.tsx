import { useEffect, useState } from "react";
import { readSupplierInvoiceError, supplierInvoiceService } from "../../services/supplierInvoiceService";
import type { SupplierInvoicePage } from "../../types/supplierInvoice";
import { Button, Modal } from "../ui";
import { formatInvoiceAmount, formatInvoiceDate } from "./SupplierInvoiceDetails";
import "./supplierInvoices.css";

interface Props {
  onOpenReceipt: (id: string) => void;
  onClose: () => void;
}

export function PendingSupplierInvoices({ onOpenReceipt, onClose }: Props) {
  const [page, setPage] = useState(1);
  const [reload, setReload] = useState(0);
  const [state, setState] = useState<{ data: SupplierInvoicePage } | { error: string } | null>(null);
  const pageSize = 20;

  useEffect(() => {
    let active = true;
    supplierInvoiceService.list({ pendingOnly: true, pageIndex: page, pageSize })
      .then((data) => {
        if (active) setState({ data });
      })
      .catch((error) => {
        if (active) setState({ error: readSupplierInvoiceError(error, "Không thể tải chênh lệch chờ duyệt.").message });
      });
    return () => { active = false; };
  }, [page, reload]);

  const data = state && "data" in state ? state.data : null;

  return (
    <Modal title="Chênh lệch chờ duyệt" onClose={onClose} wide>
      <div className="supplier-invoice__queue">
        <div><Button variant="secondary" disabled={!state} onClick={() => { setState(null); setReload((value) => value + 1); }}>Tải lại danh sách</Button></div>
        {!state && <p role="status">Đang tải chênh lệch chờ duyệt...</p>}
        {state && "error" in state && <p role="alert" className="supplier-invoice__error">{state.error}</p>}
        {data?.items.length === 0 && <p>Không có chênh lệch chờ duyệt trên trang này.</p>}
        {Boolean(data?.items.length) && (
          <div className="table-wrap supplier-invoice__table">
            <table>
              <thead><tr><th>Ký hiệu / Số hóa đơn</th><th>Ngày hóa đơn</th><th>Thời điểm</th><th>Lệch tiền hàng</th><th>Thao tác</th></tr></thead>
              <tbody>{data?.items.map((invoice) => (
                <tr key={invoice.id}>
                  <td><strong>{invoice.invoiceSeries} / {invoice.invoiceNumber}</strong></td>
                  <td>{formatInvoiceDate(invoice.invoiceDate)}</td>
                  <td>{invoice.variance?.varianceType === "AtReceipt" ? "Kèm hàng" : "Đến sau"}</td>
                  <td>{invoice.variance ? formatInvoiceAmount(invoice.variance.totalAmountDiff) : "—"}</td>
                  <td><Button variant="secondary" onClick={() => onOpenReceipt(invoice.goodsReceiptId)}>Xem và xử lý</Button></td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        )}
        {data && (page > 1 || data.totalCount > pageSize) && (
          <div className="supplier-invoice__actions">
            <Button variant="secondary" disabled={page <= 1} onClick={() => { setState(null); setPage(page - 1); }}>Trang trước</Button>
            <span>Trang {page}</span>
            <Button variant="secondary" disabled={page * pageSize >= data.totalCount} onClick={() => { setState(null); setPage(page + 1); }}>Trang sau</Button>
          </div>
        )}
      </div>
    </Modal>
  );
}
