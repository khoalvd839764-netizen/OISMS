import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import type { Role } from '../../types/auth.types';
import { CardSkeleton } from '../feedback/Skeleton';

export function ProtectedRoute({
  allowedRoles = ['Owner', 'Staff'],
}: {
  allowedRoles?: Role[];
}) {
  const { isAuthenticated, isLoading, user } = useAuth();
  const location = useLocation();

  if (isLoading) {
    return (
      <div style={{ padding: '48px 32px', maxWidth: 960, margin: '0 auto' }}>
        <div style={{ marginBottom: 16, fontWeight: 600, color: '#667085' }}>
          Đang xác thực phiên làm việc...
        </div>
        <CardSkeleton />
      </div>
    );
  }

  if (!isAuthenticated || !user) {
    return <Navigate to="/login" state={{ from: location.pathname }} replace />;
  }

  const hasPermission = user.roles?.some((r) => allowedRoles.includes(r));
  if (!hasPermission) {
    return <Navigate to="/403" replace />;
  }

  return <Outlet />;
}
