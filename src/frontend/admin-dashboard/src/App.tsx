import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { AdminShell } from "./components/AdminShell";
import { ProtectedRoute } from "./components/ProtectedRoute";
import { DashboardPage } from "./pages/DashboardPage";
import { ProductsPage } from "./pages/ProductsPage";
import { InventoryPage } from "./pages/InventoryPage";
import { OrdersPage } from "./pages/OrdersPage";
import { ReportsPage } from "./pages/ReportsPage";
import { HandoffPage } from "./pages/HandoffPage";
import { PosPage } from "./pages/PosPage";
import { AccessPage } from "./pages/AccessPage";
import { AuthPage } from "./pages/AuthPage";
import { OrganizationPage } from "./pages/OrganizationPage";
import { AuthProvider } from "./context/AuthContext";

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          {/* Public Routes */}
          <Route path="/login" element={<AuthPage />} />
          <Route path="/403" element={<AccessPage type="forbidden" />} />
          <Route path="/session-expired" element={<AccessPage type="expired" />} />

          {/* POS Route - Cho phép Owner, Staff và Cashier đã xác thực */}
          <Route element={<ProtectedRoute allowedRoles={["Owner", "Staff", "Cashier"]} />}>
            <Route path="/pos" element={<PosPage />} />
          </Route>

          {/* Protected Admin Routes */}
          <Route element={<ProtectedRoute />}>
            <Route element={<AdminShell />}>
              <Route path="/" element={<DashboardPage />} />
              <Route path="/products" element={<ProductsPage />} />
              <Route path="/inventory" element={<InventoryPage />} />
              <Route path="/orders" element={<OrdersPage />} />
              <Route path="/reports" element={<ReportsPage />} />
              <Route path="/handoff" element={<HandoffPage />} />
              {/* Chỉ Owner được truy cập trang Quản lý Chi nhánh & Tổ chức */}
              <Route element={<ProtectedRoute allowedRoles={["Owner"]} />}>
                <Route path="/organization" element={<OrganizationPage />} />
              </Route>
            </Route>
          </Route>

          {/* Fallback */}
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
