import { AlertCircle, ArrowRight, Boxes, CircleAlert, Clock3, PackageCheck, RefreshCw, ShoppingCart, TrendingUp, WalletCards } from "lucide-react";
import { Link } from "react-router-dom";
import { useEffect, useState } from "react";
import { Badge, Button, ComingSoonBadge, ComingSoonPanel, PageTitle, StatCard } from "../components/ui";
import { apiAdapter } from "../services/apiAdapter";
import { useAuth } from "../context/AuthContext";
import type { Sku } from "../types/domain";

export function DashboardPage() {
  const { account } = useAuth();
  const [skusList, setSkusList] = useState<Sku[]>([]);
  const [branchBalances, setBranchBalances] = useState<Sku[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const fetchDashboard = async () => {
    setLoading(true);
    setLoadError(null);
    const [skuRes, balRes] = await Promise.all([
      apiAdapter.getSkus(),
      apiAdapter.getBalances(),
    ]);

    if (skuRes.error || balRes.error) {
      setLoadError(skuRes.error?.message || balRes.error?.message || "Lỗi kết nối máy chủ khi tải dữ liệu tổng quan.");
    }
    if (skuRes.data) setSkusList(skuRes.data);
    if (balRes.data) setBranchBalances(balRes.data);
    setLoading(false);
  };

  useEffect(() => {
    fetchDashboard();
  }, [account.tenantId]);

  const lowStockSkus = skusList.filter((s) => s.isActive !== false && s.onHand - s.reserved <= s.threshold);

  // [A02] Tính tổng giá trị tồn kho chính xác theo từng Chi nhánh: TotalValue = sum(OnHand_branch * AverageCost_branch)
  const totalInventoryValue =
    branchBalances.length > 0
      ? branchBalances.reduce((sum, b) => sum + (Number(b.onHand) || 0) * (Number(b.averageCost) || 0), 0)
      : skusList.reduce((sum, s) => sum + (s.totalInventoryValue ?? s.onHand * s.averageCost), 0);

  return (
    <>
      <PageTitle
        eyebrow="Tổng quan vận hành OISM"
        title={`Chào mừng trở lại, ${account.name || "Quản trị viên"}`}
        description={`Theo dõi tồn kho của ${account.tenantName || "Doanh nghiệp"}. Các chỉ số bán hàng tổng hợp đang được hoàn thiện.`}
        actions={
          <>
            <Button variant="secondary" onClick={fetchDashboard} disabled={loading}>
              <RefreshCw size={17} />{loading ? "Đang tải..." : "Làm mới"}
            </Button>
            <Link className="button button--primary" to="/orders">Tạo đơn Admin</Link>
          </>
        }
      />

      {/* [A09] Hiển thị rõ lỗi kết nối API thay vì biến thành mảng rỗng = 0 */}
      {loadError && (
        <div
          style={{
            background: "#fef2f2",
            border: "1px solid #ef4444",
            color: "#b91c1c",
            padding: "14px 18px",
            borderRadius: "10px",
            marginBottom: "18px",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            gap: "12px",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
            <AlertCircle size={20} />
            <div>
              <strong>Không thể tải dữ liệu từ máy chủ Backend:</strong>
              <div style={{ fontSize: "13px" }}>{loadError}</div>
            </div>
          </div>
          <Button variant="secondary" onClick={fetchDashboard}>
            <RefreshCw size={15} /> Tải lại trang
          </Button>
        </div>
      )}

      <div className="stats-grid">
        <StatCard
          label="Doanh thu ghi nhận"
          value="Coming soon"
          change={loadError ? "Lỗi kết nối API" : `Giá trị tồn kho: ${totalInventoryValue.toLocaleString("vi-VN")} ₫`}
          icon={<WalletCards size={19} />}
        />
        <StatCard
          label="Đơn hàng"
          value="Coming soon"
          change="Đang hoàn thiện tổng hợp đơn hàng"
          icon={<ShoppingCart size={19} />}
        />
        <StatCard
          label="Danh mục SKU"
          value={loadError ? "—" : `${skusList.length} SKU`}
          change="Dữ liệu từ danh mục sản phẩm"
          icon={<PackageCheck size={19} />}
        />
        <StatCard
          label="Lợi nhuận gộp"
          value="Coming soon"
          change="Xem số liệu tại trang Báo cáo"
          icon={<TrendingUp size={19} />}
        />
      </div>
      <div className="dashboard-grid">
        <section className="card revenue-card">
          <div className="card-head">
            <div>
              <h2>Biểu đồ vận hành & lưu chuyển hàng hóa</h2>
              <p>Biểu đồ tổng hợp dữ liệu vận hành đang được hoàn thiện.</p>
            </div>
            <div style={{ display: "flex", gap: "8px", alignItems: "center" }}>
              <ComingSoonBadge />
              <Badge tone="success">{skusList.length} SKU đang quản lý</Badge>
            </div>
          </div>
          <ComingSoonPanel title="Biểu đồ vận hành" description="Chưa có biểu đồ từ dữ liệu thực tế. Số liệu mẫu đã được ẩn." />
        </section>
        <section className="card alert-card">
          <div className="card-head">
            <div>
              <h2>Cần xử lý</h2>
              <p>Ưu tiên theo mức độ ảnh hưởng</p>
            </div>
            <span className="alert-count">{lowStockSkus.length}</span>
          </div>
          <div className="attention-list">
            <Link to="/inventory">
              <span className="attention-icon danger"><CircleAlert size={19} /></span>
              <div>
                <strong>{lowStockSkus.length} SKU sắp hết hàng</strong>
                <p>Khả dụng (Available) ≤ Ngưỡng an toàn</p>
              </div>
              <ArrowRight size={18} />
            </Link>
            <Link to="/orders">
              <span className="attention-icon warning"><Clock3 size={19} /></span>
              <div>
                <strong>Tra cứu và xử lý đơn hàng</strong>
                <p>Mở danh sách đơn để xem trạng thái giữ chỗ</p>
              </div>
              <ArrowRight size={18} />
            </Link>
            <Link to="/inventory">
              <span className="attention-icon info"><Boxes size={19} /></span>
              <div>
                <strong>Tổng giá trị tồn kho (WAC theo chi nhánh)</strong>
                <p>{totalInventoryValue.toLocaleString("vi-VN")} ₫</p>
              </div>
              <ArrowRight size={18} />
            </Link>
          </div>
        </section>
      </div>
      <section className="card">
        <div className="card-head">
          <div>
            <h2>Đơn hàng gần đây</h2>
            <p>Cập nhật theo phiên Tenant hiện tại ({account.tenantName})</p>
          </div>
          <Link className="text-link" to="/orders">Xem tất cả <ArrowRight size={16} /></Link>
        </div>
        <ComingSoonPanel title="Đơn hàng gần đây" description="Danh sách đơn trên Tổng quan đang được hoàn thiện. Chọn Xem tất cả để tra cứu đơn hàng tại trang Đơn đa kênh." />
      </section>
    </>
  );
}

export function OrderBadge({ status }: { status: string }) {
  const map: Record<string, { label: string; tone: "success" | "warning" | "danger" | "info" | "neutral" }> = {
    Draft: { label: "Nháp", tone: "neutral" },
    Pending: { label: "Đang giữ chỗ", tone: "warning" },
    Reserved: { label: "Đang giữ chỗ", tone: "warning" },
    Confirmed: { label: "Đã xác nhận", tone: "info" },
    Completed: { label: "Hoàn tất", tone: "success" },
    Cancelled: { label: "Đã hủy", tone: "danger" },
  };
  const item = map[status] ?? map.Draft;
  return <Badge tone={item.tone}>{item.label}</Badge>;
}
