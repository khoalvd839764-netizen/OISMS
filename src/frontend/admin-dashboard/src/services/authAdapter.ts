import { httpClient } from "./httpClient";

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
      localStorage.setItem("oism_access_token", accessToken);
      if (refreshToken) {
        localStorage.setItem("oism_refresh_token", refreshToken);
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
    const refreshToken = localStorage.getItem("oism_refresh_token");
    if (!refreshToken) {
      return { error: { code: "SESSION_EXPIRED", message: "Phiên đăng nhập đã hết hạn." } };
    }

    try {
      const res = await httpClient.post("/api/auth/refresh-token", { refreshToken });
      const { accessToken, refreshToken: newRefreshToken, expiresIn } = res.data;
      localStorage.setItem("oism_access_token", accessToken);
      if (newRefreshToken) {
        localStorage.setItem("oism_refresh_token", newRefreshToken);
      }
      return { data: { expiresAt: Date.now() + (expiresIn || 3600) * 1000 } };
    } catch {
      localStorage.removeItem("oism_access_token");
      localStorage.removeItem("oism_refresh_token");
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
      localStorage.removeItem("oism_access_token");
      localStorage.removeItem("oism_refresh_token");
    }
    return { data: true };
  },

  async getMe() {
    try {
      const res = await httpClient.get("/api/auth/me");
      return { data: res.data };
    } catch (err: any) {
      return { error: { code: "ME_FAILED", message: err?.message || "Không thể lấy thông tin người dùng." } };
    }
  },
};
