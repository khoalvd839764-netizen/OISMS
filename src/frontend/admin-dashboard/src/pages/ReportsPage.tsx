import { BrainCircuit, PackageX, RefreshCw, Sparkles, TrendingUp } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Badge, Button, PageTitle, StatCard } from '../components/common/ui';
import { inventoryApi } from '../api/inventory.api';
import type { InventoryBalanceDto } from '../types/inventory.types';

export function ReportsPage() {
  const [balances, setBalances] = useState<InventoryBalanceDto[]>([]);
  const [loading, setLoading] = useState(true);

  const loadReportData = useCallback(async () => {
    setLoading(true);
    try {
      const balData = await inventoryApi.getBalances();
      setBalances(balData);
    } catch {
      setBalances([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadReportData();
  }, [loadReportData]);

  const totalInventoryValue = balances.reduce(
    (sum, b) => sum + b.onHand * b.averageCost,
    0
  );
  const totalOnHandUnits = balances.reduce((sum, b) => sum + b.onHand, 0);
  const lowStockItems = balances.filter((b) => b.onHand - b.reserved <= b.reorderPoint);

  return (
    <>
      <PageTitle
        eyebrow="FR-REP · CHỈ DÀNH CHO OWNER"
        title="Báo cáo Giá trị tồn kho, Giá vốn WAC & Gợi ý nhập hàng"
        description="Phân tích giá trị vốn tồn kho theo chuẩn bình quân gia quyền (WAC) và danh sách SKU cần bổ sung."
        actions={
          <Button variant="secondary" onClick={() => void loadReportData()}>
            <RefreshCw size={16} /> Cập nhật báo cáo
          </Button>
        }
      />

      <div className="stats-grid stats-grid--three">
        <StatCard
          label="Tổng giá trị tồn kho (WAC)"
          value={loading ? '...' : `${totalInventoryValue.toLocaleString('vi-VN')} ₫`}
          change="Tính theo OnHand × AverageCost từng kho"
          icon={<TrendingUp size={19} />}
        />
        <StatCard
          label="Tổng đơn vị tồn thực tế"
          value={loading ? '...' : `${totalOnHandUnits.toLocaleString('vi-VN')} ĐV`}
          change={`${balances.length} cặp SKU/Kho đang theo dõi`}
          icon={<PackageX size={19} />}
        />
        <StatCard
          label="SKU cần nhập bổ sung"
          value={loading ? '...' : `${lowStockItems.length} SKU`}
          change="Khả dụng ≤ Ngưỡng cảnh báo (Reorder Point)"
          icon={<Sparkles size={19} />}
        />
      </div>

      <div className="report-layout">
        <section className="card">
          <div className="card-head">
            <div>
              <h2>Phân tích giá vốn & giá trị tồn theo SKU</h2>
              <p>Dữ liệu tổng hợp trực tiếp từ Tồn kho & Giá vốn WAC</p>
            </div>
            <Badge tone="info">Chỉ đọc · Owner Only</Badge>
          </div>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>SKU / Sản phẩm</th>
                  <th>Kho</th>
                  <th>Tồn thực tế</th>
                  <th>Giá vốn BQ (WAC)</th>
                  <th>Tổng giá trị vốn</th>
                </tr>
              </thead>
              <tbody>
                {balances.length === 0 ? (
                  <tr>
                    <td colSpan={5} className="muted" style={{ textAlign: 'center', padding: '24px' }}>
                      Chưa có dữ liệu tồn kho ghi sổ. Hãy lập và xác nhận Phiếu nhập kho để xem báo cáo giá vốn.
                    </td>
                  </tr>
                ) : (
                  balances.map((b) => (
                    <tr key={`${b.warehouseId}-${b.skuId}`}>
                      <td>
                        <strong className="mono">{b.skuCode}</strong>
                        <span className="table-sub">
                          {b.productName} · {b.variantName}
                        </span>
                      </td>
                      <td>{b.warehouseName}</td>
                      <td>{b.onHand}</td>
                      <td>{b.averageCost.toLocaleString('vi-VN')} ₫</td>
                      <td>
                        <strong>{(b.onHand * b.averageCost).toLocaleString('vi-VN')} ₫</strong>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </section>

        <aside className="card ai-card">
          <div className="ai-head">
            <span>
              <BrainCircuit size={23} />
            </span>
            <div>
              <h2>Khuyến nghị nhập hàng</h2>
              <p>Dựa trên ngưỡng tồn an toàn (Reorder Point)</p>
            </div>
          </div>

          {lowStockItems.length === 0 ? (
            <div className="recommendation">
              <strong>Tồn kho ổn định</strong>
              <p>Hiện không có SKU nào giảm xuống dưới ngưỡng cảnh báo tồn tối thiểu.</p>
              <Link className="button button--secondary" to="/inventory">
                Mở trang Kho & Sổ kho
              </Link>
            </div>
          ) : (
            lowStockItems.slice(0, 2).map((item) => {
              const available = item.onHand - item.reserved;
              const recommendedQty = Math.max(10, item.reorderPoint * 2 - available);
              return (
                <div className="recommendation" key={`${item.warehouseId}-${item.skuId}`}>
                  <div>
                    <strong className="mono">{item.skuCode}</strong>
                    <Badge tone="warning">Ưu tiên bổ sung</Badge>
                  </div>
                  <h3>Đề xuất nhập {recommendedQty} đơn vị</h3>
                  <p>
                    Tại {item.warehouseName}: Tồn khả dụng còn {available} (Ngưỡng tối thiểu:{' '}
                    {item.reorderPoint}).
                  </p>
                  <Link className="button button--primary" to="/inventory">
                    Lập phiếu nhập kho
                  </Link>
                </div>
              );
            })
          )}
        </aside>
      </div>
    </>
  );
}
