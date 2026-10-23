import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import type { Role } from "../types/domain";
import { clearAuthSession, clearSessionOrdersStorage, httpClient } from "../services/httpClient";

export interface UserAccount {
  id: string;
  name: string;
  email: string;
  initials: string;
  role: Role;
  tenantId: string;
  tenantName: string;
  tenantCode: string;
  branchName: string;
  landingPath: string;
}

interface AuthContextValue {
  user: UserAccount | null;
  account: UserAccount; // Compatibility alias
  isAuthenticated: boolean;
  isLoading: boolean;
  networkError: string | null;
  setNetworkError: (err: string | null) => void;
  login: (credentials: { email: string; password: string }) => Promise<{ success: boolean; user?: UserAccount; error?: string }>;
  logout: () => Promise<void>;
  registerTenant: (payload: {
    tenantName: string;
    tenantCode?: string;
    ownerEmail: string;
    ownerPassword: string;
    ownerFullName: string;
    ownerPhone?: string;
  }) => Promise<{ success: boolean; user?: UserAccount; error?: string }>;
  selectAccount: (accountId: string) => UserAccount;
}

const fallbackGuest: UserAccount = {
  id: "guest",
  name: "Khách",
  email: "",
  initials: "K",
  role: "Cashier",
  tenantId: "",
  tenantName: "OISM System",
  tenantCode: "",
  branchName: "Chưa chọn chi nhánh",
  landingPath: "/login",
};

const AuthContext = createContext<AuthContextValue | null>(null);

