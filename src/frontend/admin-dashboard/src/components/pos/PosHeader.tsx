import React from 'react';
import { Store, Wifi, WifiOff, LogIn, LogOut, Maximize2, Minimize2, LayoutDashboard } from 'lucide-react';
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

export const PosHeader: React.FC<PosHeaderProps> = ({ branches, selectedBranchId, onSelectBranch,
  currentUser, onOpenLogin, onLogout, isOnline, isFullscreen, onToggleFullscreen }) => (
  <header className="pos-retail-header no-print">
    <div className="pos-brand"><span className="pos-brand-icon"><Store size={22} /></span>
      <div><h1>OISMS POS</h1><small>{currentUser?.tenantName || 'Quầy bán hàng'}</small></div>
    </div>
    <select aria-label="Chi nhánh" value={selectedBranchId} onChange={(event) => onSelectBranch(event.target.value)}>
      {branches.length === 0 ? <option value="">Chưa có chi nhánh</option> : branches.map((branch) =>
        <option key={branch.id} value={branch.id}>{branch.name}</option>)}
    </select>
    <span className={`pos-connection ${isOnline ? '' : 'is-offline'}`} title={isOnline ? 'Đã kết nối' : 'Mất kết nối'}>
      {isOnline ? <Wifi size={13} /> : <WifiOff size={13} />}<span>{isOnline ? 'Trực tuyến' : 'Ngoại tuyến'}</span>
    </span>
    <div className="pos-header-actions">
      <button type="button" onClick={onToggleFullscreen} aria-label={isFullscreen ? 'Thu nhỏ cửa sổ' : 'Toàn màn hình'}>
        {isFullscreen ? <Minimize2 size={17} /> : <Maximize2 size={17} />}
      </button>
      {currentUser && currentUser.role !== 'Cashier' && <Link to="/admin" aria-label="Quản trị" title="Quay lại quản trị"><LayoutDashboard size={17} /></Link>}
      {currentUser ? <><span className="pos-avatar">{currentUser.fullName?.charAt(0).toUpperCase() || 'U'}</span>
        <div className="pos-user"><strong>{currentUser.fullName}</strong><small>{currentUser.role || 'Thu ngân'}</small></div>
        <button type="button" onClick={onLogout} aria-label="Đăng xuất"><LogOut size={17} /></button>
      </> : <button type="button" onClick={onOpenLogin}><LogIn size={17} />Đăng nhập</button>}
    </div>
  </header>
);
