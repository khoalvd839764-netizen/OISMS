import { AlertCircle, BrainCircuit, CalendarDays, Download, PackageX, RefreshCw, Sparkles, TrendingUp } from "lucide-react";
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Badge, Button, PageTitle, StatCard } from "../components/ui";
import { apiAdapter } from "../services/apiAdapter";
import { useAuth } from "../context/AuthContext";
import type { Order, Sku } from "../types/domain";

export function ReportsPage() {
  const { account } = useAuth();
  const [skus, setSkus] = useState<Sku[]>([]);
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const loadReports = async () => {
    setLoading(true);
    setLoadError(null);
    const [sRes, oRes] = await Promise.all([
      apiAdapter.getSkus(),
      apiAdapter.getOrders(account.tenantId),
    ]);
    if (sRes.error) {
      setLoadError(sRes.error.message);
    }
    if (sRes.data) setSkus(sRes.data);
    if (oRes.data) setOrders(oRes.data);
    setLoading(false);
  };

  useEffect(() => {
    loadReports();
  }, [account.tenantId]);

  // Tổng hợp hiệu quả P&L theo từng SKU thực tế
  const skuPerformance = skus.map((sku) => {
    const activeOrders = orders.filter((o) => o.status !== "Cancelled");
    let soldFromOrders = 0;
    let cogsFromOrders = 0;

    activeOrders.forEach((o) => {
      const matched = o.items?.filter((i) => i.skuCode === sku.code) || [];
      matched.forEach((i) => {
        soldFromOrders += i.quantity;
        cogsFromOrders += (i.costSnapshot || sku.averageCost) * i.quantity;
      });
    });

    const soldQty = soldFromOrders > 0 ? soldFromOrders : sku.reserved;
    const revenue = soldQty * sku.sellingPrice;
    const cogs = soldFromOrders > 0 ? cogsFromOrders : soldQty * sku.averageCost;
    const grossProfit = revenue - cogs;
    return {
      id: sku.id,
      name: `${sku.name}${sku.variant ? ` (${sku.variant})` : ""}`,
      code: sku.code,
      isActive: sku.isActive !== false,
      soldQty,
      revenue,
      cogs,
      grossProfit,
      available: sku.onHand - sku.reserved,
      threshold: sku.threshold,
    };
  });

  const totalRevenue = skuPerformance.reduce((sum, r) => sum + r.revenue, 0);
  const totalCogs = skuPerformance.reduce((sum, r) => sum + r.cogs, 0);
  const totalGrossProfit = totalRevenue - totalCogs;

  // Chọn SKU đang hoạt động có tồn khả dụng thấp nhất để gợi ý nhập hàng
  const prioritySku = [...skuPerformance]
    .filter((r) => r.isActive)
    .sort((a, b) => a.available - b.available)[0];

  return (
    <>
      <PageTitle
        eyebrow="FR-REP · AI"
        title="Báo cáo & dự báo"
        description="Số liệu doanh thu, giá vốn (WAC) và khuyến nghị nhập hàng trực tiếp từ dữ liệu thực tế cho Owner."
        actions={
          <>
            <Button variant="secondary" onClick={loadReports} disabled={loading}>
              <CalendarDays size={17} />Kỳ hiện tại
            </Button>
            <Button variant="secondary" onClick={() => window.print()}>
              <Download size={17} />Xuất báo cáo <Badge tone="neutral">[Sprint 3 Preview]</Badge>
            </Button>
          </>
        }
      />

      {/* [A09] Báo lỗi kết nối API thay vì hiển thị dữ liệu = 0 */}
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
              <strong>Không thể tải dữ liệu báo cáo từ máy chủ:</strong>
              <div style={{ fontSize: "13px" }}>{loadError}</div>
            </div>
          </div>
          <Button variant="secondary" onClick={loadReports}>
            <RefreshCw size={15} /> Tải lại trang
          </Button>
        </div>
      )}

      <div className="stats-grid stats-grid--three">
        <StatCard
          label="Doanh thu thuần"
          value={loadError ? "—" : `${totalRevenue.toLocaleString("vi-VN")} ₫`}
          change="Đã loại trừ đơn hủy"
          icon={<TrendingUp size={19} />}
        />
        <StatCard
          label="Tổng giá vốn (COGS)"
          value={loadError ? "—" : `${totalCogs.toLocaleString("vi-VN")} ₫`}
          change="Tính theo giá vốn bình quân WAC từng chi nhánh"
          icon={<PackageX size={19} />}
        />
        <StatCard
          label="Lợi nhuận gộp"
          value={loadError ? "—" : `${totalGrossProfit.toLocaleString("vi-VN")} ₫`}
          change={`${totalRevenue.toLocaleString("vi-VN")} ₫ − ${totalCogs.toLocaleString("vi-VN")} ₫`}
          icon={<Sparkles size={19} />}
        />
      </div>

      <div className="report-layout">
        <section className="card">
          <div className="card-head">
            <div>
              <h2>Hiệu quả kinh doanh theo SKU</h2>
              <p>Đối chiếu Giá bán lẻ và Giá vốn bình quân gia quyền (WAC)</p>
            </div>
            <Badge tone="info">Dữ liệu thực tế ({skuPerformance.length} SKU)</Badge>
          </div>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Sản phẩm / SKU</th>
                  <th>SL Bán / Giữ chỗ</th>
                  <th>Doanh thu</th>
                  <th>Giá vốn (WAC)</th>
                  <th>Lợi nhuận gộp</th>
                </tr>
              </thead>
              <tbody>
                {loading ? (
                  <tr>
                    <td colSpan={5} style={{ textAlign: "center", padding: "28px 0", color: "#64748b" }}>
                      Đang tổng hợp báo cáo P&L từ máy chủ...
                    </td>
                  </tr>
                ) : skuPerformance.length === 0 ? (
                  <tr>
                    <td colSpan={5} style={{ textAlign: "center", padding: "28px 0", color: "#64748b" }}>
                      Chưa có SKU nào để thống kê.
                    </td>
                  </tr>
                ) : (
                  skuPerformance.map((row) => (
                    <tr key={row.id}>
                      <td>
                        <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
                          <strong>{row.name}</strong>
                          {!row.isActive && <Badge tone="neutral">[Ngừng hoạt động]</Badge>}
                        </div>
                        <span className="table-sub">{row.code}</span>
                      </td>
                      <td>{row.soldQty}</td>
                      <td>{row.revenue.toLocaleString("vi-VN")} ₫</td>
                      <td>{row.cogs.toLocaleString("vi-VN")} ₫</td>
                      <td>
                        <strong style={{ color: row.grossProfit >= 0 ? "#16a34a" : "#dc2626" }}>
                          {row.grossProfit.toLocaleString("vi-VN")} ₫
                        </strong>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </section>

        {/* [A11] Gắn nhãn [Sprint 3 Preview] cho thẻ AI Dự báo nhập hàng */}
        <aside className="card ai-card">
          <div className="ai-head">
            <span><BrainCircuit size={23} /></span>
            <div>
              <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
                <h2>Gợi ý nhập hàng</h2>
                <Badge tone="neutral">[Sprint 3 Preview]</Badge>
              </div>
              <p>Phân tích từ số dư khả dụng thực tế</p>
            </div>
          </div>

          {prioritySku ? (
            <div className="recommendation">
              <div>
                <strong>{prioritySku.code}</strong>
                <Badge tone={prioritySku.available <= prioritySku.threshold ? "warning" : "info"}>
                  {prioritySku.available <= prioritySku.threshold ? "Ưu tiên cao" : "Theo dõi định kỳ"}
                </Badge>
              </div>
              <h3>
                Đề xuất nhập {Math.max(10, prioritySku.threshold * 3 - Math.max(0, prioritySku.available))} sản phẩm
              </h3>
              <p>
                Mặt hàng <strong>{prioritySku.name}</strong> hiện có tồn khả dụng là <strong>{prioritySku.available}</strong> (ngưỡng an toàn: {prioritySku.threshold}).
              </p>
              <dl>
                <div><dt>Khoảng dự báo</dt><dd>14 ngày</dd></div>
                <div><dt>Tồn khả dụng</dt><dd>{prioritySku.available}</dd></div>
                <div><dt>Ngưỡng an toàn</dt><dd>{prioritySku.threshold}</dd></div>
              </dl>
              <Link className="button button--primary" to="/inventory" style={{ textAlign: "center", justifyContent: "center" }}>
                Mở trang Nhập kho
              </Link>
            </div>
          ) : (
            <div className="recommendation">
              <p>Hãy khởi tạo SKU và nhập kho để hệ thống phân tích đề xuất tồn kho an toàn.</p>
            </div>
          )}

          <div className="ai-disclaimer">
            Khuyến nghị được tính toán dựa trên công thức <code>Available = OnHand - Reserved</code> so với ngưỡng an toàn của từng SKU; hệ thống không tự động đặt hàng khi chưa có xác nhận của Owner/Staff.
          </div>
        </aside>
      </div>
    </>
  );
}
