import { useCallback, useEffect, useRef, useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import {
  Boxes,
  ChartNoAxesCombined,
  ChevronDown,
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
  Receipt,
  CreditCard,
} from "lucide-react";
import { Button } from "./ui";
import { useAuth } from "../context/AuthContext";
import { NotificationCenter } from "./NotificationCenter";
import { apiAdapter } from "../services/apiAdapter";

const nav = [
  { to: "/admin", label: "Tổng quan", icon: LayoutDashboard },
  { to: "/products", label: "Sản phẩm", icon: PackageSearch },
  { to: "/purchase-orders", label: "Đơn mua hàng", icon: ClipboardList },
  { to: "/inventory", label: "Kho & sổ kho", icon: Boxes },
  { to: "/inventory/transfers", label: "Chuyển kho", icon: ArrowLeftRight },
  { to: "/inventory/counts", label: "Kiểm kê kho", icon: ClipboardCheck },
  { to: "/orders", label: "Đơn đa kênh", icon: ShoppingBag },
  { to: "/invoices", label: "Hóa đơn & Truy vết", icon: Receipt },
  { to: "/reports", label: "Báo cáo", icon: ChartNoAxesCombined },
];

export function AdminShell() {
  const [open, setOpen] = useState(false);
  const location = useLocation();
  const navigate = useNavigate();
  const { user, logout, networkError, setNetworkError } = useAuth();

  // Tìm kiếm toàn hệ thống đa thực thể (CS-04)
  const [searchQuery, setSearchQuery] = useState("");
  const [searchResults, setSearchResults] = useState<{
    query: string;
    products: Array<{ id: string; title: string; subtitle?: string; status?: string; url: string }>;
    orders: Array<{ id: string; title: string; subtitle?: string; status?: string; url: string }>;
    goodsReceipts: Array<{ id: string; title: string; subtitle?: string; status?: string; url: string }>;
  } | null>(null);
  const [isSearching, setIsSearching] = useState(false);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [showDropdown, setShowDropdown] = useState(false);
  const searchSeqRef = useRef(0);
  const searchBoxRef = useRef<HTMLDivElement>(null);

  const performSearch = useCallback(async (queryText: string) => {
    const q = queryText.trim();
    const seq = ++searchSeqRef.current;
    if (!q) {
      setSearchResults(null);
      setSearchError(null);
      setIsSearching(false);
      setShowDropdown(false);
      return;
    }

    setIsSearching(true);
    setSearchError(null);
    setShowDropdown(true);

    const res = await apiAdapter.globalSearch(q);
    if (seq !== searchSeqRef.current) return;

    if (res.error) {
      setSearchError(res.error.message || "Tìm kiếm thất bại. Vui lòng thử lại.");
      setSearchResults(null);
    } else if (res.data) {
      setSearchResults(res.data);
      setSearchError(null);
    }
    setIsSearching(false);
  }, []);

  useEffect(() => {
    const timer = setTimeout(() => {
      performSearch(searchQuery);
    }, 250);

    return () => clearTimeout(timer);
  }, [searchQuery, performSearch]);

  // Đóng danh sách kết quả khi bấm ra ngoài ô tìm kiếm
  useEffect(() => {
    if (!showDropdown) return;
    const handleMouseDown = (e: MouseEvent) => {
      if (!searchBoxRef.current?.contains(e.target as Node)) setShowDropdown(false);
    };
    document.addEventListener("mousedown", handleMouseDown);
    return () => document.removeEventListener("mousedown", handleMouseDown);
  }, [showDropdown]);

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
    "/purchase-orders": "Đơn mua hàng từ NCC",
    "/admin/purchase-orders": "Đơn mua hàng từ NCC",
    "/invoices": "Hóa đơn & Truy vết",
    "/admin/invoices": "Hóa đơn & Truy vết",
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
    "/organization": "Nhân viên & chi nhánh",
    "/admin/organization": "Nhân viên & chi nhánh",
    "/shop-integrations": "Kết nối sàn",
    "/admin/shop-integrations": "Kết nối sàn",
    "/settings/payment-accounts": "Tài khoản nhận tiền VietQR",
    "/admin/settings/payment-accounts": "Tài khoản nhận tiền VietQR",
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
          <Button variant="ghost" className="mobile-close" aria-label="Đóng menu điều hướng" onClick={() => setOpen(false)}>
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
        </nav>

        <div className="sidebar-bottom">
          {account.role === "Owner" && (
            <>
              <NavLink to="/organization">
                <Settings2 size={18} />Nhân viên & chi nhánh
              </NavLink>
              <NavLink to="/shop-integrations">
                <Store size={18} />Kết nối sàn
              </NavLink>
              <NavLink to="/settings/payment-accounts">
                <CreditCard size={18} />Tài khoản nhận tiền
              </NavLink>
            </>
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
            <Button variant="ghost" className="mobile-toggle" aria-label="Mở menu điều hướng" aria-expanded={open} onClick={() => setOpen(true)}>
              <Menu size={20} />
            </Button>
            <div className="search-box" ref={searchBoxRef} style={{ position: "relative" }}>
              <Search size={18} />
              <input
                aria-label="Tìm kiếm toàn hệ thống"
                placeholder="Tìm sản phẩm, đơn hàng, phiếu kho..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                onFocus={() => { if (searchQuery.trim()) setShowDropdown(true); }}
              />
              {searchQuery && (
                <button
                  type="button"
                  onClick={() => { setSearchQuery(""); setShowDropdown(false); }}
                  style={{ background: "transparent", border: "none", color: "#94a3b8", cursor: "pointer", padding: "2px" }}
                >
                  <X size={15} />
                </button>
              )}

              {/* DROPDOWN KẾT QUẢ TÌM KIẾM TOÀN HỆ THỐNG (CS-04) */}
              {showDropdown && (
                <div
                  style={{
                    position: "absolute",
                    top: "calc(100% + 8px)",
                    left: 0,
                    width: "420px",
                    maxWidth: "90vw",
                    background: "#ffffff",
                    border: "1px solid #e2e8f0",
                    borderRadius: "10px",
                    boxShadow: "0 10px 25px -5px rgba(0, 0, 0, 0.1), 0 8px 10px -6px rgba(0, 0, 0, 0.1)",
                    zIndex: 1000,
                    maxHeight: "450px",
                    overflowY: "auto",
                    padding: "8px 0",
                  }}
                >
                  {isSearching ? (
                    <div style={{ padding: "16px", textAlign: "center", color: "#64748b", fontSize: "13px" }}>
                      Đang tìm kiếm toàn hệ thống...
                    </div>
                  ) : searchError ? (
                    <div style={{ padding: "16px", textAlign: "center", color: "#b91c1c", fontSize: "13px" }}>
                      <p style={{ margin: "0 0 8px" }}>{searchError}</p>
                      <button
                        type="button"
                        onClick={() => performSearch(searchQuery)}
                        style={{
                          background: "#fff",
                          border: "1px solid #f87171",
                          color: "#b91c1c",
                          borderRadius: "6px",
                          padding: "4px 10px",
                          fontSize: "12px",
                          cursor: "pointer",
                          fontWeight: 600,
                        }}
                      >
                        Thử lại
                      </button>
                    </div>
                  ) : !searchResults || (searchResults.products.length === 0 && searchResults.orders.length === 0 && searchResults.goodsReceipts.length === 0) ? (
                    <div style={{ padding: "16px", textAlign: "center", color: "#64748b", fontSize: "13px" }}>
                      Không tìm thấy kết quả phù hợp cho "{searchQuery}".
                    </div>
                  ) : (
                    <div>
                      {/* 1. SẢN PHẨM & SKU */}
                      {searchResults.products.length > 0 && (
                        <div>
                          <div style={{ padding: "6px 14px", fontSize: "11px", fontWeight: 700, color: "#94a3b8", textTransform: "uppercase", background: "#f8fafc" }}>
                            Sản phẩm & SKU ({searchResults.products.length})
                          </div>
                          {searchResults.products.map((p) => (
                            <div
                              key={p.id}
                              onClick={() => {
                                setShowDropdown(false);
                                navigate(`${p.url}?q=${encodeURIComponent(searchQuery.trim())}`);
                              }}
                              style={{
                                padding: "8px 14px",
                                cursor: "pointer",
                                borderBottom: "1px solid #f1f5f9",
                                display: "flex",
                                justifyContent: "space-between",
                                alignItems: "center",
                              }}
                              onMouseEnter={(e) => (e.currentTarget.style.background = "#f1f5f9")}
                              onMouseLeave={(e) => (e.currentTarget.style.background = "transparent")}
                            >
                              <div>
                                <div style={{ fontSize: "13px", fontWeight: 600, color: "#1e293b" }}>{p.title}</div>
                                <div style={{ fontSize: "11px", color: "#64748b" }}>{p.subtitle}</div>
                              </div>
                              <span style={{ fontSize: "11px", color: "#2563eb", fontWeight: 500 }}>Chi tiết →</span>
                            </div>
                          ))}
                        </div>
                      )}

                      {/* 2. ĐƠN HÀNG */}
                      {searchResults.orders.length > 0 && (
                        <div>
                          <div style={{ padding: "6px 14px", fontSize: "11px", fontWeight: 700, color: "#94a3b8", textTransform: "uppercase", background: "#f8fafc" }}>
                            Đơn hàng đa kênh ({searchResults.orders.length})
                          </div>
                          {searchResults.orders.map((o) => (
                            <div
                              key={o.id}
                              onClick={() => {
                                setShowDropdown(false);
                                navigate(`${o.url}?orderId=${o.id}`);
                              }}
                              style={{
                                padding: "8px 14px",
                                cursor: "pointer",
                                borderBottom: "1px solid #f1f5f9",
                                display: "flex",
                                justifyContent: "space-between",
                                alignItems: "center",
                              }}
                              onMouseEnter={(e) => (e.currentTarget.style.background = "#f1f5f9")}
                              onMouseLeave={(e) => (e.currentTarget.style.background = "transparent")}
                            >
                              <div>
                                <div style={{ fontSize: "13px", fontWeight: 600, color: "#1e293b" }}>{o.title}</div>
                                <div style={{ fontSize: "11px", color: "#64748b" }}>{o.subtitle}</div>
                              </div>
                              <span style={{ fontSize: "10px", padding: "2px 6px", borderRadius: "4px", background: "#f1f5f9", color: "#475569" }}>
                                {o.status}
                              </span>
                            </div>
                          ))}
                        </div>
                      )}

                      {/* 3. PHIẾU NHẬP KHO */}
                      {searchResults.goodsReceipts.length > 0 && (
                        <div>
                          <div style={{ padding: "6px 14px", fontSize: "11px", fontWeight: 700, color: "#94a3b8", textTransform: "uppercase", background: "#f8fafc" }}>
                            Phiếu nhập kho ({searchResults.goodsReceipts.length})
                          </div>
                          {searchResults.goodsReceipts.map((g) => (
                            <div
                              key={g.id}
                              onClick={() => {
                                setShowDropdown(false);
                                navigate(g.url);
                              }}
                              style={{
                                padding: "8px 14px",
                                cursor: "pointer",
                                borderBottom: "1px solid #f1f5f9",
                                display: "flex",
                                justifyContent: "space-between",
                                alignItems: "center",
                              }}
                              onMouseEnter={(e) => (e.currentTarget.style.background = "#f1f5f9")}
                              onMouseLeave={(e) => (e.currentTarget.style.background = "transparent")}
                            >
                              <div>
                                <div style={{ fontSize: "13px", fontWeight: 600, color: "#1e293b" }}>{g.title}</div>
                                <div style={{ fontSize: "11px", color: "#64748b" }}>{g.subtitle}</div>
                              </div>
                              <span style={{ fontSize: "10px", padding: "2px 6px", borderRadius: "4px", background: "#f1f5f9", color: "#475569" }}>
                                {g.status}
                              </span>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )}
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
