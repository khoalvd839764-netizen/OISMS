import axios, { type InternalAxiosRequestConfig } from 'axios';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000';

const ACCESS_TOKEN_KEY = 'oism_access_token';
const REFRESH_TOKEN_KEY = 'oism_refresh_token';
const TENANT_ID_KEY = 'oism_tenant_id';

export const httpClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 15000,
});

type RetriableRequest = InternalAxiosRequestConfig & { _retry?: boolean };

// [A06] Quản lý khóa đồng bộ Refresh Token (chống Race Condition khi nhiều request cùng gặp 401)
// - isRefreshing: chỉ cho phép đúng 1 lời gọi POST /api/auth/refresh-token tại một thời điểm.
// - failedQueue: các request 401 đến sau sẽ xếp hàng chờ newAccessToken rồi tự động gửi lại.
let isRefreshing = false;
let failedQueue: Array<{
  resolve: (token: string) => void;
  reject: (error: unknown) => void;
}> = [];

function processQueue(error: unknown, token: string | null = null) {
  const queue = failedQueue;
  failedQueue = [];
  queue.forEach((prom) => {
    if (error || !token) {
      prom.reject(error ?? new Error('Không nhận được access token mới.'));
    } else {
      prom.resolve(token);
    }
  });
}

/**
 * [A01] Dọn sạch mọi dữ liệu tạm theo phiên/tenant trong trình duyệt:
 * - sessionStorage: "oism_session_orders" (khóa cũ) và "oism_orders_${tenantId}".
 * - localStorage: cache catalog POS "oism_pos_catalog_${tenantId}".
 */
export function clearSessionOrdersStorage() {
  sessionStorage.removeItem('oism_session_orders');

  const sessionKeys: string[] = [];
  for (let i = 0; i < sessionStorage.length; i++) {
    const k = sessionStorage.key(i);
    if (k && (k.startsWith('oism_orders_') || k.startsWith('oism_session_orders'))) {
      sessionKeys.push(k);
    }
  }
  sessionKeys.forEach((k) => sessionStorage.removeItem(k));

  const localKeys: string[] = [];
  for (let i = 0; i < localStorage.length; i++) {
    const k = localStorage.key(i);
    if (k && k.startsWith('oism_pos_catalog_')) {
      localKeys.push(k);
    }
  }
  localKeys.forEach((k) => localStorage.removeItem(k));
}

/** Xóa toàn bộ token + dữ liệu phiên (dùng chung cho logout / refresh thất bại). */
export function clearAuthSession() {
  localStorage.removeItem(ACCESS_TOKEN_KEY);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
  localStorage.removeItem(TENANT_ID_KEY);
  sessionStorage.removeItem('oism_session_orders');
  delete httpClient.defaults.headers.common.Authorization;
  clearSessionOrdersStorage();
}

function extractBearer(config?: InternalAxiosRequestConfig): string | null {
  const raw =
    (typeof (config?.headers as any)?.get === 'function' ? (config?.headers as any).get('Authorization') : null) ??
    config?.headers?.Authorization ??
    config?.headers?.authorization;
  if (typeof raw !== 'string') return null;
  return raw.startsWith('Bearer ') ? raw.slice(7) : raw;
}

export function setAuthHeader(config: InternalAxiosRequestConfig, token: string) {
  if (config.headers) {
    if (typeof (config.headers as any).set === 'function') {
      (config.headers as any).set('Authorization', `Bearer ${token}`);
    } else {
      config.headers.Authorization = `Bearer ${token}`;
    }
  }
}

/**
 * [A06] Làm mới access token với khóa đơn (single-flight).
 * Nếu đang có một lượt refresh chạy, lời gọi sau chỉ xếp hàng chờ kết quả, KHÔNG gửi thêm request
 * refresh thứ hai (tránh làm hỏng cơ chế Rotation nguyên tử của Backend).
 */
