import React, { useState, useEffect, useCallback } from 'react';
import { useAuth } from '../context/AuthContext';
import { Navigate } from 'react-router-dom';
import { reportService } from '../services/reportService';
import { apiAdapter } from '../services/apiAdapter';
import type {
  RevenueProfitReport,
  InventoryVelocityReport,
  StockAlert,
  ReportFilter,
} from '../types/reports';
import {
  TrendingUp,
  Package,
  AlertTriangle,
  Calendar,
  Filter,
  Printer,
  RefreshCw,
  DollarSign,
  ArrowUpRight,
  Clock,
  Layers,
  Store,
  CheckCircle2,
  XCircle,
} from 'lucide-react';

export const ReportsPage: React.FC = () => {
  const { user } = useAuth();

  // Strict RBAC: Bắt buộc Owner mới được truy cập trang báo cáo quản trị tài chính
  if (user && user.role !== 'Owner') {
    return <Navigate to="/403" replace />;
  }

  // Active tab
  const [activeTab, setActiveTab] = useState<'pnl' | 'velocity' | 'alerts'>('pnl');

  // Master data for filters
  const [branches, setBranches] = useState<Array<{ id: string; name: string }>>([]);
  const [selectedBranch, setSelectedBranch] = useState('');
  const [selectedChannel, setSelectedChannel] = useState('');
  const [datePreset, setDatePreset] = useState<'today' | '7days' | '30days' | 'month' | 'custom'>('30days');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');

  // Data states
  const [pnlData, setPnlData] = useState<RevenueProfitReport | null>(null);
  const [velocityData, setVelocityData] = useState<InventoryVelocityReport | null>(null);
  const [alertsData, setAlertsData] = useState<StockAlert[]>([]);
  const [selectedAlertLevel, setSelectedAlertLevel] = useState<string>('ALL');

  const [loading, setLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Tính ngày theo preset
  const calculateDates = useCallback(() => {
    const now = new Date();
    const end = now.toISOString().split('T')[0];
    let start = '';

    if (datePreset === 'today') {
      start = end;
    } else if (datePreset === '7days') {
      const d = new Date();
      d.setDate(d.getDate() - 7);
      start = d.toISOString().split('T')[0];
    } else if (datePreset === '30days') {
      const d = new Date();
      d.setDate(d.getDate() - 30);
      start = d.toISOString().split('T')[0];
    } else if (datePreset === 'month') {
      const d = new Date(now.getFullYear(), now.getMonth(), 1);
      start = d.toISOString().split('T')[0];
    }

    if (datePreset !== 'custom') {
      setStartDate(start);
      setEndDate(end);
    }
  }, [datePreset]);

  useEffect(() => {
    calculateDates();
  }, [calculateDates]);

  useEffect(() => {
    const loadBranches = async () => {
      try {
        const brRes = await apiAdapter.getBranches();
        if (brRes.data) setBranches(brRes.data);
      } catch (err) {
        console.warn('Lỗi nạp danh mục chi nhánh:', err);
      }
    };
    loadBranches();
  }, []);

  // Fetch report data
  const loadReports = useCallback(async () => {
    setLoading(true);
    setErrorMessage(null);

    const filter: ReportFilter = {
      branchId: selectedBranch || undefined,
      channel: selectedChannel || undefined,
      startDate: startDate ? `${startDate}T00:00:00Z` : undefined,
      endDate: endDate ? `${endDate}T23:59:59Z` : undefined,
      topN: 10,
    };

    try {
      if (activeTab === 'pnl') {
        const data = await reportService.getRevenueProfit(filter);
        setPnlData(data);
      } else if (activeTab === 'velocity') {
        const data = await reportService.getInventoryVelocity(filter);
        setVelocityData(data);
      } else if (activeTab === 'alerts') {
        const data = await reportService.getStockAlerts(selectedBranch || undefined);
        setAlertsData(data);
      }
    } catch (err: any) {
      setErrorMessage(err?.detail || err?.message || 'Không thể tải báo cáo từ máy chủ.');
    } finally {
      setLoading(false);
    }
  }, [activeTab, selectedBranch, selectedChannel, startDate, endDate]);

  useEffect(() => {
    loadReports();
  }, [loadReports]);

  // Alert level styling helper
  const getAlertBadge = (level: string) => {
    switch (level) {
      case 'OutOfStock':
        return (
          <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-bold bg-rose-100 text-rose-800 border border-rose-300">
            <XCircle className="w-3.5 h-3.5 mr-1 text-rose-600" />
            Hết hàng (0)
          </span>
        );
      case 'Critical':
        return (
          <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-bold bg-amber-100 text-amber-800 border border-amber-300">
            <AlertTriangle className="w-3.5 h-3.5 mr-1 text-amber-600" />
            Nguy cấp (&lt;50% min)
          </span>
        );
      case 'Warning':
      default:
        return (
          <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-yellow-100 text-yellow-800 border border-yellow-300">
            <AlertTriangle className="w-3.5 h-3.5 mr-1 text-yellow-600" />
            Chạm ngưỡng cảnh báo
          </span>
        );
    }
  };

  const filteredAlerts = alertsData.filter((item) => {
    if (selectedAlertLevel === 'ALL') return true;
    return item.alertLevel === selectedAlertLevel;
  });

  return (
    <div className="p-6 max-w-7xl mx-auto space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-3">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center gap-2">
            <TrendingUp className="w-7 h-7 text-indigo-600" />
            Báo Cáo Tài Chính Quản Trị & Tồn Kho
          </h1>
          <p className="text-xs text-gray-500 mt-1">
            Số liệu Doanh thu thực tế, Giá vốn hàng bán (COGS snapshot), Lợi nhuận gộp và Cảnh báo an toàn cho Owner.
          </p>
        </div>

        <div className="flex gap-2">
          <button
            onClick={loadReports}
            className="px-3 py-2 text-xs font-semibold text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 shadow-sm flex items-center gap-1.5"
          >
            <RefreshCw className="w-3.5 h-3.5" />
            Tải lại
          </button>
          <button
            onClick={() => window.print()}
            className="px-4 py-2 text-xs font-semibold text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg shadow-sm flex items-center gap-1.5"
          >
            <Printer className="w-3.5 h-3.5" />
            In báo cáo
          </button>
        </div>
      </div>

      {/* Tabs navigation */}
      <div className="flex border-b border-gray-200 gap-1 bg-gray-50/50 p-1 rounded-t-lg">
        <button
          onClick={() => setActiveTab('pnl')}
          className={`px-4 py-2 text-xs font-bold rounded-lg transition-all flex items-center gap-1.5 ${
            activeTab === 'pnl'
              ? 'bg-white text-indigo-700 shadow-sm border border-gray-200'
              : 'text-gray-600 hover:text-gray-900 hover:bg-gray-100'
          }`}
        >
          <DollarSign className="w-4 h-4" />
          1. Doanh thu & Lợi nhuận (P&L)
        </button>

        <button
          onClick={() => setActiveTab('velocity')}
          className={`px-4 py-2 text-xs font-bold rounded-lg transition-all flex items-center gap-1.5 ${
            activeTab === 'velocity'
              ? 'bg-white text-indigo-700 shadow-sm border border-gray-200'
              : 'text-gray-600 hover:text-gray-900 hover:bg-gray-100'
          }`}
        >
          <Package className="w-4 h-4" />
          2. Vận tốc kho & Luân chuyển
        </button>

        <button
          onClick={() => setActiveTab('alerts')}
          className={`px-4 py-2 text-xs font-bold rounded-lg transition-all flex items-center gap-1.5 ${
            activeTab === 'alerts'
              ? 'bg-white text-indigo-700 shadow-sm border border-gray-200'
              : 'text-gray-600 hover:text-gray-900 hover:bg-gray-100'
          }`}
        >
          <AlertTriangle className="w-4 h-4 text-amber-500" />
          3. Cảnh báo tồn kho tối thiểu
        </button>
      </div>

      {/* Global Filter Bar */}
      <div className="bg-white p-4 rounded-xl border border-gray-200 shadow-sm flex flex-wrap items-center gap-3 text-xs">
        <div className="flex items-center gap-1.5 font-semibold text-gray-700 uppercase">
          <Filter className="w-4 h-4 text-gray-400" />
          <span>Bộ lọc:</span>
        </div>

        {/* Date presets (áp dụng cho P&L và Velocity) */}
        {activeTab !== 'alerts' && (
          <>
            <select
              value={datePreset}
              onChange={(e) => setDatePreset(e.target.value as any)}
              className="border border-gray-300 rounded-lg px-2.5 py-1.5 bg-white focus:ring-1 focus:ring-indigo-500 outline-none"
            >
              <option value="today">Hôm nay</option>
              <option value="7days">7 ngày qua</option>
              <option value="30days">30 ngày qua</option>
              <option value="month">Tháng này</option>
              <option value="custom">Tùy chỉnh khoảng ngày</option>
            </select>

            {datePreset === 'custom' && (
              <div className="flex items-center gap-1.5">
                <input
                  type="date"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                  className="border border-gray-300 rounded-lg px-2 py-1 text-xs outline-none"
                />
                <span className="text-gray-400">➔</span>
                <input
                  type="date"
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                  className="border border-gray-300 rounded-lg px-2 py-1 text-xs outline-none"
                />
              </div>
            )}
          </>
        )}

        {/* Chi nhánh */}
        <select
          value={selectedBranch}
          onChange={(e) => setSelectedBranch(e.target.value)}
          className="border border-gray-300 rounded-lg px-2.5 py-1.5 bg-white focus:ring-1 focus:ring-indigo-500 outline-none"
        >
          <option value="">Tất cả chi nhánh</option>
          {branches.map((b) => (
            <option key={b.id} value={b.id}>
              {b.name}
            </option>
          ))}
        </select>

        {/* Kênh bán hàng (chỉ cho P&L) */}
        {activeTab === 'pnl' && (
          <select
            value={selectedChannel}
            onChange={(e) => setSelectedChannel(e.target.value)}
            className="border border-gray-300 rounded-lg px-2.5 py-1.5 bg-white focus:ring-1 focus:ring-indigo-500 outline-none"
          >
            <option value="">Tất cả kênh bán</option>
            <option value="POS">Điểm bán lẻ (POS)</option>
            <option value="Shopee">Shopee</option>
            <option value="TikTok">TikTok Shop</option>
            <option value="Lazada">Lazada</option>
            <option value="Web">Website Online</option>
          </select>
        )}

        {/* Mức cảnh báo (chỉ cho Alerts) */}
        {activeTab === 'alerts' && (
          <select
            value={selectedAlertLevel}
            onChange={(e) => setSelectedAlertLevel(e.target.value)}
            className="border border-gray-300 rounded-lg px-2.5 py-1.5 bg-white focus:ring-1 focus:ring-indigo-500 outline-none"
          >
            <option value="ALL">Tất cả mức độ cảnh báo</option>
            <option value="OutOfStock">Chỉ xem Hết hàng (0)</option>
            <option value="Critical">Chỉ xem Nguy cấp (&lt;50% min)</option>
            <option value="Warning">Chỉ xem Chạm ngưỡng cảnh báo</option>
          </select>
        )}
      </div>

      {/* Error Banner */}
      {errorMessage && (
        <div className="p-4 bg-rose-50 border border-rose-200 rounded-xl flex items-start gap-2.5 text-rose-800 text-xs">
          <AlertTriangle className="w-5 h-5 flex-shrink-0 text-rose-600 mt-0.5" />
          <div>
            <div className="font-bold text-sm">Lỗi nạp báo cáo</div>
            <p className="mt-0.5">{errorMessage}</p>
          </div>
        </div>
      )}

      {/* Content Rendering */}
      {loading ? (
        <div className="bg-white rounded-xl border border-gray-200 p-16 text-center text-gray-500 shadow-sm">
          <div className="inline-block animate-spin rounded-full h-8 w-8 border-4 border-indigo-500 border-t-transparent mb-2"></div>
          <p className="text-sm">Đang tổng hợp số liệu báo cáo...</p>
        </div>
      ) : activeTab === 'pnl' ? (
        /* TAB 1: DOANH THU & LỢI NHUẬN */
        <div className="space-y-6">
          {/* 4 Stat Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            {/* Net Revenue */}
            <div className="bg-white p-5 rounded-xl border border-gray-200 shadow-sm">
              <div className="text-xs font-semibold text-gray-500 uppercase">Doanh thu thuần (Net)</div>
              <div className="text-2xl font-bold text-gray-900 mt-2">
                {(pnlData?.totalNetRevenue ?? 0).toLocaleString('vi-VN')} đ
              </div>
              <div className="text-xs text-gray-500 mt-1">
                {pnlData?.totalOrders ?? 0} đơn hàng · {pnlData?.totalItemsSold ?? 0} sản phẩm
              </div>
            </div>

            {/* COGS */}
            <div className="bg-white p-5 rounded-xl border border-gray-200 shadow-sm">
              <div className="text-xs font-semibold text-gray-500 uppercase">Tổng giá vốn (COGS)</div>
              <div className="text-2xl font-bold text-gray-900 mt-2">
                {(pnlData?.totalCostOfGoodsSold ?? 0).toLocaleString('vi-VN')} đ
              </div>
              <div className="text-xs text-gray-500 mt-1">Snapshot WAC tại thời điểm xuất bán</div>
            </div>

            {/* Gross Profit */}
            <div className="bg-white p-5 rounded-xl border border-gray-200 shadow-sm">
              <div className="text-xs font-semibold text-gray-500 uppercase">Lợi nhuận gộp</div>
              <div className="text-2xl font-bold text-emerald-600 mt-2">
                {(pnlData?.totalGrossProfit ?? 0).toLocaleString('vi-VN')} đ
              </div>
              <div className="text-xs text-emerald-700 font-medium mt-1 flex items-center gap-1">
                <ArrowUpRight className="w-3.5 h-3.5" />
                Revenue − COGS
              </div>
            </div>

            {/* Gross Margin % */}
            <div className="bg-white p-5 rounded-xl border border-gray-200 shadow-sm">
              <div className="text-xs font-semibold text-gray-500 uppercase">Tỷ suất lãi gộp</div>
              <div className="text-2xl font-bold text-indigo-600 mt-2">
                {(pnlData?.grossMarginPercentage ?? 0).toFixed(1)}%
              </div>
              <div className="text-xs text-gray-500 mt-1">Gross Profit / Revenue</div>
            </div>
          </div>

          {/* Breakdown Tables */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            {/* By Channel */}
            <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
              <div className="bg-gray-50 px-4 py-3 border-b border-gray-200 font-semibold text-xs text-gray-700 uppercase flex items-center gap-2">
                <Store className="w-4 h-4 text-indigo-600" />
                Phân bổ theo Kênh bán hàng
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-xs text-left border-collapse">
                  <thead>
                    <tr className="bg-gray-50/70 border-b border-gray-200 text-gray-500 font-semibold">
                      <th className="py-2.5 px-3">Kênh</th>
                      <th className="py-2.5 px-3 text-right">Doanh thu</th>
                      <th className="py-2.5 px-3 text-right">Giá vốn</th>
                      <th className="py-2.5 px-3 text-right">Lãi gộp</th>
                      <th className="py-2.5 px-3 text-right">Biên lãi</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {(pnlData?.byChannel || []).length === 0 ? (
                      <tr>
                        <td colSpan={5} className="py-6 text-center text-gray-400">
                          Chưa có đơn hàng trong kỳ
                        </td>
                      </tr>
                    ) : (
                      pnlData?.byChannel.map((ch) => (
                        <tr key={ch.channel} className="hover:bg-gray-50">
                          <td className="py-2.5 px-3 font-semibold text-gray-900">{ch.channel}</td>
                          <td className="py-2.5 px-3 text-right font-medium">
                            {ch.revenue.toLocaleString('vi-VN')} đ
                          </td>
                          <td className="py-2.5 px-3 text-right text-gray-600">
                            {ch.costOfGoodsSold.toLocaleString('vi-VN')} đ
                          </td>
                          <td className="py-2.5 px-3 text-right font-bold text-emerald-600">
                            {ch.grossProfit.toLocaleString('vi-VN')} đ
                          </td>
                          <td className="py-2.5 px-3 text-right font-semibold text-indigo-600">
                            {ch.grossMarginPercentage.toFixed(1)}%
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </div>

            {/* By Branch */}
            <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
              <div className="bg-gray-50 px-4 py-3 border-b border-gray-200 font-semibold text-xs text-gray-700 uppercase flex items-center gap-2">
                <Layers className="w-4 h-4 text-indigo-600" />
                Phân bổ theo Chi nhánh
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-xs text-left border-collapse">
                  <thead>
                    <tr className="bg-gray-50/70 border-b border-gray-200 text-gray-500 font-semibold">
                      <th className="py-2.5 px-3">Chi nhánh</th>
                      <th className="py-2.5 px-3 text-right">Doanh thu</th>
                      <th className="py-2.5 px-3 text-right">Giá vốn</th>
                      <th className="py-2.5 px-3 text-right">Lãi gộp</th>
                      <th className="py-2.5 px-3 text-right">Số đơn</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {(pnlData?.byBranch || []).length === 0 ? (
                      <tr>
                        <td colSpan={5} className="py-6 text-center text-gray-400">
                          Chưa có đơn hàng trong kỳ
                        </td>
                      </tr>
                    ) : (
                      pnlData?.byBranch.map((br) => (
                        <tr key={br.branchId} className="hover:bg-gray-50">
                          <td className="py-2.5 px-3 font-semibold text-gray-900">{br.branchName}</td>
                          <td className="py-2.5 px-3 text-right font-medium">
                            {br.revenue.toLocaleString('vi-VN')} đ
                          </td>
                          <td className="py-2.5 px-3 text-right text-gray-600">
                            {br.costOfGoodsSold.toLocaleString('vi-VN')} đ
                          </td>
                          <td className="py-2.5 px-3 text-right font-bold text-emerald-600">
                            {br.grossProfit.toLocaleString('vi-VN')} đ
                          </td>
                          <td className="py-2.5 px-3 text-right text-gray-600">{br.orderCount}</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          </div>

          {/* Daily Trend Table */}
          <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
            <div className="bg-gray-50 px-4 py-3 border-b border-gray-200 font-semibold text-xs text-gray-700 uppercase flex items-center gap-2">
              <Calendar className="w-4 h-4 text-indigo-600" />
              Biến động doanh thu & lãi gộp theo ngày
            </div>
            <div className="overflow-x-auto max-h-72">
              <table className="w-full text-xs text-left border-collapse">
                <thead>
                  <tr className="bg-gray-50/70 border-b border-gray-200 text-gray-500 font-semibold">
                    <th className="py-2.5 px-4">Ngày</th>
                    <th className="py-2.5 px-4 text-right">Doanh thu</th>
                    <th className="py-2.5 px-4 text-right">Giá vốn (COGS)</th>
                    <th className="py-2.5 px-4 text-right">Lợi nhuận gộp</th>
                    <th className="py-2.5 px-4 text-right">Số đơn hàng</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {(pnlData?.dailyTrend || []).length === 0 ? (
                    <tr>
                      <td colSpan={5} className="py-6 text-center text-gray-400">
                        Không có dữ liệu theo ngày
                      </td>
                    </tr>
                  ) : (
                    pnlData?.dailyTrend.map((d) => (
                      <tr key={d.date} className="hover:bg-gray-50">
                        <td className="py-2.5 px-4 font-mono font-medium text-gray-800">{d.date}</td>
                        <td className="py-2.5 px-4 text-right font-medium">
                          {d.revenue.toLocaleString('vi-VN')} đ
                        </td>
                        <td className="py-2.5 px-4 text-right text-gray-600">
                          {d.costOfGoodsSold.toLocaleString('vi-VN')} đ
                        </td>
                        <td className="py-2.5 px-4 text-right font-bold text-emerald-600">
                          {d.grossProfit.toLocaleString('vi-VN')} đ
                        </td>
                        <td className="py-2.5 px-4 text-right text-gray-700">{d.orderCount}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      ) : activeTab === 'velocity' ? (
        /* TAB 2: VẬN TỐC TỒN KHO & LUÂN CHUYỂN */
        <div className="space-y-6">
          {/* Stat Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <div className="bg-white p-5 rounded-xl border border-gray-200 shadow-sm">
              <div className="text-xs font-semibold text-gray-500 uppercase">Tổng giá trị tồn (WAC)</div>
              <div className="text-2xl font-bold text-indigo-700 mt-2">
                {(velocityData?.totalInventoryValue ?? 0).toLocaleString('vi-VN')} đ
              </div>
              <div className="text-xs text-gray-500 mt-1">Định giá theo giá vốn bình quân</div>
            </div>

            <div className="bg-white p-5 rounded-xl border border-gray-200 shadow-sm">
              <div className="text-xs font-semibold text-gray-500 uppercase">Tồn thực tế (On-Hand)</div>
              <div className="text-2xl font-bold text-gray-900 mt-2">
                {(velocityData?.totalOnHandQuantity ?? 0).toLocaleString('vi-VN')}
              </div>
              <div className="text-xs text-gray-500 mt-1">Tổng sản phẩm hiện có trong kho</div>
            </div>

            <div className="bg-white p-5 rounded-xl border border-gray-200 shadow-sm">
              <div className="text-xs font-semibold text-gray-500 uppercase">Hàng giữ chỗ (Reserved)</div>
              <div className="text-2xl font-bold text-amber-600 mt-2">
                {(velocityData?.totalReservedQuantity ?? 0).toLocaleString('vi-VN')}
              </div>
              <div className="text-xs text-gray-500 mt-1">Khóa cho đơn đang xử lý</div>
            </div>

            <div className="bg-white p-5 rounded-xl border border-gray-200 shadow-sm">
              <div className="text-xs font-semibold text-gray-500 uppercase">Tồn khả dụng (Available)</div>
              <div className="text-2xl font-bold text-emerald-600 mt-2">
                {(velocityData?.totalAvailableQuantity ?? 0).toLocaleString('vi-VN')}
              </div>
              <div className="text-xs text-gray-500 mt-1">On-Hand − Reserved</div>
            </div>
          </div>

          {/* Best Sellers & Slow Moving */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            {/* Top Best-Sellers */}
            <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
              <div className="bg-emerald-50/60 px-4 py-3 border-b border-gray-200 font-semibold text-xs text-emerald-900 uppercase flex items-center gap-2">
                <ArrowUpRight className="w-4 h-4 text-emerald-600" />
                Top Mặt Hàng Bán Chạy Nhất (Best-sellers)
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-xs text-left border-collapse">
                  <thead>
                    <tr className="bg-gray-50 border-b border-gray-200 text-gray-500 font-semibold">
                      <th className="py-2.5 px-3">Mã SKU / Tên SP</th>
                      <th className="py-2.5 px-3 text-right">SL Bán</th>
                      <th className="py-2.5 px-3 text-right">Doanh thu</th>
                      <th className="py-2.5 px-3 text-right">Tồn hiện tại</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {(velocityData?.bestSellers || []).length === 0 ? (
                      <tr>
                        <td colSpan={4} className="py-6 text-center text-gray-400">
                          Chưa có dữ liệu bán chạy
                        </td>
                      </tr>
                    ) : (
                      velocityData?.bestSellers.map((sku) => (
                        <tr key={sku.skuId} className="hover:bg-gray-50">
                          <td className="py-2.5 px-3">
                            <div className="font-semibold text-indigo-700">{sku.skuCode}</div>
                            <div className="text-gray-500 text-[11px] truncate max-w-xs">{sku.skuName}</div>
                          </td>
                          <td className="py-2.5 px-3 text-right font-bold text-emerald-700">
                            {sku.quantitySold}
                          </td>
                          <td className="py-2.5 px-3 text-right font-medium">
                            {sku.totalRevenue.toLocaleString('vi-VN')} đ
                          </td>
                          <td className="py-2.5 px-3 text-right text-gray-700">{sku.currentOnHand}</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </div>

            {/* Slow-Moving */}
            <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
              <div className="bg-amber-50/60 px-4 py-3 border-b border-gray-200 font-semibold text-xs text-amber-900 uppercase flex items-center gap-2">
                <Clock className="w-4 h-4 text-amber-600" />
                Mặt Hàng Chậm Luân Chuyển (Slow-moving)
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-xs text-left border-collapse">
                  <thead>
                    <tr className="bg-gray-50 border-b border-gray-200 text-gray-500 font-semibold">
                      <th className="py-2.5 px-3">Mã SKU / Tên SP</th>
                      <th className="py-2.5 px-3 text-right">Ngày không bán</th>
                      <th className="py-2.5 px-3 text-right">Tồn hiện tại</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {(velocityData?.slowMovingItems || []).length === 0 ? (
                      <tr>
                        <td colSpan={3} className="py-6 text-center text-gray-400">
                          Không có sản phẩm tồn ứ lâu ngày
                        </td>
                      </tr>
                    ) : (
                      velocityData?.slowMovingItems.map((sku) => (
                        <tr key={sku.skuId} className="hover:bg-gray-50">
                          <td className="py-2.5 px-3">
                            <div className="font-semibold text-indigo-700">{sku.skuCode}</div>
                            <div className="text-gray-500 text-[11px] truncate max-w-xs">{sku.skuName}</div>
                          </td>
                          <td className="py-2.5 px-3 text-right font-bold text-amber-700">
                            {sku.daysWithoutSale} ngày
                          </td>
                          <td className="py-2.5 px-3 text-right text-gray-700">{sku.currentOnHand}</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        </div>
      ) : (
        /* TAB 3: CẢNH BÁO TỒN KHO TỐI THIỂU */
        <div className="space-y-6">
          {/* Summary counters */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
            <div className="bg-rose-50 border border-rose-200 p-4 rounded-xl flex items-center justify-between">
              <div>
                <span className="text-xs font-semibold text-rose-700 uppercase">Hết hàng sạch (OutOfStock)</span>
                <div className="text-2xl font-bold text-rose-900 mt-1">
                  {alertsData.filter((a) => a.alertLevel === 'OutOfStock').length} SKU
                </div>
              </div>
              <XCircle className="w-8 h-8 text-rose-500" />
            </div>

            <div className="bg-amber-50 border border-amber-200 p-4 rounded-xl flex items-center justify-between">
              <div>
                <span className="text-xs font-semibold text-amber-700 uppercase">Nguy cấp (&lt;50% min)</span>
                <div className="text-2xl font-bold text-amber-900 mt-1">
                  {alertsData.filter((a) => a.alertLevel === 'Critical').length} SKU
                </div>
              </div>
              <AlertTriangle className="w-8 h-8 text-amber-500" />
            </div>

            <div className="bg-yellow-50 border border-yellow-200 p-4 rounded-xl flex items-center justify-between">
              <div>
                <span className="text-xs font-semibold text-yellow-700 uppercase">Chạm ngưỡng (Warning)</span>
                <div className="text-2xl font-bold text-yellow-900 mt-1">
                  {alertsData.filter((a) => a.alertLevel === 'Warning').length} SKU
                </div>
              </div>
              <AlertTriangle className="w-8 h-8 text-yellow-500" />
            </div>
          </div>

          {/* Alerts Table */}
          <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
            <div className="bg-gray-50 px-4 py-3 border-b border-gray-200 font-semibold text-xs text-gray-700 uppercase flex items-center justify-between">
              <span>Danh sách cảnh báo tồn kho an toàn (ReorderPoint)</span>
              <span className="text-gray-500 font-normal">Hiển thị {filteredAlerts.length} mặt hàng</span>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-xs text-left border-collapse">
                <thead>
                  <tr className="bg-gray-50 border-b border-gray-200 text-gray-500 font-semibold">
                    <th className="py-2.5 px-4">Mã SKU / Tên SP</th>
                    <th className="py-2.5 px-4">Chi nhánh</th>
                    <th className="py-2.5 px-4 text-right">Tồn thực tế (On-hand)</th>
                    <th className="py-2.5 px-4 text-right">Giữ chỗ (Reserved)</th>
                    <th className="py-2.5 px-4 text-right">Khả dụng (Available)</th>
                    <th className="py-2.5 px-4 text-right">Điểm đặt hàng (Reorder)</th>
                    <th className="py-2.5 px-4 text-center">Mức độ cảnh báo</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {filteredAlerts.length === 0 ? (
                    <tr>
                      <td colSpan={7} className="py-8 text-center text-gray-400">
                        <CheckCircle2 className="w-8 h-8 mx-auto text-emerald-500 mb-2" />
                        Tất cả các mặt hàng đều đang ở mức tồn kho an toàn!
                      </td>
                    </tr>
                  ) : (
                    filteredAlerts.map((alert) => (
                      <tr key={`${alert.skuId}-${alert.branchId}`} className="hover:bg-gray-50">
                        <td className="py-3 px-4">
                          <div className="font-semibold text-indigo-700">{alert.skuCode}</div>
                          <div className="text-gray-500 text-[11px] truncate max-w-xs">{alert.skuName}</div>
                        </td>
                        <td className="py-3 px-4 text-gray-700 font-medium">{alert.branchName}</td>
                        <td className="py-3 px-4 text-right font-medium">{alert.onHand}</td>
                        <td className="py-3 px-4 text-right text-amber-700">{alert.reserved}</td>
                        <td className="py-3 px-4 text-right font-bold text-gray-900">{alert.available}</td>
                        <td className="py-3 px-4 text-right text-gray-600 font-mono">{alert.reorderPoint}</td>
                        <td className="py-3 px-4 text-center">{getAlertBadge(alert.alertLevel)}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default ReportsPage;
