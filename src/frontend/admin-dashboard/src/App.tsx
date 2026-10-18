import { Suspense, lazy } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AdminShell } from './components/layout/AdminShell';
import { AuthProvider } from './context/AuthContext';
import { ToastProvider } from './components/feedback/Toast';
import { ErrorBoundary } from './components/feedback/ErrorBoundary';
import { CardSkeleton } from './components/feedback/Skeleton';
import { ProtectedRoute } from './components/guards/ProtectedRoute';
import { RoleGuard } from './components/guards/RoleGuard';
import { AccessPage } from './pages/AccessPage';

// Route-level Code Splitting (React.lazy)
const AuthPage = lazy(() =>
  import('./pages/AuthPage').then((m) => ({ default: m.AuthPage }))
);
const DashboardPage = lazy(() =>
  import('./pages/DashboardPage').then((m) => ({ default: m.DashboardPage }))
);
const ProductsPage = lazy(() =>
  import('./pages/ProductsPage').then((m) => ({ default: m.ProductsPage }))
);
const InventoryPage = lazy(() =>
  import('./pages/InventoryPage').then((m) => ({ default: m.InventoryPage }))
);
const OrdersPage = lazy(() =>
  import('./pages/OrdersPage').then((m) => ({ default: m.OrdersPage }))
);
const ReportsPage = lazy(() =>
  import('./pages/ReportsPage').then((m) => ({ default: m.ReportsPage }))
);
const OrganizationPage = lazy(() =>
  import('./pages/OrganizationPage').then((m) => ({ default: m.OrganizationPage }))
);
const HandoffPage = lazy(() =>
  import('./pages/HandoffPage').then((m) => ({ default: m.HandoffPage }))
);
const PosPage = lazy(() =>
  import('./pages/PosPage').then((m) => ({ default: m.PosPage }))
);

function RouteLoadingFallback() {
  return (
    <div style={{ padding: '24px' }}>
      <CardSkeleton />
    </div>
  );
}

export default function App() {
  return (
    <ErrorBoundary>
      <ToastProvider>
        <AuthProvider>
          <BrowserRouter>
            <Suspense fallback={<RouteLoadingFallback />}>
              <Routes>
                {/* Public and error routes */}
                <Route path="/login" element={<AuthPage />} />
                <Route path="/403" element={<AccessPage type="forbidden" />} />
                <Route path="/session-expired" element={<AccessPage type="expired" />} />

                {/* POS Route protected for all authenticated roles */}
                <Route element={<ProtectedRoute allowedRoles={['Owner', 'Staff', 'Cashier']} />}>
                  <Route path="/pos" element={<PosPage />} />
                </Route>

                {/* Authenticated Admin Shell (Owner & Staff only; Cashier redirects to /pos) */}
                <Route element={<ProtectedRoute allowedRoles={['Owner', 'Staff']} />}>
                  <Route element={<AdminShell />}>
                    <Route path="/" element={<DashboardPage />} />
                    <Route path="/products" element={<ProductsPage />} />
                    <Route path="/inventory" element={<InventoryPage />} />
                    <Route path="/orders" element={<OrdersPage />} />
                    <Route path="/handoff" element={<HandoffPage />} />

                    {/* Owner-only routes */}
                    <Route
                      path="/reports"
                      element={
                        <RoleGuard allowedRoles={['Owner']}>
                          <ReportsPage />
                        </RoleGuard>
                      }
                    />
                    <Route
                      path="/organization"
                      element={
                        <RoleGuard allowedRoles={['Owner']}>
                          <OrganizationPage />
                        </RoleGuard>
                      }
                    />
                  </Route>
                </Route>

                {/* Fallback route */}
                <Route path="*" element={<Navigate to="/" replace />} />
              </Routes>
            </Suspense>
          </BrowserRouter>
        </AuthProvider>
      </ToastProvider>
    </ErrorBoundary>
  );
}
