import { clearAuthSession, clearSessionOrdersStorage, httpClient, refreshAccessToken } from "./httpClient";

export interface LoginCredentials {
  identity?: string;
  email?: string;
  password: string;
}

export const authAdapter = {
  async login(credentials: LoginCredentials) {
    const email = credentials.email || credentials.identity;
    if (!email || !credentials.password) {
      return { error: { code: "VALIDATION_ERROR", message: "Vui lòng nhập đầy đủ thông tin đăng nhập." } };
    }

    try {
      const res = await httpClient.post("/api/auth/login", {
        email: email,
        password: credentials.password,
      });

      const { accessToken, refreshToken, expiresIn, user } = res.data;
      // [A01] Khi đăng nhập phiên mới, luôn dọn sạch đơn hàng tạm của phiên/tenant trước
      clearSessionOrdersStorage();

      localStorage.setItem("oism_access_token", accessToken);
      if (refreshToken) {
        localStorage.setItem("oism_refresh_token", refreshToken);
      }
      if (user?.tenantId) {
        localStorage.setItem("oism_tenant_id", user.tenantId);
      }

      return {
        data: {
          expiresAt: Date.now() + (expiresIn || 3600) * 1000,
          user,
        },
      };
    } catch (err: any) {
      const message =
        err?.response?.data?.message ||
        err?.response?.data?.errors?.[0] ||
        "Tài khoản hoặc mật khẩu không chính xác.";
      return { error: { code: "AUTH_FAILED", message } };
    }
  },

  async refresh() {
    if (!localStorage.getItem("oism_refresh_token")) {
      return { error: { code: "SESSION_EXPIRED", message: "Phiên đăng nhập đã hết hạn." } };
    }

    try {
      // [A06] Dùng chung khóa single-flight của httpClient để không phát sinh 2 lượt refresh song song
      await refreshAccessToken();
      return { data: { expiresAt: Date.now() + 3600 * 1000 } };
    } catch {
      clearAuthSession();
      return { error: { code: "REFRESH_FAILED", message: "Không thể làm mới phiên đăng nhập." } };
    }
  },

  async logout() {
    try {
      const refreshToken = localStorage.getItem("oism_refresh_token");
      if (refreshToken) {
        await httpClient.post("/api/auth/logout", { refreshToken }).catch(() => {});
      }
    } finally {
      // [A01] Bắt buộc dọn sạch token và đơn hàng lưu tạm trong sessionStorage khi đăng xuất
      sessionStorage.removeItem("oism_session_orders");
      clearAuthSession();
    }
    return { data: true };
  },

  async getMe() {
    try {
      const res = await httpClient.get("/api/auth/me");
      if (res.data?.tenantId) {
        localStorage.setItem("oism_tenant_id", res.data.tenantId);
      }
      return { data: res.data };
    } catch (err: any) {
      return { error: { code: "ME_FAILED", message: err?.message || "Không thể lấy thông tin người dùng." } };
    }
  },
};
