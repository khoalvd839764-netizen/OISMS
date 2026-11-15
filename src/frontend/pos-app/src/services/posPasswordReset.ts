import { API_BASE_URL } from './posSession';

// Upgrade 10: quên mật khẩu từ POS. Link trong mail quay về trang /reset-password của POS.

async function post(path: string, body: unknown): Promise<{ ok: boolean; data: any }> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
  });
  const data = await response.json().catch(() => ({}));
  return { ok: response.ok, data };
}

function messageOf(data: any, fallback: string): string {
  return data?.detail || data?.message || data?.errors?.[0] || fallback;
}

export async function getPasswordResetEnabled(): Promise<boolean> {
  const response = await fetch(`${API_BASE_URL}/api/auth/password-reset/status`);
  if (!response.ok) throw new Error('status');
  return Boolean((await response.json())?.enabled);
}

export async function requestPosPasswordReset(identifier: string): Promise<string> {
  const { ok, data } = await post('/api/auth/forgot-password', { identifier, app: 'pos' });
  if (!ok) throw new Error(messageOf(data, 'Không gửi được yêu cầu đặt lại mật khẩu.'));
  return data?.message || 'Nếu tài khoản tồn tại, chúng tôi đã gửi link đặt lại mật khẩu.';
}

export async function resetPosPassword(token: string, newPassword: string): Promise<void> {
  const { ok, data } = await post('/api/auth/reset-password', { token, newPassword });
  if (!ok) throw new Error(messageOf(data, 'Đặt lại mật khẩu thất bại.'));
}

let cachedToken: string | null = null;

/** Đọc token sau dấu # rồi xóa khỏi thanh địa chỉ; giữ trong bộ nhớ để StrictMode gọi lại vẫn lấy được. */
export function takeResetTokenFromUrl(): string {
  const token = new URLSearchParams(window.location.hash.replace(/^#/, '')).get('token') || '';
  if (token) {
    cachedToken = token;
    window.history.replaceState(null, '', window.location.pathname);
  }
  return cachedToken || '';
}
