import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import type { Role } from '../../types/auth.types';

export function RoleGuard({
  allowedRoles,
  children,
}: {
  allowedRoles: Role[];
  children: ReactNode;
}) {
  const { user, isLoading } = useAuth();

  if (isLoading) return null;

  const hasRole = user?.roles?.some((role) => allowedRoles.includes(role));
  if (!hasRole) {
    return <Navigate to="/403" replace />;
  }

  return <>{children}</>;
}