export function refreshAccessToken(): Promise<string> {
  if (isRefreshing) {
    return new Promise<string>((resolve, reject) => {
      failedQueue.push({ resolve, reject });
    });
  }

  const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
  if (!refreshToken) {
    return Promise.reject(new Error('Không có refresh token.'));
  }

  isRefreshing = true;

  return (async () => {
    try {
      // Dùng axios gốc (không qua interceptor) để tránh vòng lặp 401 vô hạn
      const res = await axios.post(`${API_BASE_URL}/api/auth/refresh-token`, { refreshToken }, { timeout: 15000 });
      const newAccessToken: string | undefined = res.data?.accessToken;
      const newRefreshToken: string | undefined = res.data?.refreshToken;
      if (!newAccessToken) {
        throw new Error('Phản hồi refresh-token không chứa accessToken.');
      }

      localStorage.setItem(ACCESS_TOKEN_KEY, newAccessToken);
      if (newRefreshToken) {
        localStorage.setItem(REFRESH_TOKEN_KEY, newRefreshToken);
      }
      httpClient.defaults.headers.common.Authorization = `Bearer ${newAccessToken}`;

      processQueue(null, newAccessToken);
      return newAccessToken;
    } catch (refreshErr) {
      // Trường hợp tab khác đã xoay vòng (rotate) token thành công trong lúc tab này đang refresh:
      // refresh token trong localStorage đã đổi → dùng luôn access token mới thay vì đăng xuất người dùng.
      const latestRefresh = localStorage.getItem(REFRESH_TOKEN_KEY);
      const latestAccess = localStorage.getItem(ACCESS_TOKEN_KEY);
      if (latestRefresh && latestRefresh !== refreshToken && latestAccess) {
        processQueue(null, latestAccess);
        return latestAccess;
      }

      processQueue(refreshErr, null);
      throw refreshErr;
    } finally {
      isRefreshing = false;
    }
  })();
}

// 1. Tự động đính kèm JWT Bearer Token vào mọi Request
httpClient.interceptors.request.use(
  (config) => {
    const token = localStorage.getItem(ACCESS_TOKEN_KEY);
    if (token) {
      setAuthHeader(config, token);
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// 2. Interceptor xử lý lỗi tập trung: 401 (Refresh Token với Queue), 403 (Forbidden), Lỗi mạng
httpClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config as RetriableRequest | undefined;

    // Lỗi mạng hoặc server không phản hồi
    if (!error.response) {
      window.dispatchEvent(
        new CustomEvent('oism_network_error', {
          detail: `Không thể kết nối đến máy chủ Backend (${API_BASE_URL}). Vui lòng kiểm tra lại dịch vụ.`,
        })
      );
      return Promise.reject(error);
    }

    // Lỗi 403: Không có quyền truy cập
    if (error.response.status === 403) {
      if (window.location.pathname !== '/403') {
        window.location.href = '/403';
      }
      return Promise.reject(error);
    }

    // Bỏ qua can thiệp 401 đối với các endpoint xác thực (để trả đúng lỗi mật khẩu/token sai về form)
    const requestUrl = originalRequest?.url || '';
    if (
      requestUrl.includes('/api/auth/login') ||
      requestUrl.includes('/api/auth/refresh-token') ||
      requestUrl.includes('/api/auth/register-tenant') ||
      requestUrl.includes('/api/auth/logout')
    ) {
      return Promise.reject(error);
    }

    // [A06] Lỗi 401: Token hết hạn -> Tự động Refresh Token có hàng đợi chống Race Condition
    if (error.response.status === 401 && originalRequest && !originalRequest._retry) {
      originalRequest._retry = true;

      // 1. Request này được gửi bằng token cũ nhưng một lượt refresh khác đã hoàn tất trước đó
      // → chỉ cần gửi lại với access token hiện tại, KHÔNG refresh thêm lần nữa.
      const usedToken = extractBearer(originalRequest);
      const currentToken = localStorage.getItem(ACCESS_TOKEN_KEY);
      if (!isRefreshing && currentToken && usedToken && usedToken !== currentToken) {
        setAuthHeader(originalRequest, currentToken);
        return httpClient(originalRequest);
      }

      // 2. Nếu đang có một lượt refresh token chạy: đưa vào failedQueue xếp hàng chờ token mới
      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          failedQueue.push({
            resolve: (token: string) => {
              setAuthHeader(originalRequest, token);
              resolve(httpClient(originalRequest));
            },
            reject: (err: unknown) => {
              reject(err);
            },
          });
        });
      }

      if (!localStorage.getItem(REFRESH_TOKEN_KEY)) {
        clearAuthSession();
        if (window.location.pathname !== '/login') {
          window.location.href = '/login';
        }
        return Promise.reject(error);
      }

      try {
        const newToken = await refreshAccessToken();
        setAuthHeader(originalRequest, newToken);
        return httpClient(originalRequest);
      } catch (refreshErr) {
        clearAuthSession();
        if (window.location.pathname !== '/session-expired') {
          window.location.href = '/session-expired';
        }
        return Promise.reject(refreshErr);
      }
    }

    return Promise.reject(error);
  }
);
