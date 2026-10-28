import { useEffect, useState } from "react";
import { apiAdapter } from "../services/apiAdapter";
import { Badge, Button } from "./ui";

interface ReceiptSummary {
  id: string;
  number: string;
  status: string;
  warehouseName: string;
  supplierName: string;
  totalAmount: number;
}

export function GoodsReceiptList({ tenantId, onOpen }: { tenantId?: string; onOpen: (id: string) => void }) {
  const [page, setPage] = useState(1);
  const [refresh, setRefresh] = useState(0);
  const [result, setResult] = useState<{ items: ReceiptSummary[]; pages: number; error?: string } | null>(null);

  useEffect(() => {
    let active = true;
    setResult(null);
    apiAdapter.getGoodsReceipts(page).then((response) => {
      if (!active) return;
      if (response.error) setResult({ items: [], pages: 1, error: response.error.message });
      else setResult({ items: response.data || [], pages: response.totalPages || 1 });
    });
    return () => { active = false; };
  }, [page, refresh, tenantId]);

  return (
    <div>
      <p style={{ marginBottom: 16 }}>Phiếu nháp chưa cộng tồn. Mở phiếu để xem lại và xác nhận nhập kho.</p>
      <Button variant="secondary" onClick={() => setRefresh((value) => value + 1)}>Tải lại danh sách</Button>
      {!result ? <p role="status">Đang tải phiếu nhập...</p> : result.error ? (
        <p role="alert">{result.error}</p>
      ) : result.items.length === 0 ? <p>Chưa có phiếu nhập.</p> : (
        <div className="table-wrap" style={{ marginTop: 16 }}>
          <table>
            <thead><tr><th>Mã phiếu</th><th>Kho / Nhà cung cấp</th><th>Giá trị</th><th>Trạng thái</th><th>Thao tác</th></tr></thead>
            <tbody>{result.items.map((receipt) => (
              <tr key={receipt.id}>
                <td>{receipt.number}</td>
                <td>{receipt.warehouseName}<br />{receipt.supplierName}</td>
                <td>{receipt.totalAmount.toLocaleString("vi-VN")} ₫</td>
                <td><Badge tone={receipt.status === "Confirmed" ? "success" : "warning"}>{receipt.status === "Draft" ? "Chờ xác nhận" : receipt.status === "Confirmed" ? "Đã nhập kho" : receipt.status}</Badge></td>
                <td><Button variant="secondary" onClick={() => onOpen(receipt.id)}>Xem phiếu</Button></td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
      {result && !result.error && <div className="modal-actions">
        <Button variant="secondary" disabled={page === 1} onClick={() => setPage((value) => value - 1)}>Trang trước</Button>
        <span>Trang {page} / {result.pages}</span>
        <Button variant="secondary" disabled={page >= result.pages} onClick={() => setPage((value) => value + 1)}>Trang sau</Button>
      </div>}
    </div>
  );
}
