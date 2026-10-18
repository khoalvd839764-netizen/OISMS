import { AlertTriangle, CheckCircle2, Code2, ExternalLink } from 'lucide-react';
import { Badge, PageTitle } from '../components/common/ui';

const screens = [
  ['/login', 'Đăng nhập JWT, đăng ký Tenant mới, tự động làm mới phiên', 'Công khai', 'FR-AUTH', 'AuthPage, AuthContext', 'POST /api/auth/login, /register-tenant, /refresh'],
  ['/organization', 'Quản lý Chi nhánh, Kho vật lý, Nhà cung cấp & Phân quyền', 'Owner', 'FR-ORG', 'OrganizationPage, RoleGuard', 'GET/POST /api/branches, /api/warehouses, /api/suppliers'],
  ['/products', 'Quản lý Cây Danh mục Cha-Con, Thương hiệu, Sản phẩm & Biến thể SKU (EAN-13)', 'Owner, Staff', 'FR-PROD', 'ProductsPage', 'CRUD /api/categories, /api/brands, /api/products'],
  ['/inventory', 'Xem OnHand/Reserved/Available, cảnh báo tồn, cấu hình Reorder Point', 'Owner, Staff', 'FR-INV', 'InventoryPage (Balances)', 'GET /api/inventory/balances'],
  ['/inventory', 'Phiếu nhập kho nhiều dòng (Multi-line Goods Receipt) & tính giá vốn WAC', 'Owner, Staff', 'FR-COST', 'InventoryPage (Receipts)', 'POST /api/inventory/receipts, /{id}/confirm'],
  ['/inventory', 'Sổ kho bất biến chỉ đọc (Append-Only Ledger) & truy vết chứng từ gốc', 'Owner, Staff', 'FR-INV', 'InventoryPage (Ledger)', 'GET /api/inventory/ledger'],
  ['/orders', 'Theo dõi đơn hàng đa kênh (Draft → Reserved → Confirmed → Completed)', 'Owner, Staff', 'FR-ORD', 'OrdersPage', 'GET /api/orders'],
  ['/reports', 'Báo cáo tổng giá trị vốn tồn kho WAC & gợi ý nhập hàng', 'Owner', 'FR-REP', 'ReportsPage, RoleGuard', 'GET /api/inventory/balances'],
];

export function HandoffPage() {
  return (
    <>
      <PageTitle
        eyebrow="TÀI LIỆU BÀN GIAO KỸ THUẬT (WEB ADMIN REACT 19)"
        title="Bản đồ tích hợp API & Kiến trúc Phân quyền"
        description="Tổng hợp các tuyến màn hình, hợp đồng DTO và cơ chế bảo mật đã kết nối trực tiếp vào Backend OISM.WebApi."
      />
      <section className="card">
        <div className="card-head">
          <div>
            <h2>Ma trận màn hình và API Endpoint thực tế</h2>
            <p>Đã gỡ bỏ hoàn toàn dữ liệu giả lập (Zero-Mock) và kết nối trực tiếp Axios Interceptor.</p>
          </div>
          <Badge tone="success">
            <CheckCircle2 size={14} /> Production Ready (React 19)
          </Badge>
        </div>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Route</th>
                <th>Chức năng nghiệp vụ</th>
                <th>Phân quyền RBAC</th>
                <th>Mã FR</th>
                <th>Module Frontend</th>
                <th>Backend API Endpoint</th>
              </tr>
            </thead>
            <tbody>
              {screens.map((row, index) => (
                <tr key={`${row[0]}-${index}`}>
                  <td>
                    <code>{row[0]}</code>
                  </td>
                  <td>{row[1]}</td>
                  <td>{row[2]}</td>
                  <td>
                    <Badge tone="info">{row[3]}</Badge>
                  </td>
                  <td>{row[4]}</td>
                  <td>
                    <span className="api-route">{row[5]}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <div className="handoff-grid">
        <section className="card">
          <div className="section-icon">
            <Code2 size={21} />
          </div>
          <h2>Chuẩn kiến trúc đã hoàn thiện</h2>
          <ul className="check-list">
            <li>Không còn dữ liệu cứng hoặc phiên Owner mặc định; mọi phiên làm việc bắt buộc xác thực JWT.</li>
            <li>Axios Interceptor tự động đính kèm Bearer Token, xếp hàng (Queue) làm mới token khi gặp 401 và phát sự kiện 403/Network Error.</li>
            <li>Form Danh mục phân cấp Cha-Con có kiểm soát chống vòng lặp (Circular Dependency).</li>
            <li>Form Sản phẩm hỗ trợ tạo nhiều biến thể SKU cùng lúc và tự động sinh mã vạch chuẩn EAN-13 (Checksum Modulo 10).</li>
            <li>Form Phiếu nhập kho nhiều dòng tự động tính thành tiền từng dòng và dự phóng Giá vốn bình quân gia quyền (WAC).</li>
            <li>Sổ kho bất biến (Append-Only Ledger) chỉ đọc, tuyệt đối không cho phép sửa/xóa bút toán lịch sử.</li>
          </ul>
        </section>

        <section className="card assumptions">
          <div className="section-icon warning">
            <AlertTriangle size={21} />
          </div>
          <h2>Cơ chế xử lý lỗi & Trạng thái giao diện</h2>
          <ul>
            <li>Hiển thị Skeleton Loading đồng bộ trong lúc chờ phản hồi từ máy chủ.</li>
            <li>Hiển thị NetworkBanner cảnh báo đỏ trên đỉnh màn hình khi mất kết nối mạng hoặc Backend không phản hồi.</li>
            <li>Tự động điều hướng sang trang `/403` và hiển thị Toast chi tiết khi gặp lỗi `403 Forbidden`.</li>
            <li>Trích xuất chi tiết mảng lỗi `errors: string[]` từ FluentValidation của Backend C# để hiển thị rõ ràng trên từng form.</li>
          </ul>
        </section>
      </div>

      <div className="architecture-note">
        <strong>Kiến trúc kết nối</strong>
        <span>React 19 + TypeScript</span>
        <ExternalLink size={14} />
        <span>Axios JWT Interceptor</span>
        <ExternalLink size={14} />
        <span>OISM.WebApi (.NET 9)</span>
        <small>PostgreSQL · EF Core · Multi-Tenant Isolation</small>
      </div>
    </>
  );
}