function getInitials(name: string): string {
  if (!name) return "U";
  const parts = name.trim().split(" ");
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserAccount | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [networkError, setNetworkError] = useState<string | null>(null);

  // Lắng nghe sự kiện lỗi mạng từ httpClient interceptor
  useEffect(() => {
    const handleNetworkErr = (event: Event) => {
      const customEvent = event as CustomEvent<string>;
      setNetworkError(customEvent.detail || "Mất kết nối máy chủ");
    };
    window.addEventListener("oism_network_error", handleNetworkErr);
    return () => window.removeEventListener("oism_network_error", handleNetworkErr);
  }, []);

  // Tự động kiểm tra token & lấy thông tin người dùng thật từ API /api/auth/me
  useEffect(() => {
    async function loadMe() {
      const token = localStorage.getItem("oism_access_token");
      if (!token) {
        setIsLoading(false);
        return;
      }

      try {
        const res = await httpClient.get("/api/auth/me");
        const data = res.data;
        const role = (data.roles?.[0] || "Staff") as Role;
        // [A01] Nếu tenant của phiên khôi phục khác tenant đã lưu → dọn sạch dữ liệu tạm của tenant cũ
        const storedTenantId = localStorage.getItem("oism_tenant_id");
        if (storedTenantId && data.tenantId && storedTenantId !== data.tenantId) {
          clearSessionOrdersStorage();
        }
        if (data.tenantId) {
          localStorage.setItem("oism_tenant_id", data.tenantId);
        }
        // Dọn khóa cũ không có tiền tố tenantId
        sessionStorage.removeItem("oism_session_orders");

        const accountData: UserAccount = {
          id: data.id,
          name: data.fullName || data.email,
          email: data.email,
          initials: getInitials(data.fullName || data.email),
          role: role,
          tenantId: data.tenantId,
          tenantName: data.tenantName || "Cửa hàng của tôi",
          tenantCode: data.tenantCode || "",
          branchName: "Chi nhánh chính",
          landingPath: role === "Cashier" ? "/pos" : "/admin",
        };
        setUser(accountData);
      } catch (err: any) {
        console.warn("Không thể tải thông tin phiên người dùng:", err?.message);
        if (err?.response?.status === 401) {
          clearAuthSession();
        }
      } finally {
        setIsLoading(false);
      }
    }

    loadMe();
  }, []);

  const login = async (credentials: { email: string; password: string }) => {
    try {
      setNetworkError(null);
      const res = await httpClient.post("/api/auth/login", {
        email: credentials.email,
        password: credentials.password,
      });

      const { accessToken, refreshToken, user: userData } = res.data;

      // [A01] Xóa sạch dữ liệu đơn hàng phiên cũ (mọi tenant) trước khi khởi tạo phiên mới
      sessionStorage.removeItem("oism_session_orders");
      clearSessionOrdersStorage();

      localStorage.setItem("oism_access_token", accessToken);
      if (refreshToken) {
        localStorage.setItem("oism_refresh_token", refreshToken);
      }
      if (userData?.tenantId) {
        localStorage.setItem("oism_tenant_id", userData.tenantId);
      }

      const role = (userData.roles?.[0] || "Staff") as Role;
      const accountData: UserAccount = {
        id: userData.id,
        name: userData.fullName || userData.email,
        email: userData.email,
        initials: getInitials(userData.fullName || userData.email),
        role: role,
        tenantId: userData.tenantId,
        tenantName: userData.tenantName || "Cửa hàng của tôi",
        tenantCode: userData.tenantCode || "",
        branchName: "Chi nhánh chính",
        landingPath: role === "Cashier" ? "/pos" : "/admin",
      };

      setUser(accountData);
      return { success: true, user: accountData };
    } catch (err: any) {
      if (!err?.response) {
        return {
          success: false,
          error: "Lỗi mạng: Không thể kết nối đến máy chủ Backend (http://localhost:5000). Vui lòng kiểm tra dịch vụ.",
        };
      }
      const msg =
        err?.response?.data?.message ||
        err?.response?.data?.errors?.[0] ||
        "Đăng nhập thất bại. Vui lòng kiểm tra lại email và mật khẩu.";
      return { success: false, error: msg };
    }
  };

  const logout = async () => {
    try {
      const refreshToken = localStorage.getItem("oism_refresh_token");
      if (refreshToken) {
        await httpClient.post("/api/auth/logout", { refreshToken }).catch(() => {});
      }
    } finally {
      // [A01 — Blocking] Bắt buộc xóa sạch sessionStorage ("oism_session_orders" và "oism_orders_${tenantId}") khi logout
      sessionStorage.removeItem("oism_session_orders");
      clearAuthSession();
      setUser(null);
    }
  };

  const registerTenant = async (payload: {
    tenantName: string;
    tenantCode?: string;
    ownerEmail: string;
    ownerPassword: string;
    ownerFullName: string;
    ownerPhone?: string;
  }) => {
    try {
      setNetworkError(null);
      const rawCode =
        payload.tenantCode?.trim() ||
        payload.tenantName
          .trim()
          .toLowerCase()
          .normalize("NFD")
          .replace(/[\u0300-\u036f]/g, "")
          .replace(/[^a-z0-9]/g, "-")
          .replace(/-+/g, "-")
          .replace(/^-|-$/g, "");
      const finalCode = (rawCode.length >= 3 ? rawCode : `${rawCode || "shop"}-${Date.now().toString().slice(-4)}`)
        .slice(0, 30)
        .toUpperCase();

      await httpClient.post("/api/auth/register-tenant", {
        tenantName: payload.tenantName.trim(),
        tenantCode: finalCode,
        fullName: payload.ownerFullName.trim(),
        email: payload.ownerEmail.trim().toLowerCase(),
        phoneNumber: payload.ownerPhone?.trim() || null,
        password: payload.ownerPassword,
      });

      return await login({ email: payload.ownerEmail, password: payload.ownerPassword });
    } catch (err: any) {
      if (!err?.response) {
        return {
          success: false,
          error: "Lỗi mạng: Không thể kết nối đến máy chủ Backend (http://localhost:5000). Vui lòng kiểm tra dịch vụ.",
        };
      }
      const msg =
        err?.response?.data?.message ||
        err?.response?.data?.errors?.[0] ||
        "Đăng ký doanh nghiệp thất bại.";
      return { success: false, error: msg };
    }
  };

  const selectAccount = (accountId: string) => {
    console.info("selectAccount gọi với ID:", accountId);
    return user || fallbackGuest;
  };

  const value = useMemo(
    () => ({
      user,
      account: user || fallbackGuest,
      isAuthenticated: Boolean(user),
      isLoading,
      networkError,
      setNetworkError,
      login,
      logout,
      registerTenant,
      selectAccount,
    }),
    [user, isLoading, networkError]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth phải được sử dụng bên trong AuthProvider");
  return context;
}

export function useDemoSession() {
  return useAuth();
}
