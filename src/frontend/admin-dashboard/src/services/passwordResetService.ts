import { useEffect, useState } from "react";
import { httpClient } from "./httpClient";

// Upgrade 10: quên mật khẩu qua email và Owner đặt lại mật khẩu hộ nhân viên.

export interface PasswordResetResult {
  ok: boolean;
  message?: string;
  error?: string;
}

function errorMessage(err: any, fallback: string): string {
  if (!err?.response) return "Không kết nối được máy chủ. Vui lòng thử lại.";
  const data = err.response.data;
  return data?.detail || data?.message || data?.errors?.[0] || fallback;
}

export async function getPasswordResetEnabled(): Promise<boolean> {
  const res = await httpClient.get("/api/auth/password-reset/status");
  return Boolean(res.data?.enabled);
}

/** null khi đang tải hoặc không gọi được API (khi đó vẫn cho mở trang, backend sẽ báo lỗi cụ thể). */
export function usePasswordResetEnabled(): boolean | null {
  const [enabled, setEnabled] = useState<boolean | null>(null);
  useEffect(() => {
    let active = true;
    getPasswordResetEnabled()
      .then((value) => { if (active) setEnabled(value); })
      .catch(() => { if (active) setEnabled(null); });
    return () => { active = false; };
  }, []);
  return enabled;
}

export async function requestPasswordReset(identifier: string): Promise<PasswordResetResult> {
  try {
    const res = await httpClient.post("/api/auth/forgot-password", { identifier, app: "admin" });
    return { ok: true, message: res.data?.message || "Nếu tài khoản tồn tại, chúng tôi đã gửi link đặt lại mật khẩu." };
  } catch (err) {
    return { ok: false, error: errorMessage(err, "Không gửi được yêu cầu đặt lại mật khẩu.") };
  }
}

export async function resetPassword(token: string, newPassword: string): Promise<PasswordResetResult> {
  try {
    await httpClient.post("/api/auth/reset-password", { token, newPassword });
    return { ok: true };
  } catch (err) {
    return { ok: false, error: errorMessage(err, "Đặt lại mật khẩu thất bại.") };
  }
}

export async function adminResetUserPassword(userId: string, newPassword: string): Promise<PasswordResetResult> {
  try {
    await httpClient.post(`/api/users/${userId}/reset-password`, { newPassword });
    return { ok: true };
  } catch (err) {
    return { ok: false, error: errorMessage(err, "Không đặt lại được mật khẩu cho nhân viên.") };
  }
}

let cachedResetToken: string | null = null;

/**
 * Đọc token từ phần # của URL rồi xóa khỏi thanh địa chỉ để không lưu vào lịch sử trình duyệt.
 * Giữ trong bộ nhớ để StrictMode gọi lại vẫn lấy được.
 */
export function takeResetTokenFromUrl(): string {
  const params = new URLSearchParams(window.location.hash.replace(/^#/, ""));
  const token = params.get("token") || "";
  if (token) {
    cachedResetToken = token;
    window.history.replaceState(null, "", window.location.pathname);
  }
  return cachedResetToken || "";
}
