import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "../context/AuthContext";

export function ProtectedRoute({ allowedRoles }: { allowedRoles?: string[] }) {
  const { user, isAuthenticated, isLoading } = useAuth();

  if (isLoading) {
    return (
      <div style={{ display: "grid", placeItems: "center", minHeight: "100vh", background: "#f6f7f9", color: "#172033" }}>
        <div style={{ textAlign: "center" }}>
          <div style={{ width: "36px", height: "36px", border: "3px solid #e2e8f0", borderTopColor: "#1769e0", borderRadius: "50%", animation: "spin 0.8s linear infinite", margin: "0 auto 14px" }} />
          <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
          <strong>Đang xác thực phiên làm việc OISM...</strong>
        </div>
      </div>
    );
  }

  // Chưa đăng nhập -> về trang Login
  if (!isAuthenticated || !user) {
    return <Navigate to="/login" replace />;
  }

  // Mặc định các route Admin chỉ cho phép Owner và Staff (chặn Cashier -> 403 Forbidden)
  const effectiveRoles = allowedRoles ?? ["Owner", "Staff"];
  if (!effectiveRoles.includes(user.role)) {
    return <Navigate to="/403" replace />;
  }

  return <Outlet />;
}
