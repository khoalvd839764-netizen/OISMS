import React, { useState, useEffect, useRef, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  AlertCircle,
  ChevronLeft,
  ChevronRight,
  Eye,
  Filter,
  Loader2,
  Plus,
  RefreshCw,
  Search,
} from 'lucide-react';
import { Button, PageTitle, Badge, EmptyState } from '../components/ui';
import { apiAdapter } from '../services/apiAdapter';
import {
  salesReturnService,
  getSalesReturnErrorMessage,
} from '../services/salesReturnService';
import { CreateSalesReturnModal } from '../components/salesReturns/CreateSalesReturnModal';
import { SalesReturnDetailDrawer } from '../components/salesReturns/SalesReturnDetailDrawer';
import { OrderDetailDrawer } from '../components/orders/OrderDetailDrawer';
import { SalesInvoiceTraceModal } from '../components/salesInvoices/SalesInvoiceTraceModal';
import type {
  SalesReturn,
  SalesReturnFilter,
} from '../types/salesReturns';
import { SALES_RETURN_STATUS_LABEL } from '../types/salesReturns';

export const SalesReturnsPage: React.FC = () => {
  const [returns, setReturns] = useState<SalesReturn[]>([]);
  const [branches, setBranches] = useState<Array<{ id: string; name: string }>>([]);
  const [loading, setLoading] = useState(true);
  const [apiError, setApiError] = useState<{ code?: string; message: string } | null>(null);

  // Pagination & Filters
  const [pageIndex, setPageIndex] = useState(1);
  const [pageSize] = useState(15);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);

  const [searchTerm, setSearchTerm] = useState('');
  const [appliedSearchTerm, setAppliedSearchTerm] = useState('');
  const [filterBranchId, setFilterBranchId] = useState('');
  const [filterStatus, setFilterStatus] = useState('');
  const [filterFromDate, setFilterFromDate] = useState('');
  const [filterToDate, setFilterToDate] = useState('');
  const loadSeqRef = useRef(0);

  // Modals & Drawers state
  const [searchParams] = useSearchParams();
  const urlReturnId = searchParams.get('returnId');
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [detailReturnId, setDetailReturnId] = useState<string | null>(null);

  useEffect(() => {
    if (urlReturnId) {
      setDetailReturnId(urlReturnId);
    }
  }, [urlReturnId]);
  const [detailOrderId, setDetailOrderId] = useState<string | null>(null);
  const [traceOrderData, setTraceOrderData] = useState<{
    id: string;
    number: string;
  } | null>(null);

  // Load branches
  useEffect(() => {
    let isMounted = true;
    apiAdapter
      .getBranches()
      .then((res) => {
        if (isMounted && res.data) {
          setBranches(res.data);
        }
      })
      .catch(() => {});

    return () => {
      isMounted = false;
    };
  }, []);

  // Load Sales Returns
  const loadReturns = useCallback(async () => {
    const seq = ++loadSeqRef.current;
    setLoading(true);
    setApiError(null);

    try {
      const filter: SalesReturnFilter = {
        pageIndex,
        pageSize,
      };

      if (appliedSearchTerm) filter.searchTerm = appliedSearchTerm;
      if (filterBranchId) filter.branchId = filterBranchId;
      if (filterStatus) filter.status = filterStatus;

      // Lọc ngày nghiệp vụ theo UTC+07:00, toDate nửa mở [fromDate, toDate)
      if (filterFromDate) {
        filter.fromDate = new Date(`${filterFromDate}T00:00:00+07:00`).toISOString();
      }
      if (filterToDate) {
        const endOfDay = new Date(`${filterToDate}T00:00:00+07:00`).getTime() + 24 * 60 * 60 * 1000;
        filter.toDate = new Date(endOfDay).toISOString();
      }

      const res = await salesReturnService.list(filter);
      if (seq !== loadSeqRef.current) return;

      setReturns(res.items || []);
      setTotalCount(res.totalCount || 0);
      setTotalPages(res.totalPages || 1);
    } catch (err: any) {
      if (seq !== loadSeqRef.current) return;
      const parsed = getSalesReturnErrorMessage(err);
      setApiError(parsed);
      setReturns([]);
    } finally {
      if (seq === loadSeqRef.current) {
        setLoading(false);
      }
    }
  }, [pageIndex, pageSize, appliedSearchTerm, filterBranchId, filterStatus, filterFromDate, filterToDate]);

  useEffect(() => {
    loadReturns();
  }, [loadReturns]);

  const handleApplySearch = (e: React.FormEvent) => {
    e.preventDefault();
    setPageIndex(1);
    setAppliedSearchTerm(searchTerm.trim());
  };

  const getStatusBadgeTone = (status: string): 'neutral' | 'success' | 'danger' => {
    if (status === 'Confirmed') return 'success';
    if (status === 'Cancelled') return 'danger';
    return 'neutral';
  };

  return (
    <>
      <PageTitle
        eyebrow="Quản lý bán hàng & đổi trả"
        title="Phiếu trả hàng & hoàn tiền"
        description="Quản lý chu trình khách đổi trả hàng, nhập lại kho, tự động tính lại WAC và khấu trừ doanh thu thuần."
        actions={
          <>
            <Button variant="secondary" onClick={() => loadReturns()} disabled={loading}>
              <RefreshCw size={17} className={loading ? 'animate-spin' : ''} />
              Làm mới
            </Button>
            <Button variant="primary" onClick={() => setShowCreateModal(true)}>
              <Plus size={17} />
              Tạo phiếu trả
            </Button>
          </>
        }
      />

      {/* Filter Bar */}
      <div className="bg-white p-4 rounded-xl border border-gray-200 shadow-xs mb-4 space-y-3 text-xs">
        <div className="flex flex-wrap items-center gap-3">
          {/* Search Term */}
          <form onSubmit={handleApplySearch} className="flex-1 min-w-[240px]">
            <div className="relative">
              <Search className="w-4 h-4 absolute left-3 top-2.5 text-gray-400" />
              <input
                type="text"
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                placeholder="Tìm theo số phiếu trả (SR-...) hoặc mã đơn (ORD-...)"
                className="input pl-9 w-full"
              />
            </div>
          </form>

          {/* Branch Filter */}
          <div className="w-48">
            <select
              value={filterBranchId}
              onChange={(e) => {
                setPageIndex(1);
                setFilterBranchId(e.target.value);
              }}
              className="input w-full"
            >
              <option value="">Tất cả chi nhánh</option>
              {branches.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </select>
          </div>

          {/* Status Filter */}
          <div className="w-40">
            <select
              value={filterStatus}
              onChange={(e) => {
                setPageIndex(1);
                setFilterStatus(e.target.value);
              }}
              className="input w-full"
            >
              <option value="">Tất cả trạng thái</option>
              <option value="Draft">Nháp (Draft)</option>
              <option value="Confirmed">Đã xác nhận (Confirmed)</option>
              <option value="Cancelled">Đã hủy (Cancelled)</option>
            </select>
          </div>

          {/* Date Range */}
          <div className="flex items-center gap-1.5">
            <input
              type="date"
              value={filterFromDate}
              onChange={(e) => {
                setPageIndex(1);
                setFilterFromDate(e.target.value);
              }}
              className="input text-xs"
              title="Từ ngày xác nhận"
            />
            <span className="text-gray-400">➔</span>
            <input
              type="date"
              value={filterToDate}
              onChange={(e) => {
                setPageIndex(1);
                setFilterToDate(e.target.value);
              }}
              className="input text-xs"
              title="Đến ngày xác nhận"
            />
          </div>

          <Button variant="secondary" onClick={() => loadReturns()}>
            <Filter size={15} />
            Lọc
          </Button>
        </div>
      </div>

      {/* Error Banner */}
      {apiError && (
        <div className="p-4 bg-rose-50 border border-rose-200 rounded-xl flex items-center justify-between text-xs text-rose-800 mb-4">
          <div className="flex items-center gap-2">
            <AlertCircle className="w-4 h-4 flex-none" />
            <div>
              <strong>Lỗi tải danh sách: </strong>
              <span>{apiError.message}</span>
            </div>
          </div>
          <Button variant="danger" onClick={() => loadReturns()} style={{ padding: '4px 10px' }}>
            Thử lại
          </Button>
        </div>
      )}

      {/* Table Content */}
      <div className="bg-white rounded-xl border border-gray-200 shadow-xs overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-xs text-left border-collapse">
            <thead className="bg-gray-50 border-b border-gray-200 text-gray-600 font-semibold">
              <tr>
                <th className="p-3">Số phiếu trả</th>
                <th className="p-3">Đơn hàng gốc</th>
                <th className="p-3">Kênh</th>
                <th className="p-3">Chi nhánh / Kho nhận</th>
                <th className="p-3 text-center">Số dòng</th>
                <th className="p-3 text-right">Tiền hoàn</th>
                <th className="p-3 text-center">Trạng thái</th>
                <th className="p-3">Người tạo</th>
                <th className="p-3">Ngày xác nhận / tạo</th>
                <th className="p-3 text-right">Thao tác</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {loading ? (
                <tr>
                  <td colSpan={10} className="p-12 text-center text-gray-400">
                    <Loader2 className="w-6 h-6 animate-spin mx-auto mb-2 text-indigo-600" />
                    Đang tải danh sách phiếu trả hàng...
                  </td>
                </tr>
              ) : returns.length === 0 ? (
                <tr>
                  <td colSpan={10} className="p-8 text-center">
                    <EmptyState
                      title="Chưa có phiếu trả hàng"
                      description="Chưa có phiếu trả hàng nào được tạo phù hợp với bộ lọc hiện tại."
                    />
                  </td>
                </tr>
              ) : (
                returns.map((ret) => (
                  <tr key={ret.id} className="hover:bg-gray-50/80 transition-colors">
                    <td className="p-3 font-bold text-indigo-600 cursor-pointer hover:underline" onClick={() => setDetailReturnId(ret.id)}>
                      {ret.number}
                    </td>
                    <td className="p-3">
                      <button
                        type="button"
                        onClick={() => setDetailOrderId(ret.orderId)}
                        className="font-medium text-gray-900 hover:text-indigo-600 hover:underline"
                      >
                        {ret.orderNumber}
                      </button>
                    </td>
                    <td className="p-3">
                      <Badge tone="neutral">{ret.orderChannel}</Badge>
                    </td>
                    <td className="p-3">
                      <div className="font-semibold text-gray-900">{ret.branchName}</div>
                      <div className="text-[11px] text-gray-500">{ret.warehouseName}</div>
                    </td>
                    <td className="p-3 text-center font-medium text-gray-800">
                      {ret.items?.length || 0}
                    </td>
                    <td className="p-3 text-right font-bold text-indigo-700">
                      {(ret.totalRefundAmount || 0).toLocaleString('vi-VN')} đ
                    </td>
                    <td className="p-3 text-center">
                      <Badge tone={getStatusBadgeTone(ret.status)}>
                        {SALES_RETURN_STATUS_LABEL[ret.status] || ret.status}
                      </Badge>
                    </td>
                    <td className="p-3 text-gray-600">
                      {ret.createdByName || ret.createdBy || '—'}
                    </td>
                    <td className="p-3 text-gray-600">
                      {ret.confirmedAt
                        ? new Date(ret.confirmedAt).toLocaleString('vi-VN')
                        : new Date(ret.createdAt).toLocaleString('vi-VN')}
                    </td>
                    <td className="p-3 text-right">
                      <Button
                        variant="secondary"
                        onClick={() => setDetailReturnId(ret.id)}
                        style={{ padding: '4px 8px', fontSize: 11 }}
                      >
                        <Eye size={13} className="mr-1" />
                        Chi tiết
                      </Button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination Bar */}
        <div className="p-3 border-t border-gray-100 flex items-center justify-between text-xs text-gray-600 bg-gray-50/40">
          <div>
            Hiển thị <strong>{returns.length}</strong> / <strong>{totalCount}</strong> phiếu trả
          </div>
          <div className="flex items-center gap-2">
            <Button
              variant="secondary"
              disabled={pageIndex <= 1 || loading}
              onClick={() => setPageIndex((p) => Math.max(1, p - 1))}
              style={{ padding: '4px 8px' }}
            >
              <ChevronLeft size={14} />
              Trước
            </Button>
            <span>
              Trang <strong>{pageIndex}</strong> / <strong>{totalPages || 1}</strong>
            </span>
            <Button
              variant="secondary"
              disabled={pageIndex >= totalPages || loading}
              onClick={() => setPageIndex((p) => Math.min(totalPages, p + 1))}
              style={{ padding: '4px 8px' }}
            >
              Sau
              <ChevronRight size={14} />
            </Button>
          </div>
        </div>
      </div>

      {/* Create Return Modal (G2) */}
      <CreateSalesReturnModal
        isOpen={showCreateModal}
        onClose={() => setShowCreateModal(false)}
        onSuccess={(ticket, shouldOpenDetail) => {
          loadReturns();
          if (shouldOpenDetail && ticket) {
            setDetailReturnId(ticket.id);
          }
        }}
      />

      {/* Return Detail Drawer (G3) */}
      <SalesReturnDetailDrawer
        returnId={detailReturnId}
        onClose={() => setDetailReturnId(null)}
        onRefresh={() => loadReturns()}
        onOpenOrder={(orderId) => setDetailOrderId(orderId)}
        onOpenTrace={(orderId, orderNumber) =>
          setTraceOrderData({ id: orderId, number: orderNumber })
        }
      />

      {/* Order Detail Drawer */}
      <OrderDetailDrawer
        orderId={detailOrderId}
        onClose={() => setDetailOrderId(null)}
        onRefresh={() => loadReturns()}
      />

      {/* Trace Modal if opened from SalesReturn */}
      {traceOrderData && (
        <SalesInvoiceTraceModal
          invoiceId={traceOrderData.id}
          invoiceNumber={traceOrderData.number}
          onClose={() => setTraceOrderData(null)}
        />
      )}
    </>
  );
};
