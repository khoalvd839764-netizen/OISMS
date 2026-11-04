import React, { useCallback, useEffect, useState } from 'react';
import {
  AlertCircle,
  Building2,
  Calendar,
  ChevronLeft,
  ChevronRight,
  Eye,
  FileText,
  Filter,
  GitBranch,
  Loader2,
  Printer,
  RefreshCw,
  Search,
  ShoppingBag,
} from 'lucide-react';
import { apiAdapter } from '../services/apiAdapter';
import { salesInvoiceService } from '../services/salesInvoiceService';
import type { SalesInvoiceFilter, SalesInvoiceSummary } from '../types/salesInvoices';
import { SalesInvoiceDetailModal } from '../components/salesInvoices/SalesInvoiceDetailModal';
import { SalesInvoiceReprintModal } from '../components/salesInvoices/SalesInvoiceReprintModal';
import { SalesInvoiceTraceModal } from '../components/salesInvoices/SalesInvoiceTraceModal';

export const SalesInvoicesPage: React.FC = () => {
  const [invoices, setInvoices] = useState<SalesInvoiceSummary[]>([]);
  const [branches, setBranches] = useState<Array<{ id: string; name: string }>>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [apiError, setApiError] = useState<{ code?: string; message: string } | null>(null);

  // Pagination & Filters state
  const [pageIndex, setPageIndex] = useState<number>(1);
  const [pageSize] = useState<number>(15);
  const [totalCount, setTotalCount] = useState<number>(0);
  const [totalPages, setTotalPages] = useState<number>(1);

  const [filterNumber, setFilterNumber] = useState<string>('');
  const [filterBranchId, setFilterBranchId] = useState<string>('');
  const [filterFromDate, setFilterFromDate] = useState<string>('');
  const [filterToDate, setFilterToDate] = useState<string>('');

  // Active Modals state
  const [detailInvoiceId, setDetailInvoiceId] = useState<string | null>(null);
  const [reprintData, setReprintData] = useState<{ id?: string; number?: string } | null>(null);
  const [traceData, setTraceData] = useState<{ id: string; number: string } | null>(null);

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

  // Load Invoices
  const loadInvoices = useCallback(async () => {
    setLoading(true);
    setApiError(null);
    try {
      const filter: SalesInvoiceFilter = {
        pageIndex,
        pageSize,
      };
      if (filterNumber.trim()) filter.number = filterNumber.trim();
      if (filterBranchId) filter.branchId = filterBranchId;
      if (filterFromDate) filter.fromDate = new Date(filterFromDate).toISOString();
      if (filterToDate) {
        const to = new Date(filterToDate);
        to.setHours(23, 59, 59, 999);
        filter.toDate = to.toISOString();
      }

      const res = await salesInvoiceService.getSalesInvoices(filter);
      setInvoices(res.items || []);
      setTotalCount(res.totalCount || 0);
      setTotalPages(res.totalPages || 1);
    } catch (err: any) {
      const errorData = err.response?.data;
      const code = errorData?.errorCode || errorData?.extensions?.errorCode || err.code;
      const message =
        errorData?.detail ||
        errorData?.message ||
        err.message ||
        'Không thể tải danh sách hóa đơn bán hàng từ máy chủ.';
      setApiError({ code, message });
      setInvoices([]);
    } finally {
      setLoading(false);
    }
  }, [pageIndex, pageSize, filterNumber, filterBranchId, filterFromDate, filterToDate]);

  useEffect(() => {
    loadInvoices();
  }, [loadInvoices]);

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPageIndex(1);
    loadInvoices();
  };

  const handleResetFilters = () => {
    setFilterNumber('');
    setFilterBranchId('');
    setFilterFromDate('');
    setFilterToDate('');
    setPageIndex(1);
  };

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
  };

  return (
    <div className="p-6 max-w-7xl mx-auto space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2.5">
            <div className="p-2 bg-indigo-50 text-indigo-600 rounded-xl">
              <FileText className="w-6 h-6" />
            </div>
            <div>
              <h1 className="text-xl font-black text-slate-900 tracking-tight">
                Hóa Đơn Bán Hàng & Truy Vết
              </h1>
              <p className="text-xs text-slate-500 mt-0.5">
                Quản lý hóa đơn bán lẻ, in lại biên lai nhiệt và truy vết liên thông Đơn hàng - Dòng sổ kho xuất (OUT)
              </p>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => loadInvoices()}
            disabled={loading}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 bg-white border border-slate-200 hover:bg-slate-50 rounded-xl text-xs font-semibold text-slate-700 shadow-xs transition-colors"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
            <span>Tải lại</span>
          </button>
        </div>
      </div>

      {/* Filter Bar */}
      <div className="bg-white p-4 rounded-2xl border border-slate-200/80 shadow-xs">
        <form onSubmit={handleSearchSubmit} className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3">
          {/* Number filter */}
          <div>
            <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">
              Số hóa đơn (INV-)
            </label>
            <div className="relative">
              <input
                type="text"
                value={filterNumber}
                onChange={(e) => setFilterNumber(e.target.value)}
                placeholder="INV-..."
                className="w-full pl-8 pr-3 py-1.5 bg-slate-50 border border-slate-200 rounded-xl text-xs font-mono focus:bg-white focus:border-indigo-500 focus:outline-hidden transition-colors"
              />
              <Search className="w-3.5 h-3.5 text-slate-400 absolute left-2.5 top-2.5" />
            </div>
          </div>

          {/* Branch filter */}
          <div>
            <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">
              Chi nhánh
            </label>
            <div className="relative">
              <select
                value={filterBranchId}
                onChange={(e) => setFilterBranchId(e.target.value)}
                className="w-full pl-8 pr-3 py-1.5 bg-slate-50 border border-slate-200 rounded-xl text-xs focus:bg-white focus:border-indigo-500 focus:outline-hidden transition-colors"
              >
                <option value="">Tất cả chi nhánh</option>
                {branches.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </select>
              <Building2 className="w-3.5 h-3.5 text-slate-400 absolute left-2.5 top-2.5 pointer-events-none" />
            </div>
          </div>

          {/* From Date */}
          <div>
            <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">
              Từ ngày
            </label>
            <div className="relative">
              <input
                type="date"
                value={filterFromDate}
                onChange={(e) => setFilterFromDate(e.target.value)}
                className="w-full pl-8 pr-3 py-1.5 bg-slate-50 border border-slate-200 rounded-xl text-xs focus:bg-white focus:border-indigo-500 focus:outline-hidden transition-colors"
              />
              <Calendar className="w-3.5 h-3.5 text-slate-400 absolute left-2.5 top-2.5" />
            </div>
          </div>

          {/* To Date */}
          <div>
            <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">
              Đến ngày
            </label>
            <div className="relative">
              <input
                type="date"
                value={filterToDate}
                onChange={(e) => setFilterToDate(e.target.value)}
                className="w-full pl-8 pr-3 py-1.5 bg-slate-50 border border-slate-200 rounded-xl text-xs focus:bg-white focus:border-indigo-500 focus:outline-hidden transition-colors"
              />
              <Calendar className="w-3.5 h-3.5 text-slate-400 absolute left-2.5 top-2.5" />
            </div>
          </div>

          {/* Action buttons */}
          <div className="flex items-end gap-2">
            <button
              type="submit"
              className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-1.5 bg-indigo-600 hover:bg-indigo-700 text-white rounded-xl text-xs font-semibold shadow-xs transition-colors"
            >
              <Filter className="w-3.5 h-3.5" />
              <span>Lọc</span>
            </button>
            <button
              type="button"
              onClick={handleResetFilters}
              className="px-3 py-1.5 bg-slate-100 hover:bg-slate-200 text-slate-600 rounded-xl text-xs font-semibold transition-colors"
            >
              Đặt lại
            </button>
          </div>
        </form>
      </div>

      {/* Error state */}
      {apiError && (
        <div className="p-4 bg-rose-50 border border-rose-200 rounded-2xl text-rose-800 flex items-start gap-3">
          <AlertCircle className="w-5 h-5 shrink-0 mt-0.5" />
          <div>
            <div className="font-bold text-sm">
              Lỗi tải hóa đơn {apiError.code ? `[${apiError.code}]` : ''}
            </div>
            <div className="text-xs mt-0.5 text-rose-700">{apiError.message}</div>
          </div>
        </div>
      )}

      {/* Main Table */}
      <div className="bg-white border border-slate-200 rounded-2xl overflow-hidden shadow-xs">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead className="bg-slate-50 border-b border-slate-200 text-slate-600 font-bold uppercase text-[10px] tracking-wider">
              <tr>
                <th className="py-3 px-4">Số hóa đơn</th>
                <th className="py-3 px-4">Đơn hàng gốc</th>
                <th className="py-3 px-4">Chi nhánh</th>
                <th className="py-3 px-4">Khách hàng</th>
                <th className="py-3 px-4">Người lập</th>
                <th className="py-3 px-4">Thời gian lập</th>
                <th className="py-3 px-4 text-right">Tổng tiền</th>
                <th className="py-3 px-4 text-center">Thao tác</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 font-sans">
              {loading && (
                <tr>
                  <td colSpan={8} className="py-12 text-center text-slate-400">
                    <Loader2 className="w-6 h-6 animate-spin mx-auto text-indigo-600 mb-2" />
                    <span>Đang tải danh sách hóa đơn...</span>
                  </td>
                </tr>
              )}

              {!loading && !apiError && invoices.length === 0 && (
                <tr>
                  <td colSpan={8} className="py-12 text-center text-slate-400">
                    Không tìm thấy hóa đơn bán hàng nào phù hợp với bộ lọc.
                  </td>
                </tr>
              )}

              {!loading &&
                invoices.map((inv) => (
                  <tr key={inv.id} className="hover:bg-slate-50/80 transition-colors">
                    <td className="py-3 px-4">
                      <span className="font-mono font-bold text-indigo-600">
                        {inv.number}
                      </span>
                    </td>
                    <td className="py-3 px-4">
                      <div className="flex items-center gap-1 font-mono font-semibold text-slate-700">
                        <ShoppingBag className="w-3.5 h-3.5 text-slate-400" />
                        <span>{inv.orderNumber}</span>
                      </div>
                    </td>
                    <td className="py-3 px-4 font-semibold text-slate-700">
                      {inv.branchName}
                    </td>
                    <td className="py-3 px-4 text-slate-700">
                      {inv.customerName || <span className="text-slate-400">Khách lẻ</span>}
                    </td>
                    <td className="py-3 px-4 text-slate-600">
                      {inv.issuedByName || 'Thu ngân'}
                    </td>
                    <td className="py-3 px-4 text-slate-500 whitespace-nowrap">
                      {new Date(inv.issuedAt).toLocaleString('vi-VN')}
                    </td>
                    <td className="py-3 px-4 text-right font-mono font-bold text-slate-900 whitespace-nowrap">
                      {formatCurrency(inv.totalAmount)}
                    </td>
                    <td className="py-3 px-4 text-center">
                      <div className="flex items-center justify-center gap-1">
                        <button
                          type="button"
                          onClick={() => setDetailInvoiceId(inv.id)}
                          title="Xem chi tiết hóa đơn"
                          className="p-1.5 hover:bg-slate-100 rounded-lg text-slate-600 hover:text-indigo-600 transition-colors"
                        >
                          <Eye className="w-4 h-4" />
                        </button>
                        <button
                          type="button"
                          onClick={() => setReprintData({ id: inv.id, number: inv.number })}
                          title="In lại hóa đơn (khổ 80mm / 58mm)"
                          className="p-1.5 hover:bg-slate-100 rounded-lg text-slate-600 hover:text-indigo-600 transition-colors"
                        >
                          <Printer className="w-4 h-4" />
                        </button>
                        <button
                          type="button"
                          onClick={() => setTraceData({ id: inv.id, number: inv.number })}
                          title="Truy vết chuỗi chứng từ & dòng sổ kho OUT"
                          className="p-1.5 hover:bg-slate-100 rounded-lg text-emerald-600 hover:text-emerald-700 hover:bg-emerald-50 transition-colors"
                        >
                          <GitBranch className="w-4 h-4" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
            </tbody>
          </table>
        </div>

        {/* Pagination Bar */}
        <div className="px-5 py-3.5 bg-slate-50 border-t border-slate-200 flex items-center justify-between text-xs text-slate-600">
          <div>
            Tổng số: <span className="font-bold text-slate-900">{totalCount}</span> hóa đơn
            {totalPages > 1 && ` (Trang ${pageIndex} / ${totalPages})`}
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={pageIndex <= 1 || loading}
              onClick={() => setPageIndex((p) => Math.max(1, p - 1))}
              className="p-1.5 border border-slate-200 rounded-lg bg-white disabled:opacity-40 disabled:pointer-events-none hover:bg-slate-100 transition-colors"
            >
              <ChevronLeft className="w-4 h-4" />
            </button>
            <button
              type="button"
              disabled={pageIndex >= totalPages || loading}
              onClick={() => setPageIndex((p) => Math.min(totalPages, p + 1))}
              className="p-1.5 border border-slate-200 rounded-lg bg-white disabled:opacity-40 disabled:pointer-events-none hover:bg-slate-100 transition-colors"
            >
              <ChevronRight className="w-4 h-4" />
            </button>
          </div>
        </div>
      </div>

      {/* Detail Modal */}
      {detailInvoiceId && (
        <SalesInvoiceDetailModal
          invoiceId={detailInvoiceId}
          onClose={() => setDetailInvoiceId(null)}
          onOpenReprint={(id, num) => {
            setDetailInvoiceId(null);
            setReprintData({ id, number: num });
          }}
          onOpenTrace={(id, num) => {
            setDetailInvoiceId(null);
            setTraceData({ id, number: num });
          }}
        />
      )}

      {/* Reprint Modal */}
      {reprintData && (
        <SalesInvoiceReprintModal
          invoiceId={reprintData.id}
          invoiceNumber={reprintData.number}
          onClose={() => setReprintData(null)}
        />
      )}

      {/* Trace Modal */}
      {traceData && (
        <SalesInvoiceTraceModal
          invoiceId={traceData.id}
          invoiceNumber={traceData.number}
          onClose={() => setTraceData(null)}
        />
      )}
    </div>
  );
};
