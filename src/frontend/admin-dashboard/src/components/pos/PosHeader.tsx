import React from 'react';
import { Store, Wifi, WifiOff, LogIn, LogOut, Maximize2, Minimize2, Sparkles, LayoutDashboard } from 'lucide-react';
import { Link } from 'react-router-dom';
import type { Branch } from '../../types/pos';

interface PosHeaderProps {
  branches: Branch[];
  selectedBranchId: string;
  onSelectBranch: (branchId: string) => void;
  currentUser: { fullName: string; email: string; tenantName?: string; role?: string } | null;
  onOpenLogin: () => void;
  onLogout: () => void;
  isOnline: boolean;
  isFullscreen: boolean;
  onToggleFullscreen: () => void;
}

export const PosHeader: React.FC<PosHeaderProps> = ({
  branches,
  selectedBranchId,
  onSelectBranch,
  currentUser,
  onOpenLogin,
  onLogout,
  isOnline,
  isFullscreen,
  onToggleFullscreen,
}) => {
  return (
    <header className="no-print bg-slate-900 text-white px-4 py-3 flex flex-wrap items-center justify-between gap-4 border-b border-slate-800 shadow-md">
      {/* Brand & Store Name */}
      <div className="flex items-center gap-3">
        <div className="w-10 h-10 rounded-xl bg-gradient-to-tr from-indigo-600 to-violet-500 flex items-center justify-center shadow-lg shadow-indigo-500/30">
          <Store className="w-6 h-6 text-white" />
        </div>
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-lg font-bold tracking-tight text-white m-0">OISMS POS</h1>
            <span className="text-xs font-semibold px-2 py-0.5 rounded-full bg-indigo-500/20 text-indigo-300 border border-indigo-500/30 flex items-center gap-1">
              <Sparkles className="w-3 h-3" /> PWA React 19
            </span>
          </div>
          <p className="text-xs text-slate-400 m-0">
            {currentUser?.tenantName ? `Cửa hàng: ${currentUser.tenantName}` : 'Quầy Bán Hàng Trực Tiếp'}
          </p>
        </div>
      </div>

      {/* Branch Selector & Online Indicator */}
      <div className="flex items-center gap-3">
        <div className="flex items-center gap-1.5 bg-slate-800/80 px-3 py-1.5 rounded-lg border border-slate-700">
          <label htmlFor="branch-select" className="text-xs text-slate-400 font-medium whitespace-nowrap">
            Chi nhánh:
          </label>
          <select
            id="branch-select"
            value={selectedBranchId}
            onChange={(e) => onSelectBranch(e.target.value)}
            className="bg-transparent text-sm text-indigo-300 font-semibold focus:outline-none cursor-pointer"
          >
            {branches.length === 0 ? (
              <option value="" className="bg-slate-900 text-slate-300">
                Chi nhánh mặc định
              </option>
            ) : (
              branches.map((b) => (
                <option key={b.id} value={b.id} className="bg-slate-900 text-slate-200">
                  {b.name}
                </option>
              ))
            )}
          </select>
        </div>

        {/* Network Status */}
        <div
          className={`flex items-center gap-1 text-xs px-2.5 py-1.5 rounded-lg font-medium ${
            isOnline ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20' : 'bg-rose-500/10 text-rose-400 border border-rose-500/20'
          }`}
          title={isOnline ? 'Hệ thống trực tuyến' : 'Đang ngoại tuyến'}
        >
          {isOnline ? <Wifi className="w-3.5 h-3.5" /> : <WifiOff className="w-3.5 h-3.5" />}
          <span className="hidden sm:inline">{isOnline ? 'Online' : 'Offline'}</span>
        </div>

        {/* Fullscreen Toggle */}
        <button
          type="button"
          onClick={onToggleFullscreen}
          className="p-2 text-slate-400 hover:text-white bg-slate-800/80 hover:bg-slate-700 rounded-lg transition-colors border border-slate-700"
          title={isFullscreen ? 'Thu nhỏ cửa sổ' : 'Toàn màn hình (F11)'}
        >
          {isFullscreen ? <Minimize2 className="w-4 h-4" /> : <Maximize2 className="w-4 h-4" />}
        </button>

        {/* Nút quay lại Quản trị cho Owner / Staff */}
        {currentUser && currentUser.role !== 'Cashier' && (
          <Link
            to="/admin"
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold text-slate-200 bg-slate-800/90 hover:bg-slate-700 hover:text-white rounded-lg border border-slate-700 transition-colors"
            title="Quay lại Bảng điều khiển Quản trị"
          >
            <LayoutDashboard className="w-3.5 h-3.5 text-indigo-400" />
            <span className="hidden sm:inline">Quản trị</span>
          </Link>
        )}

        {/* User Account / Auth */}
        {currentUser ? (
          <div className="flex items-center gap-2 pl-2 border-l border-slate-800">
            <div className="w-8 h-8 rounded-full bg-slate-700 flex items-center justify-center text-slate-200 text-xs font-bold border border-slate-600">
              {currentUser.fullName ? currentUser.fullName.charAt(0).toUpperCase() : 'U'}
            </div>
            <div className="hidden md:block text-left">
              <div className="text-xs font-semibold text-slate-200 truncate max-w-[120px]">
                {currentUser.fullName}
              </div>
              <div className="text-[10px] text-slate-400">
                {currentUser.role || 'Thu ngân'}
              </div>
            </div>
            <button
              type="button"
              onClick={onLogout}
              className="p-1.5 text-slate-400 hover:text-rose-400 transition-colors"
              title="Đăng xuất"
            >
              <LogOut className="w-4 h-4" />
            </button>
          </div>
        ) : (
          <button
            type="button"
            onClick={onOpenLogin}
            className="flex items-center gap-1.5 bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold px-3 py-1.5 rounded-lg transition-all shadow-md shadow-indigo-600/30"
          >
            <LogIn className="w-3.5 h-3.5" />
            <span>Đăng nhập</span>
          </button>
        )}
      </div>
    </header>
  );
};
