import { AlertTriangle, CheckCircle2, Code2, ExternalLink } from "lucide-react";
import { Badge, PageTitle } from "../components/ui";

const screens = [
  ["/login", "Đăng nhập, đăng xuất, làm mới phiên", "Công khai", "FR-AUTH", "LoginForm, authAdapter", "POST /api/auth/*"],
  ["/organization", "Quản lý nhân viên, vai trò, chi nhánh/kho", "Owner", "FR-AUTH", "StaffTable, BranchGrid", "/api/users, /api/branches"],
  ["/products", "Tra cứu hoặc tạo sản phẩm/SKU/barcode", "Owner, Staff đọc", "FR-PROD", "ProductTable, ProductForm", "/api/products, /api/skus"],
  ["/inventory", "Xem onHand/reserved/available, cấu hình ngưỡng", "Owner, Staff", "FR-INV", "BalanceTable, ThresholdDialog", "/api/inventory, /api/alerts/low-stock"],
  ["/inventory", "Tạo nháp và xác nhận phiếu nhập, tính giá vốn", "Owner, Staff", "FR-COST", "ReceiptDialog", "POST /api/stock-receipts"],
  ["/inventory", "Lọc ledger, mở chứng từ, tạo phiếu trả nháp", "Owner, Staff", "FR-INV", "LedgerTable, ReturnDialog", "/api/inventory/ledger"],
  ["/orders", "Giữ chỗ, xác nhận, hoàn tất, nhận sự kiện hết hạn", "Owner, Staff", "FR-ORD, FR-RSE", "OrderDetail, realtimeAdapter", "PATCH /api/orders"],
  ["/reports", "Doanh thu, giá vốn, lợi nhuận và gợi ý nhập", "Owner", "FR-REP, AI", "SalesReport, ReorderCard", "GET /api/reports/*"],
  ["/403", "Màn hình từ chối truy cập và bảo vệ phân quyền RBAC", "Tất cả vai trò", "FR-AUTH", "AccessPage", "Bắt mã lỗi 403 Forbidden"],
];

export function HandoffPage() {
  return (
    <>
      <PageTitle eyebrow="TÀI LIỆU BÀN GIAO" title="Handoff kỹ thuật & nghiệp vụ" description="Bản đồ màn hình, ranh giới API và các giả định cần nhóm sản phẩm chốt." />
      <section className="card"><div className="card-head"><div><h2>Ma trận màn hình và hành động</h2><p>Route tương thích React Router; DTO dùng camelCase.</p></div><Badge tone="success"><CheckCircle2 size={14} />Prototype tương tác</Badge></div><div className="table-wrap"><table><thead><tr><th>Route</th><th>Hành động</th><th>Vai trò</th><th>Mã FR</th><th>Component</th><th>API dự kiến</th></tr></thead><tbody>{screens.map((row, index) => <tr key={`${row[0]}-${index}`}><td><code>{row[0]}</code></td><td>{row[1]}</td><td>{row[2]}</td><td><Badge tone="info">{row[3]}</Badge></td><td>{row[4]}</td><td><span className="api-route">{row[5]}</span></td></tr>)}</tbody></table></div></section>
      <div className="handoff-grid"><section className="card"><div className="section-icon"><Code2 size={21} /></div><h2>Ranh giới tích hợp</h2><ul className="check-list"><li>Tenant, role, branchIds và warehouseIds lấy từ Session/backend.</li><li>API, auth, realtime và simulator được cô lập trong adapter để thay bằng ASP.NET Core/SignalR.</li><li>Access token và refresh token do authAdapter xử lý, không hiển thị trên UI.</li><li>POS được bàn giao trong ứng dụng riêng; Admin không cung cấp màn hình checkout tại quầy.</li><li>Sổ kho bất biến; giá vốn đơn là snapshot tại lúc xác nhận.</li><li>PWA, in hóa đơn và checkout toàn giỏ thuộc phạm vi ứng dụng POS riêng.</li></ul></section><section className="card assumptions"><div className="section-icon warning"><AlertTriangle size={21} /></div><h2>Giả định cần duyệt</h2><ul><li>Quyền mặc định của Staff và phạm vi kho được phân công.</li><li>Phiếu nhập/chuyển/kiểm kê có trạng thái nháp trước khi ghi sổ.</li><li>Đường hủy sau trạng thái Đã xác nhận và quy tắc hoàn tiền.</li><li>Quan hệ một chi nhánh có thể có một hoặc nhiều kho.</li><li>Quy tắc tiền, giá vốn và điều kiện trả hàng.</li><li>Phương pháp, tiêu chí và ngưỡng gợi ý nhập hàng AI.</li></ul></section></div>
      <div className="nfr-disclaimer"><AlertTriangle size={20} /><div><strong>Không phải chứng nhận NFR</strong><p>Prototype không chứng minh latency, uptime, throughput, test coverage, bảo mật database, tính đúng của khóa đồng thời hoặc khả năng phục hồi. Các yêu cầu này phải được hiện thực, quan sát và kiểm thử trên hệ thống backend thật.</p></div></div>
      <div className="architecture-note"><strong>Kiến trúc backend dự kiến</strong><span>Domain</span><ExternalLink size={14} /><span>Application</span><ExternalLink size={14} /><span>Infrastructure</span><ExternalLink size={14} /><span>WebApi</span><small>PostgreSQL / EF Core · SignalR · Hangfire</small></div>
    </>
  );
}
