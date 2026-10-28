import { useEffect, useState } from "react";
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
  ArrowLeftRight,
  ClipboardCheck,
} from "lucide-react";
import { Button } from "./ui";
import { useAuth } from "../context/AuthContext";
import { NotificationCenter } from "./NotificationCenter";

const nav = [
  { to: "/admin", label: "Tổng quan", icon: LayoutDashboard },
  { to: "/products", label: "Sản phẩm", icon: PackageSearch },
  { to: "/inventory", label: "Kho & sổ kho", icon: Boxes },
  { to: "/inventory/transfers", label: "Chuyển kho", icon: ArrowLeftRight },
  { to: "/inventory/counts", label: "Kiểm kê kho", icon: ClipboardCheck },
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
    landingPath: "/admin",
  };

  const visibleNav = nav.filter(
    (item) => account.role === "Owner" || !["/reports"].includes(item.to)
  );

  const secondaryTitles: Record<string, string> = {
    "/": "Tổng quan",
    "/admin": "Tổng quan",
    "/products": "Sản phẩm",
    "/admin/products": "Sản phẩm",
    "/inventory": "Kho & sổ kho",
    "/admin/inventory": "Kho & sổ kho",
    "/inventory/transfers": "Chuyển kho nội bộ (2 Bước)",
    "/admin/inventory/transfers": "Chuyển kho nội bộ (2 Bước)",
    "/inventory/counts": "Kiểm kê kho & cân bằng tồn",
    "/admin/inventory/counts": "Kiểm kê kho & cân bằng tồn",
    "/orders": "Đơn đa kênh",
    "/admin/orders": "Đơn đa kênh",
    "/reports": "Báo cáo tài chính & tồn kho",
    "/admin/reports": "Báo cáo tài chính & tồn kho",
    "/demo-tools": "Công cụ demo",
    "/handoff": "Handoff kỹ thuật",
    "/admin/handoff": "Handoff kỹ thuật",
    "/organization": "Nhân viên & chi nhánh",
    "/admin/organization": "Nhân viên & chi nhánh",
  };

  const current =
    nav.find((item) => item.to === location.pathname)?.label ||
    secondaryTitles[location.pathname] ||
    "OISMS";

  useEffect(() => {
    const portalLabel =
      account.role === "Staff"
        ? "OISMS Staff · Kho & Vận hành"
        : "OISMS Admin · Quản trị Cửa hàng";
    document.title = `${current} | ${portalLabel}`;
  }, [current, account.role]);

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
              <span>{networkError}</span>
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
              <button
                onClick={() => window.location.reload()}
                style={{
                  background: "#ffffff",
                  color: "#dc2626",
                  border: "none",
                  padding: "4px 12px",
                  borderRadius: "4px",
                  fontWeight: 600,
                  fontSize: "12px",
                  cursor: "pointer",
                  display: "flex",
                  alignItems: "center",
                  gap: "4px",
                }}
              >
                <RefreshCw size={12} /> Tải lại trang
              </button>
              <button
                onClick={() => setNetworkError(null)}
                style={{
                  background: "transparent",
                  color: "#ffffff",
                  border: "1px solid rgba(255,255,255,0.4)",
                  padding: "4px 8px",
                  borderRadius: "4px",
                  fontSize: "12px",
                  cursor: "pointer",
                }}
              >
                Đóng
              </button>
            </div>
          </div>
        )}
        <header className="topbar">
          <div className="topbar-left">
            <Button variant="ghost" className="mobile-toggle" onClick={() => setOpen(true)}>
              <Menu size={20} />
            </Button>
            <div className="search-box">
              <Search size={18} />
              <input placeholder="Tra cứu SKU, đơn hàng đa kênh, barcode..." />
            </div>
          </div>

          <div className="topbar-right">
            <Button variant="secondary" className="pos-btn" onClick={() => navigate("/pos")}>
              <Store size={18} />
              <span>Mở Web POS</span>
            </Button>
            <NotificationCenter />
            <div className="profile-chip">
              <span className="avatar">{account.initials}</span>
              <div className="profile-chip-text">
                <strong>{account.name}</strong>
                <small>
                  {account.role === "Owner"
                    ? "Chủ cửa hàng (Owner)"
                    : account.role === "Staff"
                    ? "Nhân viên kho (Staff)"
                    : "Thu ngân (Cashier)"}{" "}
                  · {account.branchName}
                </small>
              </div>
              <button
                className="profile-logout-btn"
                title="Đăng xuất khỏi hệ thống"
                aria-label="Đăng xuất"
                onClick={async () => {
                  await logout();
                  navigate("/login", { replace: true });
                }}
              >
                <LogOut size={16} />
              </button>
            </div>
          </div>
        </header>

        <main className="main-content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
