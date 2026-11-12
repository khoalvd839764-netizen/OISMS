import { useState, type ClipboardEvent, type FormEvent } from "react";
import type { GoodsReceiptItemResponse } from "../../types/goodsReceipt";
import type { SupplierInvoiceDraft } from "../../types/supplierInvoice";
import { NumberField } from "../NumberField";
import { Button, Input } from "../ui";
import { formatInvoiceAmount } from "./SupplierInvoiceDetails";

interface Props {
  initial: SupplierInvoiceDraft;
  receiptItems: GoodsReceiptItemResponse[];
  purchasePrices: ReadonlyMap<string, number>;
  busy: boolean;
  retrying: boolean;
  onSubmit: (draft: SupplierInvoiceDraft) => void;
  onClose: () => void;
}

interface Line {
  item: GoodsReceiptItemResponse;
  included: boolean;
  quantity: number | null;
  price: number | null;
}

const VAT_RATES = [0, 5, 8, 10];

/** Ký hiệu hóa đơn điện tử: chữ in hoa và số, bỏ khoảng trắng. */
const normalizeSeries = (value: string) => value.toUpperCase().replace(/\s+/g, "");
/** Số hóa đơn: chỉ giữ chữ số, giữ số 0 ở đầu. */
const normalizeNumber = (value: string) => value.replace(/\D/g, "");
const round2 = (value: number) => Math.round(value * 100) / 100;

/** Tách "1C26TAA-00001234", "1C26TAA 00001234" hoặc "1C26TAA/00001234" thành ký hiệu và số. */
function splitSeriesAndNumber(raw: string): { series: string; number: string } | null {
  const match = raw.trim().match(/^([A-Za-z0-9]+)\s*[-/\s]\s*(\d+)$/);
  return match ? { series: normalizeSeries(match[1]), number: match[2] } : null;
}

