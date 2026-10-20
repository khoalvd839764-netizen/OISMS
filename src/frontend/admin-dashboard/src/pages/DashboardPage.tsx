import { AlertCircle, ArrowRight, Boxes, CircleAlert, Clock3, PackageCheck, RefreshCw, ShoppingCart, TrendingUp, WalletCards } from "lucide-react";
import { Link } from "react-router-dom";
import { useEffect, useState } from "react";
import { Badge, Button, PageTitle, StatCard } from "../components/ui";
import { apiAdapter } from "../services/apiAdapter";
import { useAuth } from "../context/AuthContext";
import type { Order, Sku } from "../types/domain";

const chart = [42, 58, 48, 72, 64, 84, 76, 93, 70, 88, 96, 82];

export function DashboardPage() {
  const { account } = useAuth();
  const [ordersList, setOrdersList] = useState<Order[]>([]);
  const [skusList, setSkusList] = useState<Sku[]>([]);
  const [branchBalances, setBranchBalances] = useState<Sku[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const fetchDashboard = async () => {
    setLoading(true);
    setLoadError(null);
    const [ordRes, skuRes, balRes] = await Promise.all([
      apiAdapter.getOrders(account.tenantId),
      apiAdapter.getSkus(),
      apiAdapter.getBalances(),
    ]);

    if (skuRes.error || balRes.error) {
      setLoadError(skuRes.error?.message || balRes.error?.message || "Lỗi kết nối máy chủ khi tải dữ liệu tổng quan.");
    }
    if (ordRes.data) setOrdersList(ordRes.data);
    if (skuRes.data) setSkusList(skuRes.data);
    if (balRes.data) setBranchBalances(balRes.data);
    setLoading(false);
  };

  useEffect(() => {
    fetchDashboard();
  }, [account.tenantId]);

  const activeOrders = ordersList.filter((o) => o.status !== "Cancelled");
  const totalRevenue = activeOrders.reduce((sum, o) => sum + (Number(o.amount) || 0), 0);
  const totalCogs = activeOrders.reduce((sum, o) => sum + (Number(o.cogs) || 0), 0);
  const grossProfit = totalRevenue - totalCogs;
  const marginPct = totalRevenue > 0 ? ((grossProfit / totalRevenue) * 100).toFixed(1) : "0.0";
  const totalUnitsSold = activeOrders.reduce(
    (sum, o) => sum + (o.items?.reduce((s, i) => s + i.quantity, 0) || 1),
    0
  );
  const lowStockSkus = skusList.filter((s) => s.isActive !== false && s.onHand - s.reserved <= s.threshold);
  const reservedOrders = ordersList.filter((o) => o.status === "Pending" || o.status === "Reserved");

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
        description={`Tình hình bán hàng và tồn kho thực tế của ${account.tenantName || "Doanh nghiệp"} hôm nay.`}
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
          value={loadError ? "—" : `${totalRevenue.toLocaleString("vi-VN")} ₫`}
          change={loadError ? "Lỗi kết nối API" : `Giá trị tồn kho: ${totalInventoryValue.toLocaleString("vi-VN")} ₫`}
          icon={<WalletCards size={19} />}
        />
        <StatCard
          label="Đơn hàng"
          value={loadError ? "—" : String(ordersList.length)}
          change={`${reservedOrders.length} đơn đang giữ chỗ`}
          icon={<ShoppingCart size={19} />}
        />
        <StatCard
          label="Danh mục SKU"
          value={loadError ? "—" : `${skusList.length} SKU`}
          change={`Đã xuất/giữ chỗ: ${totalUnitsSold} SP`}
          icon={<PackageCheck size={19} />}
        />
        <StatCard
          label="Lợi nhuận gộp"
          value={loadError ? "—" : `${grossProfit.toLocaleString("vi-VN")} ₫`}
          change={`Biên lợi nhuận ${marginPct}%`}
          icon={<TrendingUp size={19} />}
        />
      </div>
      <div className="dashboard-grid">
        <section className="card revenue-card">
          <div className="card-head">
            <div>
              <h2>Biểu đồ vận hành & lưu chuyển hàng hóa</h2>
              <p>Đã trừ đơn hủy · Đồng bộ trực tiếp từ tồn kho & đơn hàng</p>
            </div>
            <div style={{ display: "flex", gap: "8px", alignItems: "center" }}>
              <Badge tone="neutral">[Sprint 3 Preview]</Badge>
              <Badge tone="success">{skusList.length} SKU đang quản lý</Badge>
            </div>
          </div>
          <div className="chart">
            <div className="chart-y"><span>20</span><span>15</span><span>10</span><span>5</span><span>0</span></div>
            <div className="bars">
              {chart.map((height, index) => (
                <div className="bar-wrap" key={index}>
                  <div className={`bar ${index === 11 ? "bar--active" : ""}`} style={{ height: `${height}%` }} />
                  {index % 2 === 0 && <span>{index + 11}/06</span>}
                </div>
              ))}
            </div>
          </div>
        </section>
        <section className="card alert-card">
          <div className="card-head">
            <div>
              <h2>Cần xử lý</h2>
              <p>Ưu tiên theo mức độ ảnh hưởng</p>
            </div>
            <span className="alert-count">{lowStockSkus.length + reservedOrders.length}</span>
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
                <strong>{reservedOrders.length} đơn đang giữ chỗ tồn kho</strong>
                <p>Thời hạn giữ chỗ 15 phút (Advisory Lock)</p>
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
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Mã đơn</th>
                <th>Kênh</th>
                <th>Khách hàng</th>
                <th>Thời gian</th>
                <th>Tổng tiền</th>
                <th>Trạng thái</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr>
                  <td colSpan={6} style={{ textAlign: "center", padding: "28px 0", color: "#64748b" }}>
                    Đang tải dữ liệu đơn hàng từ máy chủ...
                  </td>
                </tr>
              ) : ordersList.length === 0 ? (
                <tr>
                  <td colSpan={6} style={{ textAlign: "center", padding: "28px 0", color: "#64748b" }}>
                    Chưa có đơn hàng nào phát sinh hôm nay. Bạn có thể tạo đơn tại trang Đơn hàng.
                  </td>
                </tr>
              ) : (
                ordersList.slice(0, 5).map((order) => (
                  <tr key={order.id}>
                    <td><strong>{order.code}</strong></td>
                    <td><span className={`channel channel--${order.channel.toLowerCase()}`}>{order.channel}</span></td>
                    <td>{order.customer}</td>
                    <td>{new Date(order.createdAt).toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit", timeZone: "Asia/Ho_Chi_Minh" })}</td>
                    <td><strong>{order.amount.toLocaleString("vi-VN")} ₫</strong></td>
                    <td><OrderBadge status={order.status} /></td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
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
