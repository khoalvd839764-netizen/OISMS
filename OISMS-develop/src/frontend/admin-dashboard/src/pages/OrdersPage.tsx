import { AlertCircle, RefreshCw, Search, ShoppingBag } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import { Button, EmptyState, Input, PageTitle, Select } from '../components/common/ui';
import { TableSkeleton } from '../components/feedback/Skeleton';
import { apiClient, parseApiError } from '../api/client';
import { OrderBadge } from './DashboardPage';

interface OrderSummaryDto {
  id: string;
  code: string;
  channel: string;
  customerName: string;
  branchName: string;
  status: 'Draft' | 'Reserved' | 'Confirmed' | 'Completed' | 'Cancelled';
  totalAmount: number;
  createdAt: string;
}

export function OrdersPage() {
  const [orders, setOrders] = useState<OrderSummaryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [channelFilter, setChannelFilter] = useState('');

  const loadOrders = useCallback(async () => {
    setLoading(true);
    setErrorMsg(null);
    try {
      const response = await apiClient.get<OrderSummaryDto[]>('/orders', {
        params: {
          search: searchQuery.trim() || undefined,
          channel: channelFilter || undefined,
        },
      });
      setOrders(response.data);
    } catch (err) {
      const parsed = parseApiError(err);
      setErrorMsg(parsed.message);
      setOrders([]);
    } finally {
      setLoading(false);
    }
  }, [searchQuery, channelFilter]);

  useEffect(() => {
    void loadOrders();
  }, [loadOrders]);

  return (
    <>
      <PageTitle
        eyebrow="FR-ORD · FR-RSE · OMNICHANNEL ORDERS"
        title="Đơn hàng đa kênh"
        description="Quản lý vòng đời đơn hàng (Draft → Reserved → Confirmed → Completed / Cancelled) từ POS, Admin và sàn TMĐT."
        actions={
          <Button variant="secondary" onClick={() => void loadOrders()}>
            <RefreshCw size={16} /> Đồng bộ đơn hàng
          </Button>
        }
      />

      <section className="card">
        <div className="toolbar">
          <label className="search-field">
            <Search size={18} />
            <Input
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Tìm mã đơn hàng hoặc khách hàng..."
            />
          </label>
          <Select value={channelFilter} onChange={(e) => setChannelFilter(e.target.value)}>
            <option value="">Tất cả kênh bán</option>
            <option value="POS">POS tại quầy</option>
            <option value="Admin">Web Admin</option>
            <option value="Shopee">Shopee</option>
            <option value="TikTok">TikTok Shop</option>
          </Select>
        </div>

        {errorMsg && (
          <div
            role="status"
            style={{
              margin: '16px',
              padding: '12px 16px',
              borderRadius: '10px',
              background: '#FFFBEB',
              border: '1px solid #FDE68A',
              color: '#92400E',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              fontSize: '13px',
            }}
          >
            <span style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <AlertCircle size={18} />
              <span>Endpoint đơn hàng (`/api/orders`): {errorMsg}</span>
            </span>
            <Button variant="secondary" onClick={() => void loadOrders()}>
              Thử lại
            </Button>
          </div>
        )}

        {loading ? (
          <div style={{ padding: '16px' }}>
            <TableSkeleton rows={5} columns={6} />
          </div>
        ) : orders.length === 0 ? (
          <EmptyState
            title="Chưa có đơn hàng nào"
            description="Đơn hàng phát sinh từ POS hoặc kênh bán hàng sẽ hiển thị trực tiếp tại đây."
            action={
              <Button variant="secondary" onClick={() => void loadOrders()}>
                <ShoppingBag size={16} /> Kiểm tra lại
              </Button>
            }
          />
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Mã đơn</th>
                  <th>Kênh bán</th>
                  <th>Khách hàng</th>
                  <th>Chi nhánh</th>
                  <th>Tổng tiền</th>
                  <th>Trạng thái</th>
                  <th>Thời gian tạo</th>
                </tr>
              </thead>
              <tbody>
                {orders.map((order) => (
                  <tr key={order.id}>
                    <td>
                      <strong className="mono">{order.code}</strong>
                    </td>
                    <td>{order.channel}</td>
                    <td>{order.customerName}</td>
                    <td>{order.branchName}</td>
                    <td>
                      <strong>{order.totalAmount.toLocaleString('vi-VN')} ₫</strong>
                    </td>
                    <td>
                      <OrderBadge status={order.status} />
                    </td>
                    <td>{new Date(order.createdAt).toLocaleString('vi-VN')}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}
