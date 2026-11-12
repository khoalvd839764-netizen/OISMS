import { useState, type FormEvent } from "react";
import type { GoodsReceiptItemResponse } from "../../types/goodsReceipt";
import type { SupplierInvoiceDraft } from "../../types/supplierInvoice";
import { Button, Input } from "../ui";

interface Props {
  initial: SupplierInvoiceDraft;
  receiptItems: GoodsReceiptItemResponse[];
  purchasePrices: ReadonlyMap<string, number>;
  busy: boolean;
  retrying: boolean;
  onSubmit: (draft: SupplierInvoiceDraft) => void;
  onClose: () => void;
}

export function SupplierInvoiceForm({ initial, receiptItems, purchasePrices, busy, retrying, onSubmit, onClose }: Props) {
  const [series, setSeries] = useState(initial.invoiceSeries);
  const [number, setNumber] = useState(initial.invoiceNumber);
  const [date, setDate] = useState(initial.invoiceDate);
  const [vat, setVat] = useState(String(initial.vatAmount));
  const [shipping, setShipping] = useState(String(initial.shippingFee));
  const [lines, setLines] = useState(() => receiptItems.map((item) => {
    const billed = initial.items.find((line) => line.goodsReceiptItemId === item.id);
    return {
      item,
      included: billed != null,
      quantity: String(billed?.quantity ?? item.quantity),
      price: String(billed?.unitPrice ?? purchasePrices.get(item.id) ?? item.unitPrice),
    };
  }));
  const [error, setError] = useState("");
  const readOnly = retrying || busy;

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const items = lines.filter((line) => line.included).map(({ item, quantity, price }) => ({
      goodsReceiptItemId: item.id,
      quantity: Number(quantity),
      unitPrice: Number(price),
    }));
    if (!series.trim() || !number.trim() || !items.length) {
      setError("Nhập ký hiệu, số hóa đơn và chọn ít nhất một dòng hàng.");
      return;
    }
    setError("");
    onSubmit({
      invoiceSeries: series.trim(),
      invoiceNumber: number.trim(),
      invoiceDate: date,
      vatAmount: Number(vat),
      shippingFee: Number(shipping),
      items,
    });
  }

  return (
    <form className="supplier-invoice__form" onSubmit={submit}>
      {retrying && <p role="status" className="supplier-invoice__notice">Chưa rõ kết quả lưu. Thử lại với đúng nội dung và mã yêu cầu đã gửi.</p>}
      <div className="supplier-invoice__fields">
        <label>Ký hiệu<Input required maxLength={50} value={series} readOnly={readOnly} onChange={(e) => setSeries(e.target.value)} /></label>
        <label>Số hóa đơn<Input required maxLength={50} value={number} readOnly={readOnly} onChange={(e) => setNumber(e.target.value)} /></label>
        <label>Ngày hóa đơn<Input type="date" required value={date} readOnly={readOnly} onChange={(e) => setDate(e.target.value)} /></label>
        <label>Thuế GTGT (₫)<Input type="number" required min="0" step="0.000001" value={vat} readOnly={readOnly} onChange={(e) => setVat(e.target.value)} /></label>
        <label>Phí vận chuyển (₫)<Input type="number" required min="0" step="0.000001" value={shipping} readOnly={readOnly} onChange={(e) => setShipping(e.target.value)} /></label>
      </div>
      <div className="table-wrap supplier-invoice__table">
        <table>
          <thead><tr><th>SKU / Đơn vị</th><th>SL nhận</th><th>Giá phiếu mua</th><th>Có trên hóa đơn</th><th>SL hóa đơn</th><th>Đơn giá trước thuế</th></tr></thead>
          <tbody>
            {lines.map(({ item, included, quantity, price }) => {
              const change = (patch: Partial<{ included: boolean; quantity: string; price: string }>) =>
                setLines((current) => current.map((line) => line.item.id === item.id ? { ...line, ...patch } : line));
              return (
                <tr key={item.id}>
                  <td><strong>{item.skuCode}</strong><small>{item.productName} · {item.unitName || "Đơn vị gốc"}</small></td>
                  <td>{item.quantity.toLocaleString("vi-VN", { maximumFractionDigits: 3 })}</td>
                  <td>{(purchasePrices.get(item.id) ?? item.unitPrice).toLocaleString("vi-VN", { maximumFractionDigits: 6 })} ₫</td>
                  <td><input type="checkbox" aria-label={`Có trên hóa đơn ${item.skuCode}`} checked={included} disabled={readOnly} onChange={(e) => change({ included: e.target.checked })} /></td>
                  <td><Input type="number" aria-label={`SL hóa đơn ${item.skuCode}`} required={included} disabled={!included} readOnly={readOnly} min="0" step="0.001" value={quantity} onChange={(e) => change({ quantity: e.target.value })} /></td>
                  <td><Input type="number" aria-label={`Đơn giá trước thuế ${item.skuCode}`} required={included} disabled={!included} readOnly={readOnly} min="0" step="0.000001" value={price} onChange={(e) => change({ price: e.target.value })} /></td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {error && <p role="alert" className="supplier-invoice__error">{error}</p>}
      <div className="supplier-invoice__actions">
        <Button type="submit" disabled={busy}>{busy ? "Đang lưu..." : retrying ? "Thử lại lưu nháp hóa đơn" : "Lưu nháp hóa đơn"}</Button>
        <Button type="button" variant="secondary" disabled={busy} onClick={onClose}>Đóng form hóa đơn</Button>
      </div>
    </form>
  );
}
