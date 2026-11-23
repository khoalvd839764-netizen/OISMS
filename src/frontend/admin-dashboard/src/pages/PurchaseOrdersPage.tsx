import React, { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { Badge, Button, EmptyState, PageTitle, Select } from '../components/ui';
import { purchaseOrderService } from '../services/purchaseOrderService';
import { apiAdapter } from '../services/apiAdapter';
import type {
  PurchaseOrderResponse,
  PurchaseOrderStatus,
} from '../types/purchaseOrder';
import { CreatePurchaseOrderModal } from '../components/purchase/CreatePurchaseOrderModal';
import { PurchaseOrderDetailModal } from '../components/purchase/PurchaseOrderDetailModal';

export const PurchaseOrdersPage: React.FC = () => {
  const { account } = useAuth();
  const isOwner = account?.role === 'Owner';
  const navigate = useNavigate();

  const [orders, setOrders] = useState<PurchaseOrderResponse[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [pageIndex, setPageIndex] = useState(1);
  const [pageSize] = useState(15);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [statusFilter, setStatusFilter] = useState<string>('');
  // Staff không được lọc NCC trên đơn chưa duyệt (backend trả 403 để không suy ra NCC Owner đang chọn, G1 F-03).
  const supplierFilterLocked = !isOwner && (statusFilter === 'Draft' || statusFilter === 'PendingApproval');
  const [branchFilter, setBranchFilter] = useState<string>('');
  const [supplierFilter, setSupplierFilter] = useState<string>('');

  const [branches, setBranches] = useState<Array<{ id: string; name: string }>>([]);
  const [suppliers, setSuppliers] = useState<Array<{ id: string; name: string }>>([]);

  // Modals
  const [selectedPo, setSelectedPo] = useState<PurchaseOrderResponse | null>(null);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [poToEdit, setPoToEdit] = useState<PurchaseOrderResponse | null>(null);

  useEffect(() => {
    let active = true;
    Promise.all([apiAdapter.getBranches(), apiAdapter.getSuppliers()]).then(
      ([bRes, sRes]) => {
        if (!active) return;
        if (bRes.data) setBranches(bRes.data);
        if (sRes.data) setSuppliers(sRes.data);
      }
    );
    return () => {
      active = false;
    };
  }, []);

  // Bỏ qua response của lần tải cũ khi người dùng đổi bộ lọc liên tiếp (response về không theo thứ tự).
  const latestFetchRef = useRef(0);

  const fetchOrders = async () => {
    const fetchId = ++latestFetchRef.current;
    setLoading(true);
    setError(null);
    try {
      const res = await purchaseOrderService.getPurchaseOrders({
        status: statusFilter || undefined,
        branchId: branchFilter || undefined,
        supplierId: supplierFilterLocked ? undefined : supplierFilter || undefined,
        pageIndex,
        pageSize,
      });
      if (fetchId !== latestFetchRef.current) return;
      setOrders(res.items || []);
      setTotalCount(res.totalCount || 0);
      setTotalPages(res.totalPages || 1);
    } catch (err: any) {
      if (fetchId !== latestFetchRef.current) return;
      setError(
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        'Không thể tải danh sách đơn mua hàng.'
      );
    } finally {
      if (fetchId === latestFetchRef.current) setLoading(false);
    }
  };

  useEffect(() => {
    fetchOrders();
  }, [pageIndex, statusFilter, branchFilter, supplierFilter]);

  const getStatusBadge = (status: PurchaseOrderStatus) => {
    switch (status) {
      case 'Draft':
        return <Badge tone="warning">Đề nghị mua (Draft)</Badge>;
      case 'PendingApproval':
        return <Badge tone="warning">Chờ duyệt giá (PendingApproval)</Badge>;
      case 'Approved':
        return <Badge tone="info">Đã duyệt (Approved)</Badge>;
      case 'PartiallyReceived':
        return <Badge tone="warning">Đã nhận một phần</Badge>;
      case 'Received':
        return <Badge tone="success">Đã nhận đủ</Badge>;
      case 'Closed':
        return <Badge tone="neutral">Đã đóng</Badge>;
      case 'Cancelled':
        return <Badge tone="danger">Đã hủy</Badge>;
      default:
        return <Badge tone="neutral">{status}</Badge>;
    }
  };

  const handleCreateReceipt = (po: PurchaseOrderResponse) => {
    navigate(`/inventory?purchaseOrderId=${po.id}`);
  };

  return (
    <div style={{ padding: 24 }}>
      <PageTitle
        eyebrow="Quản lý Mua hàng"
        title="Đơn mua hàng từ Nhà cung cấp"
        description={
          isOwner
            ? 'Tiếp nhận đề nghị mua, chốt nhà cung cấp, giá mua & phân bổ chi phí mua hàng theo VAS 02, và theo dõi tiến độ nhập kho.'
            : 'Lập đề nghị mua hàng theo chi nhánh và SKU, gửi chờ duyệt và theo dõi tiến độ nhận hàng nhập kho.'
        }
        actions={
          <div style={{ display: 'flex', gap: 12 }}>
            <Button
              variant="secondary"
              onClick={() => navigate('/inventory')}
            >
              Sang Quản lý Kho
            </Button>
            <Button
              variant="primary"
              onClick={() => {
                setPoToEdit(null);
                setShowCreateModal(true);
              }}
            >
              {isOwner ? '+ Tạo đơn mua hàng' : '+ Đề nghị mua hàng'}
            </Button>
          </div>
        }
      />

      {/* Filter Bar */}
      <div
        style={{
          display: 'flex',
          gap: 16,
          alignItems: 'center',
          flexWrap: 'wrap',
          marginBottom: 16,
          background: 'var(--surface, #ffffff)',
          padding: 16,
          borderRadius: 8,
          border: '1px solid var(--border, #e2e8f0)',
        }}
      >
        <div style={{ minWidth: 180 }}>
          <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 4 }}>
            Trạng thái
          </label>
          <Select
            value={statusFilter}
            onChange={(e) => {
              setStatusFilter(e.target.value);
              setPageIndex(1);
            }}
          >
            <option value="">-- Tất cả trạng thái --</option>
            <option value="Draft">Đề nghị mua (Draft)</option>
            <option value="PendingApproval">Chờ duyệt giá (PendingApproval)</option>
            <option value="Approved">Đã duyệt (Approved)</option>
            <option value="PartiallyReceived">Đã nhận một phần</option>
            <option value="Received">Đã nhận đủ (Received)</option>
            <option value="Closed">Đã đóng (Closed)</option>
            <option value="Cancelled">Đã hủy (Cancelled)</option>
          </Select>
        </div>

        <div style={{ minWidth: 180 }}>
          <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 4 }}>
            Chi nhánh
          </label>
          <Select
            value={branchFilter}
            onChange={(e) => {
              setBranchFilter(e.target.value);
              setPageIndex(1);
            }}
          >
            <option value="">-- Tất cả chi nhánh --</option>
            {branches.map((b) => (
              <option key={b.id} value={b.id}>
                {b.name}
              </option>
            ))}
          </Select>
        </div>

        <div style={{ minWidth: 200 }}>
          <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 4 }}>
            Nhà cung cấp
          </label>
          <Select
            value={supplierFilterLocked ? '' : supplierFilter}
            disabled={supplierFilterLocked}
            title={supplierFilterLocked ? 'Đơn chưa duyệt không lọc theo nhà cung cấp' : undefined}
            onChange={(e) => {
              setSupplierFilter(e.target.value);
              setPageIndex(1);
            }}
          >
            <option value="">-- Tất cả nhà cung cấp --</option>
            {suppliers.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </Select>
        </div>

        <div style={{ marginTop: 'auto', paddingBottom: 2 }}>
          <Button variant="secondary" onClick={fetchOrders}>
            Làm mới
          </Button>
        </div>
      </div>

      {/* Error state */}
      {error && (
        <div
          role="alert"
          style={{
            padding: 12,
            borderRadius: 6,
            background: '#fee2e2',
            color: '#991b1b',
            marginBottom: 16,
          }}
        >
          {error}
        </div>
      )}

      {/* Table */}
      {loading ? (
        <div style={{ padding: 48, textAlign: 'center', color: 'var(--text-muted, #64748b)' }}>
          Đang tải danh sách đơn mua hàng...
        </div>
      ) : orders.length === 0 ? (
        <EmptyState
          title="Không tìm thấy đơn mua hàng"
          description="Chưa có đơn mua nào phù hợp với bộ lọc hiện tại. Bấm nút tạo ở góc phải để lập đơn mới."
        />
      ) : (
        <div className="table-wrap">
          <table style={{ width: '100%', fontSize: 14 }}>
            <thead>
              <tr>
                <th>Mã đơn mua</th>
                <th>Nhà cung cấp</th>
                <th>Chi nhánh</th>
                <th>Ngày đặt</th>
                <th>Số mặt hàng</th>
                {isOwner && <th>Tổng thanh toán</th>}
                <th>Trạng thái</th>
                <th style={{ textAlign: 'center' }}>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {orders.map((po) => (
                <tr key={po.id}>
                  <td>
                    <strong>{po.number}</strong>
                    {po.returnReason && po.status === 'Draft' && (
                      <div style={{ fontSize: 11, color: '#d97706', marginTop: 2 }}>
                        Đã trả về: {po.returnReason}
                      </div>
                    )}
                  </td>
                  <td>{po.supplierName || <span style={{ color: '#94a3b8' }}>Chưa chọn NCC</span>}</td>
                  <td>{po.branchName}</td>
                  <td>
                    {po.orderDate
                      ? new Date(po.orderDate).toLocaleDateString('vi-VN')
                      : new Date(po.createdAt).toLocaleDateString('vi-VN')}
                  </td>
                  <td>{po.items?.length || 0} SKU</td>
                  {isOwner && (
                    <td>
                      <strong>
                        {po.totalAmount != null
                          ? `${po.totalAmount.toLocaleString('vi-VN')} ₫`
                          : '—'}
                      </strong>
                    </td>
                  )}
                  <td>{getStatusBadge(po.status)}</td>
                  <td style={{ textAlign: 'center' }}>
                    <div style={{ display: 'flex', justifyContent: 'center', gap: 8 }}>
                      <Button
                        variant="secondary"
                        onClick={() => setSelectedPo(po)}
                        style={{ padding: '4px 10px', fontSize: 12 }}
                      >
                        Chi tiết
                      </Button>
                      {(po.status === 'Approved' || po.status === 'PartiallyReceived') && (
                        <Button
                          variant="primary"
                          onClick={() => handleCreateReceipt(po)}
                          style={{ padding: '4px 10px', fontSize: 12 }}
                        >
                          Nhập kho
                        </Button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Pagination */}
      {totalPages > 1 && (
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            marginTop: 16,
          }}
        >
          <span style={{ fontSize: 13, color: 'var(--text-muted, #64748b)' }}>
            Tổng số: <strong>{totalCount}</strong> đơn mua
          </span>
          <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <Button
              variant="secondary"
              disabled={pageIndex <= 1}
              onClick={() => setPageIndex((p) => Math.max(1, p - 1))}
            >
              Trang trước
            </Button>
            <span style={{ fontSize: 13 }}>
              Trang {pageIndex} / {totalPages}
            </span>
            <Button
              variant="secondary"
              disabled={pageIndex >= totalPages}
              onClick={() => setPageIndex((p) => p + 1)}
            >
              Trang sau
            </Button>
          </div>
        </div>
      )}

      {/* Modals */}
      {showCreateModal && (
        <CreatePurchaseOrderModal
          initialPo={poToEdit}
          userRole={account?.role}
          onClose={() => {
            setShowCreateModal(false);
            setPoToEdit(null);
          }}
          onSuccess={() => {
            setShowCreateModal(false);
            setPoToEdit(null);
            fetchOrders();
          }}
        />
      )}

      {selectedPo && (
        <PurchaseOrderDetailModal
          po={selectedPo}
          userRole={account?.role}
          currentUserId={account?.id}
          onClose={() => setSelectedPo(null)}
          onRefresh={() => {
            setSelectedPo(null);
            fetchOrders();
          }}
          onEdit={(po) => {
            setSelectedPo(null);
            setPoToEdit(po);
            setShowCreateModal(true);
          }}
          onCreateReceipt={(po) => {
            setSelectedPo(null);
            handleCreateReceipt(po);
          }}
        />
      )}
    </div>
  );
};
