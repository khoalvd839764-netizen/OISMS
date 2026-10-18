import { useState, useEffect } from 'react';
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import {
  Boxes,
  ChartNoAxesCombined,
  ChevronDown,
  CircleHelp,
  LayoutDashboard,
  LogOut,
  Menu,
  PackageSearch,
  Search,
  Settings2,
  ShoppingBag,
  Store,
  X,
} from 'lucide-react';
import { Button } from '../common/ui';
import { NotificationCenter } from './NotificationCenter';
import { NetworkBanner } from '../feedback/NetworkBanner';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../feedback/Toast';

interface NavItem {
  to: string;
  label: string;
  icon: React.ComponentType<{ size?: number }>;
  ownerOnly?: boolean;
  badge?: number | string;
}

const navItems: NavItem[] = [
  { to: '/', label: 'Tổng quan', icon: LayoutDashboard },
  { to: '/products', label: 'Sản phẩm & Biến thể', icon: PackageSearch },
  { to: '/inventory', label: 'Kho & Sổ kho', icon: Boxes, badge: 'Mới' },
  { to: '/orders', label: 'Đơn đa kênh', icon: ShoppingBag },
  { to: '/reports', label: 'Báo cáo doanh thu', icon: ChartNoAxesCombined, ownerOnly: true },
];

export function AdminShell() {
  const [open, setOpen] = useState(false);
  const location = useLocation();
  const navigate = useNavigate();
  const { user, role, hasRole, logout } = useAuth();
  const { showToast } = useToast();

  useEffect(() => {
    const handleForbidden = (e: Event) => {
      const detail = (e as CustomEvent<{ message?: string }>).detail;
      showToast('error', 'Không đủ quyền truy cập (403)', detail?.message || 'Tài khoản của bạn không được cấp quyền thực hiện thao tác này.');
      navigate('/403');
    };

    const handleSessionExpired = () => {
      showToast('warning', 'Phiên đăng nhập hết hạn', 'Vui lòng đăng nhập lại để tiếp tục.');
      navigate('/session-expired', { replace: true });
    };

    window.addEventListener('oism:forbidden', handleForbidden);
    window.addEventListener('oism:session-expired', handleSessionExpired);
    return () => {
      window.removeEventListener('oism:forbidden', handleForbidden);
      window.removeEventListener('oism:session-expired', handleSessionExpired);
    };
  }, [navigate, showToast]);

  const isOwner = hasRole(['Owner']);
  const visibleNav = navItems.filter((item) => !item.ownerOnly || isOwner);

  const secondaryTitles: Record<string, string> = {
    '/organization': 'Nhân viên & Chi nhánh',
    '/handoff': 'Tài liệu Handoff',
  };

  const currentTitle =
    navItems.find((item) => item.to === location.pathname)?.label ||
    secondaryTitles[location.pathname] ||
    'OISM';

  const tenantDisplayName = user?.tenantName || 'Doanh nghiệp OISM';
  const userDisplayName = user?.fullName || user?.email || 'Người dùng';
  const userInitials =
    user?.fullName
      ?.split(' ')
      .filter(Boolean)
      .slice(-2)
      .map((part) => part[0])
      .join('')
      .toUpperCase() || 'OI';
  const roleDisplay = role || 'Staff';

  const handleLogout = async () => {
    await logout();
    navigate('/login', { replace: true });
  };

  return (
    <div className="app-shell">
      <aside className={`sidebar ${open ? 'sidebar--open' : ''}`}>
        <div className="brand">
          <div className="brand-mark">O</div>
          <div>
            <strong>OISM</strong>
            <span>Omnichannel Retail</span>
          </div>
          <Button variant="ghost" className="mobile-close" onClick={() => setOpen(false)}>
            <X size={20} />
          </Button>
        </div>

        <div className="workspace">
          <span className="workspace-logo">
            <Store size={18} />
          </span>
          <div>
            <small>DOANH NGHIỆP ({user?.tenantCode || 'TENANT'})</small>
            <strong>{tenantDisplayName}</strong>
          </div>
          <ChevronDown size={16} />
        </div>

        <nav className="nav-list" aria-label="Điều hướng chính">
          <span className="nav-label">VẬN HÀNH</span>
          {visibleNav.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === '/'}
              onClick={() => setOpen(false)}
            >
              <item.icon size={19} />
              <span>{item.label}</span>
              {item.badge && <i>{item.badge}</i>}
            </NavLink>
          ))}

          {isOwner && (
            <>
              <span className="nav-label nav-label--spaced">HỆ THỐNG</span>
              <NavLink to="/organization" onClick={() => setOpen(false)}>
                <Settings2 size={19} />
                <span>Nhân viên & Chi nhánh</span>
              </NavLink>
            </>
          )}
        </nav>

        <div className="sidebar-bottom">
          <NavLink to="/handoff">
            <CircleHelp size={18} />
            <span>Hỗ trợ & Handoff</span>
          </NavLink>
        </div>
      </aside>

      <div className="main-area">
        <NetworkBanner />
        <header className="topbar">
          <div className="top-left">
            <Button
              variant="ghost"
              className="menu-button"
              onClick={() => setOpen(true)}
              aria-label="Mở menu điều hướng"
            >
              <Menu size={21} />
            </Button>
            <span className="crumb">{tenantDisplayName}</span>
            <span>/</span>
            <strong>{currentTitle}</strong>
          </div>

          <div className="top-actions">
            <div className="global-search" onClick={() => navigate('/products')} style={{ cursor: 'pointer' }}>
              <Search size={17} />
              <span>Tìm nhanh sản phẩm / SKU</span>
              <kbd>⌘ K</kbd>
            </div>

            <NotificationCenter />

            <div className="avatar" title={userDisplayName}>
              {userInitials}
            </div>

            <div className="user-meta">
              <strong>{userDisplayName}</strong>
              <span>{roleDisplay}</span>
            </div>

            <Button
              variant="ghost"
              aria-label="Đăng xuất"
              title="Đăng xuất khỏi hệ thống"
              onClick={handleLogout}
            >
              <LogOut size={18} />
            </Button>
          </div>
        </header>

        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
