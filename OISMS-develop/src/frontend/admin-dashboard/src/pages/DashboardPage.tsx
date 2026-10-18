import {
  ArrowRight,
  Boxes,
  Building2,
  CircleAlert,
  FolderTree,
  Layers,
  PackageCheck,
  Plus,
  RefreshCw,
  Warehouse,
} from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Badge, Button, PageTitle, StatCard } from '../components/common/ui';
import { useAuth } from '../context/AuthContext';
import { catalogApi } from '../api/catalog.api';
import { organizationApi } from '../api/organization.api';
import { inventoryApi } from '../api/inventory.api';
import type { ProductResponse } from '../types/catalog.types';

export function DashboardPage() {
  const { user, role } = useAuth();
  const [todayLabel] = useState(() =>
    new Date().toLocaleDateString('vi-VN', {
      weekday: 'long',
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
    })
  );
  const [products, setProducts] = useState<ProductResponse[]>([]);
  const [categoryCount, setCategoryCount] = useState(0);
  const [brandCount, setBrandCount] = useState(0);
  const [branchCount, setBranchCount] = useState(0);
  const [warehouseCount, setWarehouseCount] = useState(0);
  const [supplierCount, setSupplierCount] = useState(0);
  const [lowStockCount, setLowStockCount] = useState(0);
  const [loading, setLoading] = useState(true);

  const loadDashboardSummary = useCallback(async () => {
    setLoading(true);
    try {
      const [prodRes, catRes, brandRes, branchRes, whRes, supRes, balRes] =
        await Promise.allSettled([
          catalogApi.getProducts(),
          catalogApi.getCategories(),
          catalogApi.getBrands(),
          organizationApi.getBranches(),
          organizationApi.getWarehouses(),
          organizationApi.getSuppliers(),
          inventoryApi.getBalances({ lowStockOnly: true }),
        ]);

      if (prodRes.status === 'fulfilled') setProducts(prodRes.value);
      if (catRes.status === 'fulfilled') setCategoryCount(catRes.value.length);
      if (brandRes.status === 'fulfilled') setBrandCount(brandRes.value.length);
      if (branchRes.status === 'fulfilled') setBranchCount(branchRes.value.length);
      if (whRes.status === 'fulfilled') setWarehouseCount(whRes.value.length);
      if (supRes.status === 'fulfilled') setSupplierCount(supRes.value.length);
      if (balRes.status === 'fulfilled') setLowStockCount(balRes.value.length);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadDashboardSummary();
  }, [loadDashboardSummary]);

  const totalVariants = products.reduce((sum, p) => sum + (p.variants?.length || 0), 0);

  return (
    <>
      <PageTitle
        eyebrow={`${todayLabel} · VAI TRÒ: ${role || 'STAFF'}`}
        title={`Xin chào, ${user?.fullName || 'Quản trị viên'}`}
        description={`Tổng quan dữ liệu thời gian thực của doanh nghiệp ${user?.tenantName || 'OISM'} (${user?.tenantCode || 'TENANT'}).`}
        actions={
          <>
            <Button variant="secondary" onClick={() => void loadDashboardSummary()}>
              <RefreshCw size={16} /> Làm mới số liệu
            </Button>
            <Link className="button button--primary" to="/inventory">
              <Plus size={16} /> Tạo phiếu nhập kho
            </Link>
          </>
        }
      />

      <div className="stats-grid">
        <StatCard
          label="Sản phẩm & Biến thể SKU"
          value={loading ? '...' : `${products.length} SP / ${totalVariants} SKU`}
          change={`${categoryCount} danh mục gốc · ${brandCount} thương hiệu`}
          icon={<Layers size={19} />}
        />
        <StatCard
          label="Chi nhánh & Kho vật lý"
          value={loading ? '...' : `${branchCount} CN / ${warehouseCount} Kho`}
          change="Phân tách tồn kho độc lập theo từng kho"
          icon={<Warehouse size={19} />}
        />
        <StatCard
          label="Đối tác Nhà cung cấp"
          value={loading ? '...' : `${supplierCount} NCC`}
          change="Sẵn sàng lập phiếu nhập kho nhiều dòng"
          icon={<Building2 size={19} />}
        />
        <StatCard
          label="Cảnh báo tồn kho thấp"
          value={loading ? '...' : `${lowStockCount} SKU`}
          change="Điều kiện: Available ≤ ReorderPoint"
          icon={<PackageCheck size={19} />}
        />
      </div>

      <div className="dashboard-grid">
        <section className="card">
          <div className="card-head">
            <div>
              <h2>Sản phẩm & SKU mới cập nhật</h2>
              <p>Dữ liệu trực tiếp từ Catalog API (`/api/products`)</p>
            </div>
            <Link className="text-link" to="/products">
              Quản lý Catalog <ArrowRight size={16} />
            </Link>
          </div>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Tên sản phẩm</th>
                  <th>Danh mục</th>
                  <th>Thương hiệu</th>
                  <th>Số biến thể SKU</th>
                  <th>Trạng thái</th>
                </tr>
              </thead>
              <tbody>
                {products.length === 0 ? (
                  <tr>
                    <td colSpan={5} className="muted" style={{ textAlign: 'center', padding: '24px' }}>
                      Chưa có sản phẩm nào. Nhấn &ldquo;Quản lý Catalog&rdquo; để tạo sản phẩm đầu tiên.
                    </td>
                  </tr>
                ) : (
                  products.slice(0, 6).map((p) => (
                    <tr key={p.id}>
                      <td>
                        <strong>{p.name}</strong>
                      </td>
                      <td>{p.categoryName || 'Chưa phân loại'}</td>
                      <td>{p.brandName || '—'}</td>
                      <td>
                        <span className="mono">{p.variants?.length || 0} SKU</span>
                      </td>
                      <td>
                        <Badge tone={p.isActive ? 'success' : 'neutral'}>
                          {p.isActive ? 'Đang bán' : 'Ngừng bán'}
                        </Badge>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </section>

        <section className="card alert-card">
          <div className="card-head">
            <div>
              <h2>Tác vụ vận hành nhanh</h2>
              <p>Điểm truy cập nhanh theo phân quyền</p>
            </div>
          </div>
          <div className="attention-list">
            <Link to="/products">
              <span className="attention-icon info">
                <FolderTree size={19} />
              </span>
              <div>
                <strong>Cấu hình Danh mục, Thương hiệu & SKU</strong>
                <p>Tạo cây danh mục Cha-Con và sinh mã vạch EAN-13</p>
              </div>
              <ArrowRight size={18} />
            </Link>

            <Link to="/inventory">
              <span className="attention-icon warning">
                <Boxes size={19} />
              </span>
              <div>
                <strong>Phiếu nhập kho nhiều dòng & Sổ kho</strong>
                <p>Nhập hàng, tính giá vốn WAC và tra cứu sổ cái bất biến</p>
              </div>
              <ArrowRight size={18} />
            </Link>

            <Link to="/inventory">
              <span className="attention-icon danger">
                <CircleAlert size={19} />
              </span>
              <div>
                <strong>{lowStockCount} SKU cần kiểm tra ngưỡng tồn</strong>
                <p>Theo dõi tồn khả dụng (Available = OnHand - Reserved)</p>
              </div>
              <ArrowRight size={18} />
            </Link>
          </div>
        </section>
      </div>
    </>
  );
}

export function OrderBadge({ status }: { status: string }) {
  const map: Record<
    string,
    { label: string; tone: 'success' | 'warning' | 'danger' | 'info' | 'neutral' }
  > = {
    Draft: { label: 'Nháp', tone: 'neutral' },
    Reserved: { label: 'Đang giữ chỗ', tone: 'warning' },
    Confirmed: { label: 'Đã xác nhận', tone: 'info' },
    Completed: { label: 'Hoàn tất', tone: 'success' },
    Cancelled: { label: 'Đã hủy', tone: 'danger' },
  };
  const item = map[status] ?? map.Draft;
  return <Badge tone={item.tone}>{item.label}</Badge>;
}
