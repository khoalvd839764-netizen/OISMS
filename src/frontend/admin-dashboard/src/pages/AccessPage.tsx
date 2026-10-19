import { Clock3, LockKeyhole, LogOut, ShieldAlert, ArrowLeft } from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { Badge, Button } from "../components/ui";

export function AccessPage({ type }: { type: "forbidden" | "expired" }) {
  const { user, isAuthenticated, logout } = useAuth();
  const navigate = useNavigate();
  const forbidden = type === "forbidden";

  const handleSwitchAccount = async () => {
    await logout();
    navigate("/login", { replace: true });
  };

  return (
    <main className="access-page">
      <div className="access-card" style={{ maxWidth: "480px" }}>
        <div className="brand-mark">O</div>
        <div style={{ marginBottom: "8px" }}>
          <Badge tone={forbidden ? "danger" : "warning"}>
            {forbidden ? "HTTP 403 · FORBIDDEN" : "HTTP 401 · SESSION EXPIRED"}
          </Badge>
        </div>
        <span className="access-icon">
          {forbidden ? <LockKeyhole size={28} /> : <Clock3 size={28} />}
        </span>
        <h1>{forbidden ? "Bạn không có quyền truy cập" : "Phiên đăng nhập đã hết hạn"}</h1>
        <p>
          {forbidden
            ? "Tài khoản hiện tại không được cấp quyền cho kho hoặc phân hệ Quản trị này. Hệ thống RBAC đã chặn yêu cầu truy cập."
            : "Vì lý do bảo mật, phiên làm việc của bạn đã hết hạn hoặc bị thu hồi. Vui lòng đăng nhập lại để tiếp tục."}
        </p>

        {forbidden && isAuthenticated && user && (
          <div
            style={{
              background: "#f8fafc",
              border: "1px solid #e2e8f0",
              borderRadius: "10px",
              padding: "12px 14px",
              margin: "12px 0 18px",
              textAlign: "left",
              fontSize: "13px",
              display: "flex",
              alignItems: "center",
              gap: "10px",
            }}
          >
            <ShieldAlert size={18} color="#dc2626" style={{ flexShrink: 0 }} />
            <div>
              <div>
                Tài khoản: <strong>{user.name}</strong> ({user.email})
              </div>
              <div style={{ color: "#64748b", marginTop: "2px" }}>
                Vai trò hiện tại: <Badge tone="warning">{user.role}</Badge> · Doanh nghiệp: <strong>{user.tenantName}</strong>
              </div>
            </div>
          </div>
        )}

        <div style={{ display: "flex", gap: "10px", justifyContent: "center", flexWrap: "wrap", marginTop: "8px" }}>
          {forbidden ? (
            <>
              {user?.role !== "Cashier" && (
                <Link className="button button--primary" to="/">
                  <ArrowLeft size={16} /> Về trang tổng quan
                </Link>
              )}
              <Button variant="secondary" onClick={handleSwitchAccount}>
                <LogOut size={16} /> Đăng xuất / Đổi tài khoản
              </Button>
            </>
          ) : (
            <Link className="button button--primary" to="/login" onClick={() => logout()}>
              Đăng nhập lại
            </Link>
          )}
        </div>

        {forbidden && user?.role === "Cashier" && (
          <p>Tài khoản thu ngân dùng ứng dụng POS riêng. Vui lòng truy cập địa chỉ POS do quản trị viên cung cấp.</p>
        )}

        <small style={{ display: "block", marginTop: "16px" }}>
          Ẩn menu trên giao diện không thay thế việc kiểm tra phân quyền RBAC tại Route Guard và Backend.
        </small>
      </div>
    </main>
  );
}
