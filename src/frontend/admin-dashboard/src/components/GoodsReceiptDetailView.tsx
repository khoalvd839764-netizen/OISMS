import { useAuth } from "../context/AuthContext";
import type { GoodsReceiptResponse } from "../types/goodsReceipt";
import { Badge, Button } from "./ui";

interface Props {
  receipt: GoodsReceiptResponse;
  onViewLedger?: () => void;
}

const money = (value?: number | null) =>
  value != null ? `${value.toLocaleString("vi-VN", { maximumFractionDigits: 6 })} ₫` : "—";
const qty = (value: number) => value.toLocaleString("vi-VN", { maximumFractionDigits: 3 });
const dateTime = (value?: string | null) => (value ? new Date(value).toLocaleString("vi-VN") : "—");

/** Xem lại phiếu nhập đã lưu (đã nhập kho hoặc đã hủy): thông tin chung và các dòng hàng, chỉ đọc. */
export function GoodsReceiptDetailView({ receipt, onViewLedger }: Props) {
  const { account } = useAuth();
  const isOwner = account?.role === "Owner";
  const confirmed = receipt.status === "Confirmed";

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(3, 1fr)",
          gap: 12,
          padding: 12,
          background: "var(--surface-subtle, #f8fafc)",
          borderRadius: 8,
          fontSize: 13,
        }}
      >
        <div>
          <span style={{ color: "var(--text-muted, #64748b)", display: "block" }}>Trạng thái</span>
          {confirmed ? <Badge tone="success">Đã nhập kho</Badge> : <Badge tone="danger">Đã hủy</Badge>}
        </div>
        <div>
          <span style={{ color: "var(--text-muted, #64748b)", display: "block" }}>Đơn mua</span>
          <strong>{receipt.purchaseOrderNumber ?? "—"}</strong>
        </div>
        <div>
          <span style={{ color: "var(--text-muted, #64748b)", display: "block" }}>Nhà cung cấp</span>
          <strong>{receipt.supplierName}</strong>
        </div>
        <div>
          <span style={{ color: "var(--text-muted, #64748b)", display: "block" }}>Chi nhánh / Kho nhận</span>
          <strong>
            {receipt.branchName} · {receipt.warehouseName}
          </strong>
        </div>
        <div>
          <span style={{ color: "var(--text-muted, #64748b)", display: "block" }}>Người lập</span>
          <strong>{receipt.createdByName ?? "—"}</strong>
          <div style={{ fontSize: 11, color: "var(--text-muted, #64748b)" }}>{dateTime(receipt.createdAt)}</div>
        </div>
        {confirmed && (
          <div>
            <span style={{ color: "var(--text-muted, #64748b)", display: "block" }}>Người xác nhận</span>
            <strong>{receipt.confirmedByName ?? "—"}</strong>
            <div style={{ fontSize: 11, color: "var(--text-muted, #64748b)" }}>{dateTime(receipt.confirmedAt)}</div>
          </div>
        )}
        {receipt.note && (
          <div style={{ gridColumn: "span 3", borderTop: "1px solid #e2e8f0", paddingTop: 6 }}>
            <span style={{ color: "var(--text-muted, #64748b)" }}>Ghi chú: </span>
            {receipt.note}
          </div>
        )}
      </div>

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>SKU / Sản phẩm</th>
              <th>Đơn vị</th>
              <th>SL nhập</th>
              <th>Quy đổi (gốc)</th>
              {isOwner && <th>Đơn giá nhập (sau phân bổ)</th>}
              {isOwner && <th>Thành tiền</th>}
            </tr>
          </thead>
          <tbody>
            {receipt.items.map((item) => (
              <tr key={item.id}>
                <td>
                  <strong>{item.skuCode}</strong>
                  <div style={{ fontSize: 12, color: "var(--text-muted, #64748b)" }}>{item.productName}</div>
                </td>
                <td>
                  {item.unitName || "Đơn vị gốc"}
                  {item.factorSnapshot !== 1 && (
                    <div style={{ fontSize: 11, color: "var(--text-muted, #64748b)" }}>
                      × {qty(item.factorSnapshot)}
                    </div>
                  )}
                </td>
                <td>{qty(item.quantity)}</td>
                <td>{qty(item.baseQuantity)}</td>
                {isOwner && <td>{money(item.unitPrice)}</td>}
                {isOwner && (
                  <td>
                    <strong>{money(item.lineTotal)}</strong>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          padding: "10px 14px",
          background: "var(--surface-subtle, #f8fafc)",
          borderRadius: 6,
          fontSize: 13,
        }}
      >
        <span>
          {receipt.items.length} dòng hàng
          {isOwner && (
            <>
              {" "}
              · Tổng giá trị phiếu nhập: <strong>{money(receipt.totalAmount)}</strong>
            </>
          )}
        </span>
        {confirmed && onViewLedger && (
          <Button variant="secondary" onClick={onViewLedger}>
            Xem sổ kho
          </Button>
        )}
      </div>
    </div>
  );
}
