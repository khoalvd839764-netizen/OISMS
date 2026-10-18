import { useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import {
  Boxes,
  ChartNoAxesCombined,
  ChevronDown,
  CircleHelp,
  ClipboardList,
  LayoutDashboard,
  LogOut,
  Menu,
  PackageSearch,
  RefreshCw,
  Search,
  Settings2,
  ShoppingBag,
  Store,
  WifiOff,
  X,
} from "lucide-react";
import { Button } from "./ui";
import { useAuth } from "../context/AuthContext";
import { NotificationCenter } from "./NotificationCenter";

const nav = [
  { to: "/", label: "Tổng quan", icon: LayoutDashboard },
  { to: "/products", label: "Sản phẩm", icon: PackageSearch },
  { to: "/inventory", label: "Kho & sổ kho", icon: Boxes },
  { to: "/orders", label: "Đơn đa kênh", icon: ShoppingBag },
  { to: "/reports", label: "Báo cáo", icon: ChartNoAxesCombined },
];

export function AdminShell() {
  const [open, setOpen] = useState(false);
  const location = useLocation();
  const navigate = useNavigate();
  const { user, logout, networkError, setNetworkError } = useAuth();

  const account = user || {
    id: "user",
    name: "Quản trị viên",
    email: "",
    initials: "QT",
    role: "Staff" as const,
    tenantId: "",
    tenantName: "Cửa hàng của tôi",
    tenantCode: "",
    branchName: "Chi nhánh chính",
    landingPath: "/",
  };

  const visibleNav = nav.filter(
    (item) => account.role === "Owner" || !["/reports"].includes(item.to)
  );

  const secondaryTitles: Record<string, string> = {
    "/demo-tools": "Công cụ demo",
    "/handoff": "Handoff",
    "/organization": "Nhân viên & chi nhánh",
  };

  const current =
    nav.find((item) => item.to === location.pathname)?.label ||
    secondaryTitles[location.pathname] ||
    "OISM";

  return (
    <div className="app-shell">
      <aside className={`sidebar ${open ? "sidebar--open" : ""}`}>
        <div className="brand">
          <div className="brand-mark">O</div>
          <div>
            <strong>OISM</strong>
            <span>Retail Operations</span>
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
            <small>DOANH NGHIỆP</small>
            <strong>{account.tenantName}</strong>
          </div>
          <ChevronDown size={16} />
        </div>

        <nav className="nav-list" aria-label="Điều hướng chính">
          <span className="nav-label">VẬN HÀNH</span>
          {visibleNav.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === "/"}
              onClick={() => setOpen(false)}
            >
              <item.icon size={19} />
              <span>{item.label}</span>
              {item.label === "Kho & sổ kho" && <i>3</i>}
            </NavLink>
          ))}
          <span className="nav-label nav-label--spaced">HỆ THỐNG</span>
          <NavLink to="/handoff" onClick={() => setOpen(false)}>
            <ClipboardList size={19} />
            <span>Handoff kỹ thuật</span>
          </NavLink>
        </nav>

        <div className="sidebar-bottom">
          <NavLink to="/handoff">
            <CircleHelp size={18} />Trợ giúp & giả định
          </NavLink>
          {account.role === "Owner" && (
            <NavLink to="/organization">
              <Settings2 size={18} />Nhân viên & chi nhánh
            </NavLink>
          )}
        </div>
      </aside>

      <div className="main-area">
        {networkError && (
          <div
            style={{
              background: "#dc2626",
              color: "#ffffff",
              padding: "10px 18px",
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              fontSize: "13px",
              fontWeight: 500,
              position: "sticky",
              top: 0,
              zIndex: 100,
              boxShadow: "0 2px 8px rgba(0,0,0,0.15)",
            }}
          >
            <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
              <WifiOff size={18} />
              <span>
                <strong>Lỗi mất kết nối mạng:</strong> {networkError}
              </span>
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
              <button
                type="button"
                onClick={() => window.location.reload()}
                style={{
                  background: "rgba(255,255,255,0.2)",
                  border: "1px solid rgba(255,255,255,0.4)",
                  color: "#fff",
                  padding: "4px 12px",
                  borderRadius: "6px",
                  cursor: "pointer",
                  display: "flex",
                  alignItems: "center",
                  gap: "6px",
                  fontSize: "12px",
                  fontWeight: 600,
                }}
              >
                <RefreshCw size={14} /> Thử lại
              </button>
              <button
                type="button"
                onClick={() => setNetworkError(null)}
                style={{
                  background: "none",
                  border: "none",
                  color: "#fff",
                  cursor: "pointer",
                  padding: "4px",
                }}
                title="Đóng cảnh báo"
              >
                <X size={16} />
              </button>
            </div>
          </div>
        )}

        <header className="topbar">
          <div className="top-left">
            <Button variant="ghost" className="menu-button" onClick={() => setOpen(true)}>
              <Menu size={21} />
            </Button>
            <span className="crumb">{account.tenantName}</span>
            <span>/</span>
            <strong>{current}</strong>
          </div>

          <div className="top-actions">
            <div className="global-search">
              <Search size={17} />
              <span>Tìm nhanh</span>
              <kbd>⌘ K</kbd>
            </div>
            <button className="branch-picker" type="button">
              <Store size={17} />
              <span>{account.branchName}</span>
              <ChevronDown size={15} />
            </button>
            <NotificationCenter />
            <div className="avatar">{account.initials}</div>
            <div className="user-meta">
              <strong>{account.name}</strong>
              <span>{account.role}</span>
            </div>
            <Button
              variant="ghost"
              aria-label="Đăng xuất"
              title="Đăng xuất"
              onClick={async () => {
                await logout();
                navigate("/login");
              }}
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
