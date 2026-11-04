import React, { useState, useEffect, useCallback, useRef } from 'react';
import type {
  StockCount,
  StockCountPeriod,
  StockCountSchedule,
  StockCountStatus,
} from '../types/stockCount';
import { stockCountService } from '../services/stockCountService';
import { StockCountsTable } from '../components/stockCounts/StockCountsTable';
import { CreateStockCountModal } from '../components/stockCounts/CreateStockCountModal';
import { StockCountDetailDrawer } from '../components/stockCounts/StockCountDetailDrawer';
import { CreateScheduleModal } from '../components/stockCounts/CreateScheduleModal';
import { apiAdapter } from '../services/apiAdapter';
import { useAuth } from '../context/AuthContext';
import {
  Plus,
  RefreshCw,
  ClipboardCheck,
  Calendar,
  Layers,
  ChevronLeft,
  ChevronRight,
  Play,
  Clock,
  AlertCircle,
  Building2,
  Boxes,
} from 'lucide-react';

export const StockCountsPage: React.FC = () => {
  const { user } = useAuth();
  const [activeTab, setActiveTab] = useState<'COUNTS' | 'PERIODS' | 'SCHEDULES'>('COUNTS');

  // Counts state
  const [counts, setCounts] = useState<StockCount[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Pagination states
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize] = useState(20);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);

  const countReqSeqRef = useRef(0);

  // Filters
  const [branches, setBranches] = useState<Array<{ id: string; name: string }>>([]);
  const [warehouses, setWarehouses] = useState<Array<{ id: string; name: string; branchId?: string }>>([]);
  const [selectedBranch, setSelectedBranch] = useState('');
  const [selectedWarehouse, setSelectedWarehouse] = useState('');
  const [selectedStatus, setSelectedStatus] = useState<StockCountStatus | ''>('');

  // Modals & Drawers
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isCreateScheduleOpen, setIsCreateScheduleOpen] = useState(false);
  const [selectedCountId, setSelectedCountId] = useState<string | null>(null);

  // Periods & Schedules state
  const [periods, setPeriods] = useState<StockCountPeriod[]>([]);
  const [schedules, setSchedules] = useState<StockCountSchedule[]>([]);
  const [periodsLoading, setPeriodsLoading] = useState(false);
  const [schedulesLoading, setSchedulesLoading] = useState(false);
  const [startingPeriodId, setStartingPeriodId] = useState<string | null>(null);

  const loadFilterOptions = async () => {
    try {
      const [brRes, whRes] = await Promise.all([
        apiAdapter.getBranches(),
        apiAdapter.getWarehouses(),
      ]);
      if (brRes.data) setBranches(brRes.data);
      if (whRes.data) setWarehouses(whRes.data);
    } catch (err) {
      console.warn('Lỗi nạp danh mục chi nhánh/kho:', err);
    }
  };

  const loadCounts = useCallback(async () => {
    const seq = ++countReqSeqRef.current;
    setLoading(true);
    setError(null);
    try {
      const data = await stockCountService.getStockCounts({
        branchId: selectedBranch || undefined,
        warehouseId: selectedWarehouse || undefined,
        status: selectedStatus || undefined,
        pageNumber,
        pageSize,
      });
      if (seq !== countReqSeqRef.current) return;

      if (data.totalPages > 0 && pageNumber > data.totalPages) {
        setPageNumber(1);
        return;
      }

      setCounts(data.items);
      setTotalCount(data.totalCount);
      setTotalPages(Math.max(1, data.totalPages));
    } catch (err: any) {
      if (seq !== countReqSeqRef.current) return;
      console.error('Lỗi nạp danh sách kiểm kê kho:', err);
      const msg =
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        err?.message ||
        'Không thể tải danh sách kiểm kê kho từ máy chủ.';
      setError(msg);
    } finally {
      if (seq === countReqSeqRef.current) {
        setLoading(false);
      }
    }
  }, [selectedBranch, selectedWarehouse, selectedStatus, pageNumber, pageSize]);

  const loadPeriods = useCallback(async () => {
    setPeriodsLoading(true);
    try {
      const res = await stockCountService.getPeriods({
        branchId: selectedBranch || undefined,
        warehouseId: selectedWarehouse || undefined,
      });
      setPeriods(res.items || []);
    } catch (err) {
      console.warn('Lỗi tải danh sách kỳ kiểm kê:', err);
    } finally {
      setPeriodsLoading(false);
    }
  }, [selectedBranch, selectedWarehouse]);

  const loadSchedules = useCallback(async () => {
    setSchedulesLoading(true);
    try {
      const res = await stockCountService.getSchedules({
        branchId: selectedBranch || undefined,
        warehouseId: selectedWarehouse || undefined,
      });
      setSchedules(res.items || []);
    } catch (err) {
      console.warn('Lỗi tải danh sách lịch kiểm kê:', err);
    } finally {
      setSchedulesLoading(false);
    }
  }, [selectedBranch, selectedWarehouse]);

  useEffect(() => {
    loadFilterOptions();
  }, []);

  useEffect(() => {
    if (activeTab === 'COUNTS') {
      loadCounts();
    } else if (activeTab === 'PERIODS') {
      loadPeriods();
    } else if (activeTab === 'SCHEDULES') {
      loadSchedules();
    }
  }, [activeTab, loadCounts, loadPeriods, loadSchedules]);

  const handleStartPeriod = async (periodId: string) => {
    setStartingPeriodId(periodId);
    try {
      const stockCount = await stockCountService.startPeriod(periodId);
      alert('Đã khởi tạo đợt kiểm kê cho kỳ này thành công!');
      loadPeriods();
      setSelectedCountId(stockCount.id);
    } catch (err: any) {
      const msg = err.response?.data?.detail || err.message || 'Lỗi bắt đầu kỳ kiểm kê.';
      alert(msg);
    } finally {
      setStartingPeriodId(null);
    }
  };

  const filteredWarehouses = warehouses.filter(
    (w) => !selectedBranch || !w.branchId || w.branchId === selectedBranch
  );

  return (
    <div className="p-6 max-w-7xl mx-auto space-y-5">
      {/* Page Header */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-3">
        <div>
          <div className="flex items-center gap-2">
            <ClipboardCheck className="w-7 h-7 text-indigo-600" />
            <h1 className="text-2xl font-bold text-gray-900 tracking-tight">
              Kiểm Kê Kho Hàng (U03)
            </h1>
          </div>
          <p className="text-xs text-gray-500 mt-1">
            Quy trình kiểm đếm thực tế (theo đơn vị gốc), trình nộp duyệt chênh lệch và cấu hình lịch định kỳ
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => {
              if (activeTab === 'COUNTS') loadCounts();
              else if (activeTab === 'PERIODS') loadPeriods();
              else if (activeTab === 'SCHEDULES') loadSchedules();
            }}
            disabled={loading || periodsLoading || schedulesLoading}
            className="p-2 border border-gray-300 rounded-lg text-gray-700 hover:bg-gray-50 flex items-center gap-1.5 text-xs font-semibold"
          >
            <RefreshCw
              className={`w-4 h-4 ${
                loading || periodsLoading || schedulesLoading ? 'animate-spin' : ''
              }`}
            />
            <span>Làm mới</span>
          </button>

          {activeTab === 'COUNTS' && (
            <button
              type="button"
              onClick={() => setIsCreateModalOpen(true)}
              className="px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg flex items-center gap-1.5 text-xs font-semibold shadow-sm"
            >
              <Plus className="w-4 h-4" />
              <span>Kiểm kê đột xuất (có lý do)</span>
            </button>
          )}

          {activeTab === 'SCHEDULES' && user?.role === 'Owner' && (
            <button
              type="button"
              onClick={() => setIsCreateScheduleOpen(true)}
              className="px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg flex items-center gap-1.5 text-xs font-semibold shadow-sm"
            >
              <Plus className="w-4 h-4" />
              <span>Thêm lịch định kỳ</span>
            </button>
          )}
        </div>
      </div>

      {/* Hangfire Status Notice Banner */}
      <div className="p-3.5 bg-amber-50/80 border border-amber-200 rounded-xl flex items-start gap-3 text-amber-900 text-xs">
        <Clock className="w-4 h-4 text-amber-600 shrink-0 mt-0.5" />
        <div>
          <span className="font-bold">Lưu ý cấu hình tác vụ nền: </span>
          <span>
            Hệ thống chạy ở chế độ quản trị trực tiếp (không kích hoạt Hangfire ngầm). Các kỳ kiểm kê được tạo và bắt đầu theo nhu cầu thực tế hoặc thao tác của Quản trị viên, không tự động phát sinh hàng tháng khi Hangfire tắt.
          </span>
        </div>
      </div>

      {/* Tab Navigation */}
      <div className="flex border-b border-gray-200 gap-6 text-sm font-semibold">
        <button
          type="button"
          onClick={() => setActiveTab('COUNTS')}
          className={`pb-2.5 flex items-center gap-2 border-b-2 transition-colors ${
            activeTab === 'COUNTS'
              ? 'border-indigo-600 text-indigo-600'
              : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          <ClipboardCheck className="w-4 h-4" />
          <span>Phiếu kiểm kê</span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('PERIODS')}
          className={`pb-2.5 flex items-center gap-2 border-b-2 transition-colors ${
            activeTab === 'PERIODS'
              ? 'border-indigo-600 text-indigo-600'
              : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          <Layers className="w-4 h-4" />
          <span>Kỳ kiểm kê theo lịch</span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('SCHEDULES')}
          className={`pb-2.5 flex items-center gap-2 border-b-2 transition-colors ${
            activeTab === 'SCHEDULES'
              ? 'border-indigo-600 text-indigo-600'
              : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          <Calendar className="w-4 h-4" />
          <span>Cấu hình lịch định kỳ</span>
        </button>
      </div>

      {/* Global Filters */}
      <div className="bg-white p-3.5 rounded-xl border border-gray-200 shadow-xs flex flex-wrap gap-3 items-center text-xs">
        <div className="flex items-center gap-1.5 font-semibold text-gray-600">
          <Building2 className="w-4 h-4 text-gray-400" />
          <span>Chi nhánh:</span>
        </div>
        <select
          value={selectedBranch}
          onChange={(e) => {
            setSelectedBranch(e.target.value);
            setSelectedWarehouse('');
            setPageNumber(1);
          }}
          className="border border-gray-300 rounded-lg px-2.5 py-1.5 bg-white text-xs outline-none"
        >
          <option value="">Tất cả chi nhánh</option>
          {branches.map((b) => (
            <option key={b.id} value={b.id}>
              {b.name}
            </option>
          ))}
        </select>

        <div className="flex items-center gap-1.5 font-semibold text-gray-600 ml-2">
          <Boxes className="w-4 h-4 text-gray-400" />
          <span>Kho:</span>
        </div>
        <select
          value={selectedWarehouse}
          onChange={(e) => {
            setSelectedWarehouse(e.target.value);
            setPageNumber(1);
          }}
          className="border border-gray-300 rounded-lg px-2.5 py-1.5 bg-white text-xs outline-none"
        >
          <option value="">Tất cả kho hàng</option>
          {filteredWarehouses.map((w) => (
            <option key={w.id} value={w.id}>
              {w.name}
            </option>
          ))}
        </select>

        {activeTab === 'COUNTS' && (
          <>
            <div className="flex items-center gap-1.5 font-semibold text-gray-600 ml-2">
              <span>Trạng thái:</span>
            </div>
            <select
              value={selectedStatus}
              onChange={(e) => {
                setSelectedStatus(e.target.value as StockCountStatus | '');
                setPageNumber(1);
              }}
              className="border border-gray-300 rounded-lg px-2.5 py-1.5 bg-white text-xs outline-none"
            >
              <option value="">Tất cả trạng thái</option>
              <option value="Draft">Bản nháp</option>
              <option value="Counting">Đang kiểm đếm</option>
              <option value="PendingApproval">Chờ duyệt chênh lệch</option>
              <option value="Confirmed">Đã cân bằng (Hoàn tất)</option>
              <option value="Cancelled">Đã hủy</option>
            </select>
          </>
        )}
      </div>

      {/* Error state */}
      {error && activeTab === 'COUNTS' && (
        <div className="p-4 bg-rose-50 border border-rose-200 rounded-xl text-rose-800 text-xs flex items-start gap-2">
          <AlertCircle className="w-5 h-5 shrink-0 mt-0.5" />
          <div>
            <span className="font-bold">Lỗi tải dữ liệu: </span>
            <span>{error}</span>
          </div>
        </div>
      )}

      {/* TAB 1: COUNTS */}
      {activeTab === 'COUNTS' && (
        <>
          <StockCountsTable
            counts={counts}
            loading={loading}
            onSelectCount={(id) => setSelectedCountId(id)}
          />

          {/* Pagination */}
          <div className="flex items-center justify-between bg-white px-4 py-3 border border-gray-200 rounded-xl text-xs text-gray-600">
            <div>
              Tổng số: <span className="font-bold text-gray-900">{totalCount}</span> đợt kiểm kê
              {totalPages > 1 && ` (Trang ${pageNumber} / ${totalPages})`}
            </div>
            <div className="flex items-center gap-2">
              <button
                type="button"
                disabled={pageNumber <= 1 || loading}
                onClick={() => setPageNumber((p) => Math.max(1, p - 1))}
                className="p-1.5 border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-40"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>
              <button
                type="button"
                disabled={pageNumber >= totalPages || loading}
                onClick={() => setPageNumber((p) => Math.min(totalPages, p + 1))}
                className="p-1.5 border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-40"
              >
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
          </div>
        </>
      )}

      {/* TAB 2: PERIODS */}
      {activeTab === 'PERIODS' && (
        <div className="bg-white border border-gray-200 rounded-2xl overflow-hidden shadow-xs">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-gray-50 border-b border-gray-200 text-gray-600 uppercase text-[10px] font-bold">
                <tr>
                  <th className="py-3 px-4">Mã kỳ (PeriodKey)</th>
                  <th className="py-3 px-4">Loại kỳ</th>
                  <th className="py-3 px-4">Chi nhánh / Kho</th>
                  <th className="py-3 px-4">Lịch trình / Hạn chót</th>
                  <th className="py-3 px-4">Người phụ trách</th>
                  <th className="py-3 px-4 text-center">Trạng thái</th>
                  <th className="py-3 px-4 text-center">Thao tác</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 font-sans">
                {periodsLoading && (
                  <tr>
                    <td colSpan={7} className="py-12 text-center text-gray-400">
                      Đang tải danh sách kỳ kiểm kê...
                    </td>
                  </tr>
                )}
                {!periodsLoading && periods.length === 0 && (
                  <tr>
                    <td colSpan={7} className="py-12 text-center text-gray-400">
                      Chưa có kỳ kiểm kê định kỳ nào được tạo.
                    </td>
                  </tr>
                )}
                {!periodsLoading &&
                  periods.map((p) => (
                    <tr key={p.id} className="hover:bg-gray-50/80">
                      <td className="py-3 px-4 font-mono font-bold text-indigo-600">
                        {p.periodKey}
                      </td>
                      <td className="py-3 px-4">
                        <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-indigo-50 text-indigo-700">
                          {p.periodType === 'MidMonth' ? 'Giữa tháng' : 'Cuối tháng'}
                        </span>
                      </td>
                      <td className="py-3 px-4">
                        <div className="font-semibold text-gray-900">{p.warehouseName}</div>
                        <div className="text-[11px] text-gray-500">{p.branchName}</div>
                      </td>
                      <td className="py-3 px-4 text-gray-600">
                        <div>Dự kiến: {new Date(p.scheduledAt).toLocaleDateString('vi-VN')}</div>
                        <div className="text-[11px] text-rose-600 font-medium">
                          Hạn chốt: {new Date(p.dueAt).toLocaleDateString('vi-VN')}
                        </div>
                      </td>
                      <td className="py-3 px-4 text-gray-700 font-medium">
                        {p.assigneeName || 'Chưa chỉ định'}
                      </td>
                      <td className="py-3 px-4 text-center">
                        <span
                          className={`inline-block px-2 py-0.5 rounded-full text-[10px] font-bold ${
                            p.status === 'Completed'
                              ? 'bg-emerald-100 text-emerald-800'
                              : p.status === 'InProgress'
                              ? 'bg-blue-100 text-blue-800'
                              : 'bg-amber-100 text-amber-800'
                          }`}
                        >
                          {p.status === 'Planned'
                            ? 'Lên kế hoạch'
                            : p.status === 'InProgress'
                            ? 'Đang kiểm đếm'
                            : p.status === 'Completed'
                            ? 'Đã hoàn tất'
                            : p.status}
                        </span>
                        {p.isOverdue && (
                          <span className="ml-1 inline-block px-1.5 py-0.5 bg-rose-100 text-rose-700 rounded text-[9px] font-bold">
                            Quá hạn
                          </span>
                        )}
                      </td>
                      <td className="py-3 px-4 text-center">
                        {p.status === 'Planned' ? (
                          <button
                            type="button"
                            onClick={() => handleStartPeriod(p.id)}
                            disabled={startingPeriodId === p.id}
                            className="inline-flex items-center gap-1 px-3 py-1 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg text-xs font-semibold shadow-xs"
                          >
                            <Play className="w-3.5 h-3.5" />
                            <span>Bắt đầu đếm</span>
                          </button>
                        ) : p.stockCountId ? (
                          <button
                            type="button"
                            onClick={() => setSelectedCountId(p.stockCountId!)}
                            className="text-xs font-semibold text-indigo-600 hover:underline"
                          >
                            Xem phiếu ({p.stockCountNumber || 'Chi tiết'})
                          </button>
                        ) : (
                          <span className="text-gray-400">—</span>
                        )}
                      </td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* TAB 3: SCHEDULES */}
      {activeTab === 'SCHEDULES' && (
        <div className="bg-white border border-gray-200 rounded-2xl overflow-hidden shadow-xs">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-gray-50 border-b border-gray-200 text-gray-600 uppercase text-[10px] font-bold">
                <tr>
                  <th className="py-3 px-4">Loại kỳ</th>
                  <th className="py-3 px-4">Chi nhánh / Kho</th>
                  <th className="py-3 px-4">Ngày / Giờ cấu hình</th>
                  <th className="py-3 px-4">Thời hạn hoàn tất</th>
                  <th className="py-3 px-4">Người phụ trách</th>
                  <th className="py-3 px-4 text-center">Trạng thái</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 font-sans">
                {schedulesLoading && (
                  <tr>
                    <td colSpan={6} className="py-12 text-center text-gray-400">
                      Đang tải cấu hình lịch kiểm kê...
                    </td>
                  </tr>
                )}
                {!schedulesLoading && schedules.length === 0 && (
                  <tr>
                    <td colSpan={6} className="py-12 text-center text-gray-400">
                      Chưa có lịch kiểm kê định kỳ nào. Bấm &quot;Thêm lịch định kỳ&quot; phía trên để cấu hình.
                    </td>
                  </tr>
                )}
                {!schedulesLoading &&
                  schedules.map((s) => (
                    <tr key={s.id} className="hover:bg-gray-50/80">
                      <td className="py-3 px-4 font-bold text-gray-900">
                        {s.periodType === 'MidMonth' ? 'Giữa tháng (MidMonth)' : 'Cuối tháng (EndOfMonth)'}
                      </td>
                      <td className="py-3 px-4">
                        <div className="font-semibold text-gray-900">{s.warehouseName}</div>
                        <div className="text-[11px] text-gray-500">{s.branchName}</div>
                      </td>
                      <td className="py-3 px-4 text-gray-600">
                        <div>
                          {s.periodType === 'MidMonth'
                            ? `Ngày ${s.dayOfMonth || 15} hàng tháng`
                            : 'Ngày cuối tháng'}
                        </div>
                        <div className="text-[11px] text-gray-400">{s.localTime} (UTC+7)</div>
                      </td>
                      <td className="py-3 px-4 text-gray-700">
                        {s.dueDays} ngày kể từ ngày chốt
                      </td>
                      <td className="py-3 px-4 text-gray-700 font-medium">
                        {s.assigneeName || 'Chưa chỉ định'}
                      </td>
                      <td className="py-3 px-4 text-center">
                        <span
                          className={`inline-block px-2 py-0.5 rounded-full text-[10px] font-bold ${
                            s.isActive
                              ? 'bg-emerald-100 text-emerald-800'
                              : 'bg-gray-100 text-gray-500'
                          }`}
                        >
                          {s.isActive ? 'Đang hiệu lực' : 'Tạm dừng'}
                        </span>
                      </td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Modals */}
      <CreateStockCountModal
        isOpen={isCreateModalOpen}
        onClose={() => setIsCreateModalOpen(false)}
        onSuccess={() => {
          loadCounts();
        }}
      />

      <CreateScheduleModal
        isOpen={isCreateScheduleOpen}
        onClose={() => setIsCreateScheduleOpen(false)}
        onSuccess={() => {
          loadSchedules();
        }}
        branches={branches}
        warehouses={warehouses}
      />

      {/* Drawer */}
      <StockCountDetailDrawer
        countId={selectedCountId}
        onClose={() => setSelectedCountId(null)}
        onRefresh={() => {
          loadCounts();
          if (activeTab === 'PERIODS') loadPeriods();
        }}
      />
    </div>
  );
};
