import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { authApi } from '../api/auth.api';
import { TOKEN_STORAGE_KEY, USER_STORAGE_KEY, clearSession } from '../api/client';
import type {
  AuthResponse,
  LoginRequest,
  RegisterTenantRequest,
  Role,
  UserProfileDto,
} from '../types/auth.types';

export interface AuthContextValue {
  user: UserProfileDto | null;
  role: Role | null;
  isOwner: boolean;
  isStaff: boolean;
  isAuthenticated: boolean;
  isLoading: boolean;
  hasRole: (allowedRoles: Role[]) => boolean;
  login: (payload: LoginRequest) => Promise<AuthResponse>;
  registerTenant: (payload: RegisterTenantRequest) => Promise<AuthResponse>;
  logout: () => Promise<void>;
  refreshProfile: () => Promise<UserProfileDto | null>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

function readStoredUser(): UserProfileDto | null {
  try {
    const raw = localStorage.getItem(USER_STORAGE_KEY);
    if (!raw) return null;
    return JSON.parse(raw) as UserProfileDto;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserProfileDto | null>(() => {
    const token = localStorage.getItem(TOKEN_STORAGE_KEY);
    return token ? readStoredUser() : null;
  });
  const [isLoading, setIsLoading] = useState<boolean>(() => {
    return Boolean(localStorage.getItem(TOKEN_STORAGE_KEY));
  });

  const refreshProfile = useCallback(async (): Promise<UserProfileDto | null> => {
    const token = localStorage.getItem(TOKEN_STORAGE_KEY);
    if (!token) {
      setUser(null);
      setIsLoading(false);
      return null;
    }

    try {
      const profile = await authApi.getMe();
      setUser(profile);
      return profile;
    } catch (err: unknown) {
      const status = (err as { status?: number })?.status;
      if (status === 401) {
        clearSession();
        setUser(null);
      }
      return null;
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    const token = localStorage.getItem(TOKEN_STORAGE_KEY);
    if (token) {
      void refreshProfile();
    } else {
      setIsLoading(false);
    }
  }, [refreshProfile]);

  useEffect(() => {
    const handleSyncAuth = () => {
      const token = localStorage.getItem(TOKEN_STORAGE_KEY);
      setUser(token ? readStoredUser() : null);
    };

    const handleSessionExpired = () => {
      setUser(null);
    };

    window.addEventListener('oism:auth-change', handleSyncAuth);
    window.addEventListener('oism:session-expired', handleSessionExpired);
    return () => {
      window.removeEventListener('oism:auth-change', handleSyncAuth);
      window.removeEventListener('oism:session-expired', handleSessionExpired);
    };
  }, []);

  const login = useCallback(async (payload: LoginRequest): Promise<AuthResponse> => {
    const response = await authApi.login(payload);
    setUser(response.user);
    return response;
  }, []);

  const registerTenant = useCallback(
    async (payload: RegisterTenantRequest): Promise<AuthResponse> => {
      const response = await authApi.registerTenant(payload);
      if (response.user) {
        setUser(response.user);
      }
      return response;
    },
    []
  );

  const logout = useCallback(async (): Promise<void> => {
    await authApi.logout();
    setUser(null);
  }, []);

  const hasRole = useCallback(
    (allowedRoles: Role[]): boolean => {
      if (!user?.roles) return false;
      return user.roles.some((r) => allowedRoles.includes(r));
    },
    [user]
  );

  const value = useMemo<AuthContextValue>(() => {
    const primaryRole: Role | null = user?.roles?.[0] ?? null;
    const isOwner = Boolean(user?.roles?.includes('Owner'));
    const isStaff = Boolean(user?.roles?.includes('Staff'));
    const isAuthenticated = Boolean(user && localStorage.getItem(TOKEN_STORAGE_KEY));

    return {
      user,
      role: primaryRole,
      isOwner,
      isStaff,
      isAuthenticated,
      isLoading,
      hasRole,
      login,
      registerTenant,
      logout,
      refreshProfile,
    };
  }, [user, isLoading, hasRole, login, registerTenant, logout, refreshProfile]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth phải được sử dụng bên trong AuthProvider');
  }
  return context;
}
