import { AlertCircle, ArrowRight, Boxes, CircleAlert, Clock3, PackageCheck, RefreshCw, ShoppingCart, TrendingUp, WalletCards } from "lucide-react";
import { Link } from "react-router-dom";
import { useEffect, useRef, useState } from "react";
import { Badge, Button, PageTitle, StatCard } from "../components/ui";
import { apiAdapter } from "../services/apiAdapter";
import { useAuth } from "../context/AuthContext";
import type { DashboardSummary } from "../types/reports";

export function DashboardPage() {
  const { account } = useAuth();
  const isOwner = account?.role === "Owner";
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const reqSeqRef = useRef(0);

  const fetchDashboard = async () => {
    const seq = ++reqSeqRef.current;
    setLoading(true);
    setLoadError(null);

    const res = await apiAdapter.getDashboardSummary();
    if (seq !== reqSeqRef.current) return;

    if (res.error) {
      setLoadError(res.error.message || "Lỗi kết nối máy chủ khi tải dữ liệu tổng quan.");
      setSummary(null);
    } else if (res.data) {
      setSummary(res.data);
    }
    setLoading(false);
  };

  useEffect(() => {
    fetchDashboard();
  }, [account?.tenantId, account?.role]);

  // Kiểm tra dữ liệu biểu đồ thật
  const trendData = summary?.recentRevenueTrend || [];
  const hasRealTrendData = trendData.length > 0 && trendData.some((d) => Number(d.revenue) > 0);
  const maxRevenue = hasRealTrendData
    ? Math.max(...trendData.map((d) => Number(d.revenue) || 0), 1)
    : 1;

  return (
    <>
      <PageTitle
        eyebrow="Tổng quan vận hành OISM"
        title={`Chào mừng trở lại, ${account?.name || "Quản trị viên"}`}
        description={`Theo dõi hiệu quả kinh doanh và tồn kho của ${account?.tenantName || "Doanh nghiệp"}.`}
        actions={
          <>
            <Button variant="secondary" onClick={fetchDashboard} disabled={loading}>
              <RefreshCw size={17} />
              {loading ? "Đang tải..." : "Làm mới"}
            </Button>
            <Link className="button button--primary" to="/orders">
              Tạo đơn Admin
            </Link>
          </>
        }
      />

      {/* Hiển thị rõ lỗi kết nối API riêng biệt với trạng thái không có dữ liệu */}
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
            <RefreshCw size={15} /> Thử lại
          </Button>
        </div>
      )}

      {/* KPI Cards */}
      <div className="stats-grid">
        <StatCard
          label="Doanh thu thuần"
          value={
            loading
              ? "..."
              : loadError
              ? "—"
              : `${(summary?.totalRevenue ?? 0).toLocaleString("vi-VN")} ₫`
          }
          change={
            loadError
              ? "Lỗi tải số liệu"
              : summary
              ? `Từ ${summary.totalOrders} đơn hàng hoàn tất`
              : "Đang tải..."
          }
          icon={<WalletCards size={19} />}
        />
        <StatCard
          label="Tổng đơn hàng"
          value={
            loading
              ? "..."
              : loadError
              ? "—"
              : `${summary?.totalOrders ?? 0} đơn`
          }
          change="Bao gồm đơn từ POS và Admin"
          icon={<ShoppingCart size={19} />}
        />
        <StatCard
          label="Danh mục SKU"
          value={
            loading
              ? "..."
              : loadError
              ? "—"
              : `${summary?.skuCount ?? 0} SKU`
          }
          change={
            isOwner && summary?.totalInventoryValue !== null && summary?.totalInventoryValue !== undefined
              ? `Giá trị tồn: ${summary.totalInventoryValue.toLocaleString("vi-VN")} ₫`
              : "SKU đang hoạt động"
          }
          icon={<PackageCheck size={19} />}
        />
        <StatCard
          label="Lợi nhuận gộp"
          value={
            loading
              ? "..."
              : loadError
              ? "—"
              : isOwner
              ? summary?.totalGrossProfit !== null && summary?.totalGrossProfit !== undefined
                ? `${summary.totalGrossProfit.toLocaleString("vi-VN")} ₫`
                : "0 ₫"
              : "Bảo mật vai trò"
          }
          change={
            !isOwner
              ? "Chỉ Chủ doanh nghiệp (Owner) mới có quyền xem lợi nhuận"
              : summary && summary.totalGrossProfit !== null
              ? `Tỷ suất biên: ${Number(summary.grossMarginPercentage || 0).toFixed(1)}%`
              : "Chưa phát sinh lợi nhuận"
          }
          icon={<TrendingUp size={19} />}
        />
      </div>

      <div className="dashboard-grid">
        {/* Biểu đồ xu hướng doanh thu 7 ngày */}
        <section className="card revenue-card">
          <div className="card-head">
            <div>
              <h2>Xu hướng doanh thu 7 ngày gần nhất</h2>
              <p>Dữ liệu doanh thu thực tế được ghi nhận theo từng ngày</p>
            </div>
            {summary && (
              <Badge tone="success">{summary.skuCount} SKU đang quản lý</Badge>
            )}
          </div>

          {loading ? (
            <div style={{ padding: "48px 20px", textAlign: "center", color: "#64748b" }}>
              Đang tải biểu đồ xu hướng...
            </div>
          ) : !hasRealTrendData ? (
            <div
              style={{
                padding: "48px 20px",
                textAlign: "center",
                color: "#64748b",
                display: "flex",
                flexDirection: "column",
                alignItems: "center",
                gap: "8px",
              }}
            >
              <CircleAlert size={28} color="#94a3b8" />
              <strong>Chưa có dữ liệu doanh thu trong 7 ngày gần nhất để vẽ biểu đồ.</strong>
              <p style={{ margin: 0, fontSize: "11px", maxWidth: "420px" }}>
                Biểu đồ chỉ sử dụng dữ liệu thực tế từ các đơn hàng đã phát sinh. Dữ liệu sẽ tự động hiển thị tại đây khi có giao dịch.
              </p>
            </div>
          ) : (
            <div className="chart">
              <div className="chart-y">
                <span>{maxRevenue.toLocaleString("vi-VN")} ₫</span>
                <span>{(maxRevenue / 2).toLocaleString("vi-VN")} ₫</span>
                <span>0 ₫</span>
              </div>
              <div className="bars">
                {trendData.map((d) => {
                  const rev = Number(d.revenue) || 0;
                  const heightPercent = maxRevenue > 0 ? Math.max((rev / maxRevenue) * 100, 4) : 0;
                  const dateLabel = d.date.length >= 10 ? d.date.slice(5) : d.date;
                  return (
                    <div className="bar-wrap" key={d.date} title={`${d.date}: ${rev.toLocaleString("vi-VN")} ₫ (${d.orderCount} đơn)`}>
                      <div
                        className="bar"
                        style={{ height: `${heightPercent}%` }}
                      />
                      <span>{dateLabel}</span>
                    </div>
                  );
                })}
              </div>
            </div>
          )}
        </section>

        {/* Cần xử lý */}
        <section className="card alert-card">
          <div className="card-head">
            <div>
              <h2>Cần xử lý</h2>
              <p>Ưu tiên theo mức độ ảnh hưởng</p>
            </div>
            <span className="alert-count">{summary?.lowStockCount ?? 0}</span>
          </div>
          <div className="attention-list">
            <Link to="/inventory">
              <span className="attention-icon danger"><CircleAlert size={19} /></span>
              <div>
                <strong>{summary?.lowStockCount ?? 0} SKU sắp hết hàng</strong>
                <p>Khả dụng (Available) ≤ Ngưỡng an toàn</p>
              </div>
              <ArrowRight size={18} />
            </Link>
            <Link to="/orders">
              <span className="attention-icon warning"><Clock3 size={19} /></span>
              <div>
                <strong>Tra cứu và xử lý đơn hàng</strong>
                <p>Xem danh sách đơn & trạng thái giữ chỗ</p>
              </div>
              <ArrowRight size={18} />
            </Link>
            <Link to="/inventory">
              <span className="attention-icon info"><Boxes size={19} /></span>
              <div>
                <strong>
                  {isOwner && summary?.totalInventoryValue !== null && summary?.totalInventoryValue !== undefined
                    ? `Giá trị tồn: ${summary.totalInventoryValue.toLocaleString("vi-VN")} ₫`
                    : "Kiểm tra tồn kho & Sổ kho"}
                </strong>
                <p>Tra cứu số dư tồn & lịch sử giao dịch kho</p>
              </div>
              <ArrowRight size={18} />
            </Link>
          </div>
        </section>
      </div>

      {/* Đơn hàng gần đây */}
      <section className="card">
        <div className="card-head">
          <div>
            <h2>Đơn hàng gần đây</h2>
            <p>Cập nhật theo phiên Tenant hiện tại ({account?.tenantName || "Doanh nghiệp"})</p>
          </div>
          <Link className="text-link" to="/orders">
            Xem tất cả <ArrowRight size={16} />
          </Link>
        </div>

        {loading ? (
          <div style={{ padding: "32px", textAlign: "center", color: "#64748b" }}>
            Đang tải danh sách đơn hàng gần đây...
          </div>
        ) : !summary?.recentOrders || summary.recentOrders.length === 0 ? (
          <div style={{ padding: "36px 20px", textAlign: "center", color: "#64748b" }}>
            Chưa có đơn hàng nào được ghi nhận gần đây.
          </div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Mã đơn</th>
                  <th>Thời gian</th>
                  <th>Kênh bán</th>
                  <th>Khách hàng</th>
                  <th>Trạng thái</th>
                  <th>Tổng tiền</th>
                </tr>
              </thead>
              <tbody>
                {summary.recentOrders.map((ord) => {
                  const channelClass =
                    ord.channel === "POS"
                      ? "channel--pos"
                      : ord.channel === "Shopee"
                      ? "channel--shopee"
                      : ord.channel === "TikTok"
                      ? "channel--tiktok"
                      : ord.channel === "Lazada"
                      ? "channel--lazada"
                      : "channel--admin";

                  return (
                    <tr key={ord.id}>
                      <td>
                        <strong>{ord.number}</strong>
                      </td>
                      <td>
                        {ord.createdAt
                          ? new Date(ord.createdAt).toLocaleString("vi-VN")
                          : "—"}
                      </td>
                      <td>
                        <span className={`channel ${channelClass}`}>
                          {ord.channel}
                        </span>
                      </td>
                      <td>{ord.customerName || "Khách lẻ"}</td>
                      <td>
                        <OrderBadge status={ord.status} />
                      </td>
                      <td>
                        <strong>
                          {Number(ord.totalAmount || 0).toLocaleString("vi-VN")} ₫
                        </strong>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
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
    Processing: { label: "Đang xử lý", tone: "info" },
    Completed: { label: "Hoàn tất", tone: "success" },
    Cancelled: { label: "Đã hủy", tone: "danger" },
  };
  const item = map[status] ?? map.Draft;
  return <Badge tone={item.tone}>{item.label}</Badge>;
}
