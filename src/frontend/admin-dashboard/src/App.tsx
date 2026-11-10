import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { AdminShell } from "./components/AdminShell";
import { ProtectedRoute } from "./components/ProtectedRoute";
import { DashboardPage } from "./pages/DashboardPage";
import { ProductsPage } from "./pages/ProductsPage";
import { PurchaseOrdersPage } from "./pages/PurchaseOrdersPage";
import { InventoryPage } from "./pages/InventoryPage";
import { InventoryTransfersPage } from "./pages/InventoryTransfersPage";
import { StockCountsPage } from "./pages/StockCountsPage";
import { OrdersPage } from "./pages/OrdersPage";
import { SalesReturnsPage } from "./pages/SalesReturnsPage";
import { SalesInvoicesPage } from "./pages/SalesInvoicesPage";
import { ReportsPage } from "./pages/ReportsPage";
import { PosRedirectPage } from "./pages/PosRedirectPage";
import { AccessPage } from "./pages/AccessPage";
import { AuthPage } from "./pages/AuthPage";
import { OrganizationPage } from "./pages/OrganizationPage";
import { ShopIntegrationsPage } from "./pages/ShopIntegrationsPage";
import { PaymentAccountsPage } from "./pages/PaymentAccountsPage";
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

          {/* Compatibility link: the workstation lives only in pos-app. */}
          <Route path="/pos" element={<PosRedirectPage />} />

          {/* Protected Admin Routes (Hỗ trợ cả http://localhost:5173/admin và /) */}
          <Route element={<ProtectedRoute />}>
            <Route element={<AdminShell />}>
              <Route path="/" element={<DashboardPage />} />
              <Route path="/admin" element={<DashboardPage />} />
              <Route path="/products" element={<ProductsPage />} />
              <Route path="/admin/products" element={<ProductsPage />} />
              <Route path="/purchase-orders" element={<PurchaseOrdersPage />} />
              <Route path="/admin/purchase-orders" element={<PurchaseOrdersPage />} />
              <Route path="/inventory" element={<InventoryPage />} />
              <Route path="/admin/inventory" element={<InventoryPage />} />
              <Route path="/inventory/transfers" element={<InventoryTransfersPage />} />
              <Route path="/admin/inventory/transfers" element={<InventoryTransfersPage />} />
              <Route path="/inventory/counts" element={<StockCountsPage />} />
              <Route path="/admin/inventory/counts" element={<StockCountsPage />} />
              <Route path="/orders" element={<OrdersPage />} />
              <Route path="/admin/orders" element={<OrdersPage />} />
              <Route path="/invoices" element={<SalesInvoicesPage />} />
              <Route path="/admin/invoices" element={<SalesInvoicesPage />} />

              {/* Phân hệ Quản lý Trả hàng (CS-01): Chỉ Owner và Staff */}
              <Route element={<ProtectedRoute allowedRoles={["Owner", "Staff"]} />}>
                <Route path="/returns" element={<SalesReturnsPage />} />
                <Route path="/admin/returns" element={<SalesReturnsPage />} />
              </Route>

              {/* Chỉ Owner được truy cập Báo cáo, Chi nhánh / Tổ chức và Kết nối sàn */}
              <Route element={<ProtectedRoute allowedRoles={["Owner"]} />}>
                <Route path="/reports" element={<ReportsPage />} />
                <Route path="/admin/reports" element={<ReportsPage />} />
                <Route path="/organization" element={<OrganizationPage />} />
                <Route path="/admin/organization" element={<OrganizationPage />} />
                <Route path="/shop-integrations" element={<ShopIntegrationsPage />} />
                <Route path="/admin/shop-integrations" element={<ShopIntegrationsPage />} />
                <Route path="/settings/payment-accounts" element={<PaymentAccountsPage />} />
                <Route path="/admin/settings/payment-accounts" element={<PaymentAccountsPage />} />
              </Route>
            </Route>
          </Route>

          {/* Fallback */}
          <Route path="*" element={<Navigate to="/admin" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