export function SupplierInvoiceForm({ initial, receiptItems, purchasePrices, busy, retrying, onSubmit, onClose }: Props) {
  const purchasePrice = (item: GoodsReceiptItemResponse) => purchasePrices.get(item.id) ?? item.unitPrice;
  const [series, setSeries] = useState(initial.invoiceSeries);
  const [number, setNumber] = useState(initial.invoiceNumber);
  const [date, setDate] = useState(initial.invoiceDate);
  const [vat, setVat] = useState<number | null>(initial.vatAmount);
  const [shipping, setShipping] = useState<number | null>(initial.shippingFee);
  const [lines, setLines] = useState<Line[]>(() => receiptItems.map((item) => {
    const billed = initial.items.find((line) => line.goodsReceiptItemId === item.id);
    return {
      item,
      included: billed != null,
      quantity: billed?.quantity ?? item.quantity,
      price: billed?.unitPrice ?? purchasePrice(item),
    };
  }));
  const [error, setError] = useState("");
  const readOnly = retrying || busy;

  const change = (id: string, patch: Partial<Omit<Line, "item">>) =>
    setLines((current) => current.map((line) => line.item.id === id ? { ...line, ...patch } : line));

  const included = lines.filter((line) => line.included);
  const subtotal = round2(included.reduce((sum, l) => sum + (l.quantity ?? 0) * (l.price ?? 0), 0));
  const expected = round2(lines.reduce((sum, l) => sum + l.item.quantity * purchasePrice(l.item), 0));
  const total = round2(subtotal + (vat ?? 0) + (shipping ?? 0));
  const variance = round2(subtotal - expected);

  const fillFromPurchase = () =>
    setLines((current) => current.map((line) => ({
      ...line, included: true, quantity: line.item.quantity, price: purchasePrice(line.item),
    })));

  // Chỉ tách khi dán cả chuỗi; gõ tay từng ký tự thì giữ nguyên trong ô Ký hiệu.
  const onSeriesPaste = (event: ClipboardEvent<HTMLInputElement>) => {
    const split = splitSeriesAndNumber(event.clipboardData.getData("text"));
    if (!split) return;
    event.preventDefault();
    setSeries(split.series);
    setNumber(split.number);
  };

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!series || !number || !included.length) {
      setError("Nhập ký hiệu, số hóa đơn và chọn ít nhất một dòng hàng.");
      return;
    }
    if (included.some((l) => l.quantity == null || l.price == null) || vat == null || shipping == null) {
      setError("Nhập đủ số lượng, đơn giá, thuế GTGT và phí vận chuyển (ghi 0 nếu không có).");
      return;
    }
    setError("");
    onSubmit({
      invoiceSeries: series,
      invoiceNumber: number,
      invoiceDate: date,
      vatAmount: vat,
      shippingFee: shipping,
      items: included.map((l) => ({ goodsReceiptItemId: l.item.id, quantity: l.quantity!, unitPrice: l.price! })),
    });
  }

  return (
    <form className="supplier-invoice__form" onSubmit={submit}>
      {retrying && <p role="status" className="supplier-invoice__notice">Chưa rõ kết quả lưu. Thử lại với đúng nội dung và mã yêu cầu đã gửi.</p>}
      <div className="supplier-invoice__fields">
        <label>Ký hiệu
          <Input required maxLength={50} placeholder="VD: 1C26TAA" value={series} readOnly={readOnly}
            onPaste={onSeriesPaste} onChange={(e) => setSeries(normalizeSeries(e.target.value))} />
        </label>
        <label>Số hóa đơn
          <Input required maxLength={50} inputMode="numeric" placeholder="VD: 00001234" value={number} readOnly={readOnly}
            onChange={(e) => setNumber(normalizeNumber(e.target.value))} />
        </label>
        <label>Ngày hóa đơn<Input type="date" required value={date} readOnly={readOnly} onChange={(e) => setDate(e.target.value)} /></label>
      </div>
      <small className="supplier-invoice__hint">
        Có thể dán cả chuỗi ký hiệu và số (vd. <code>1C26TAA-00001234</code>) vào ô Ký hiệu, hệ thống tự tách. Ký hiệu tự chuyển chữ in hoa; số hóa đơn chỉ nhận chữ số.
      </small>

      <div className="supplier-invoice__bar">
        <strong>Dòng hàng trên hóa đơn</strong>
        <Button type="button" variant="secondary" disabled={readOnly} onClick={fillFromPurchase}>Điền theo phiếu mua và số thực nhận</Button>
      </div>
      <div className="table-wrap supplier-invoice__table">
        <table>
          <thead><tr><th>SKU / Đơn vị</th><th>SL nhận</th><th>Giá phiếu mua</th><th>Có trên HĐ</th><th>SL hóa đơn</th><th>Đơn giá trước thuế</th><th>Thành tiền HĐ</th></tr></thead>
          <tbody>
            {lines.map(({ item, included: on, quantity, price }) => {
              const differs = on && (quantity !== item.quantity || price !== purchasePrice(item));
              return (
                <tr key={item.id} className={!on || differs ? "supplier-invoice__variance" : undefined}>
                  <td><strong>{item.skuCode}</strong><small>{item.productName} · {item.unitName || "Đơn vị gốc"}</small></td>
                  <td>{item.quantity.toLocaleString("vi-VN", { maximumFractionDigits: 3 })}</td>
                  <td>{formatInvoiceAmount(purchasePrice(item))}</td>
                  <td><input type="checkbox" aria-label={`Có trên hóa đơn ${item.skuCode}`} checked={on} disabled={readOnly} onChange={(e) => change(item.id, { included: e.target.checked })} /></td>
                  <td><NumberField aria-label={`SL hóa đơn ${item.skuCode}`} decimals={3} value={quantity} disabled={!on || readOnly} onChange={(v) => change(item.id, { quantity: v })} /></td>
                  <td><NumberField aria-label={`Đơn giá trước thuế ${item.skuCode}`} decimals={2} suffix="₫" value={price} disabled={!on || readOnly} onChange={(v) => change(item.id, { price: v })} /></td>
                  <td>{on ? formatInvoiceAmount(round2((quantity ?? 0) * (price ?? 0))) : "—"}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <div className="supplier-invoice__fields">
        <label>Thuế GTGT
          <NumberField aria-label="Thuế GTGT" decimals={2} suffix="₫" value={vat} disabled={readOnly} onChange={setVat} />
          <span className="supplier-invoice__actions">
            {VAT_RATES.map((rate) => (
              <Button key={rate} type="button" variant="secondary" disabled={readOnly}
                onClick={() => setVat(Math.round(subtotal * rate / 100))}>{rate}%</Button>
            ))}
          </span>
        </label>
        <label>Phí vận chuyển
          <NumberField aria-label="Phí vận chuyển" decimals={2} suffix="₫" value={shipping} disabled={readOnly} onChange={setShipping} />
        </label>
      </div>

      <div className="supplier-invoice__totals" aria-live="polite">
        <span>Tiền hàng trước thuế: <strong>{formatInvoiceAmount(subtotal)}</strong></span>
        <span>Tổng thanh toán: <strong>{formatInvoiceAmount(total)}</strong></span>
        <span className={variance ? "supplier-invoice__notice" : undefined}>
          So với phiếu mua × thực nhận: <strong>{variance === 0 ? "khớp" : `${variance > 0 ? "+" : ""}${formatInvoiceAmount(variance)}`}</strong>
        </span>
      </div>
      <small className="supplier-invoice__hint">Thuế GTGT và phí vận chuyển chỉ lưu trên hóa đơn, không tính vào chênh lệch và giá vốn.</small>

      {error && <p role="alert" className="supplier-invoice__error">{error}</p>}
      <div className="supplier-invoice__actions">
        <Button type="submit" disabled={busy}>{busy ? "Đang lưu..." : retrying ? "Thử lại lưu nháp hóa đơn" : "Lưu nháp hóa đơn"}</Button>
        <Button type="button" variant="secondary" disabled={busy} onClick={onClose}>Đóng form hóa đơn</Button>
      </div>
    </form>
  );
}
