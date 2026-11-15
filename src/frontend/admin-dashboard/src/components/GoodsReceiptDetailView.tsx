import type { GoodsReceiptResponse } from "../types/goodsReceipt";
import { Badge, Button } from "./ui";
import "./supplierInvoices/supplierInvoices.css";

interface Props {
  receipt: GoodsReceiptResponse;
  onViewLedger?: () => void;
}

const money = (value: number) => `${value.toLocaleString("vi-VN", { maximumFractionDigits: 6 })} ₫`;
const qty = (value: number) => value.toLocaleString("vi-VN", { maximumFractionDigits: 3 });
const dateTime = (value?: string | null) => value ? new Date(value).toLocaleString("vi-VN") : "—";

/** Xem lại phiếu nhập đã lưu (đã nhập kho hoặc đã hủy): thông tin chung và các dòng hàng, chỉ đọc. */
export function GoodsReceiptDetailView({ receipt, onViewLedger }: Props) {
  const confirmed = receipt.status === "Confirmed";
  return (
    <div className="receipt-detail">
      <div className="receipt-detail__info">
        <div><span>Trạng thái</span>{confirmed ? <Badge tone="success">Đã nhập kho</Badge> : <Badge tone="danger">Đã hủy</Badge>}</div>
        <div><span>Đơn mua</span><strong>{receipt.purchaseOrderNumber ?? "—"}</strong></div>
        <div><span>Nhà cung cấp</span><strong>{receipt.supplierName}</strong></div>
        <div><span>Chi nhánh / Kho nhận</span><strong>{receipt.branchName} · {receipt.warehouseName}</strong></div>
        <div><span>Người lập</span><strong>{receipt.createdByName ?? "—"}</strong><small>{dateTime(receipt.createdAt)}</small></div>
        {confirmed && (
          <div><span>Người xác nhận</span><strong>{receipt.confirmedByName ?? "—"}</strong><small>{dateTime(receipt.confirmedAt)}</small></div>
        )}
        {receipt.note && <div className="receipt-detail__note"><span>Ghi chú</span>{receipt.note}</div>}
      </div>

      <div className="table-wrap supplier-invoice__table">
        <table>
          <thead>
            <tr><th>SKU / Sản phẩm</th><th>Đơn vị</th><th>SL nhập</th><th>Quy đổi (gốc)</th><th>Đơn giá nhập</th><th>Thành tiền</th></tr>
          </thead>
          <tbody>
            {receipt.items.map((item) => (
              <tr key={item.id}>
                <td><strong>{item.skuCode}</strong><small>{item.productName}</small></td>
                <td>{item.unitName || "Đơn vị gốc"}{item.factorSnapshot !== 1 && <small>× {qty(item.factorSnapshot)}</small>}</td>
                <td>{qty(item.quantity)}</td>
                <td>{qty(item.baseQuantity)}</td>
                <td>{money(item.unitPrice)}</td>
                <td><strong>{money(item.lineTotal)}</strong></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="supplier-invoice__bar">
        <span>{receipt.items.length} dòng hàng · Tổng giá trị phiếu nhập: <strong>{money(receipt.totalAmount)}</strong></span>
        {confirmed && onViewLedger && <Button variant="secondary" onClick={onViewLedger}>Xem sổ kho</Button>}
      </div>
    </div>
  );
}
