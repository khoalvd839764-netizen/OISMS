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

// [BF-036] Các endpoint xác thực chưa có phiên hợp lệ: không được gửi kèm access token cũ.
// Token cũ còn hạn của tenant khác làm backend xác định sai tenant và từ chối đăng nhập.
const ANONYMOUS_AUTH_PATHS = [
  '/api/auth/login',
  '/api/auth/refresh-token',
  '/api/auth/register-tenant',
  '/api/auth/logout',
];

export function isAnonymousAuthUrl(url?: string): boolean {
  if (!url) return false;
  return ANONYMOUS_AUTH_PATHS.some((path) => url.includes(path));
}

function removeAuthHeader(config: InternalAxiosRequestConfig) {
  if (!config.headers) return;
  if (typeof (config.headers as any).delete === 'function') {
    (config.headers as any).delete('Authorization');
  } else {
    delete (config.headers as any).Authorization;
    delete (config.headers as any).authorization;
  }
}

// [A06] Quản lý khóa đồng bộ Refresh Token (chống Race Condition khi nhiều request cùng gặp 401)
// - isRefreshing: chỉ cho phép đúng 1 lời gọi POST /api/auth/refresh-token tại một thời điểm.
// - failedQueue: các request 401 đến sau sẽ xếp hàng chờ newAccessToken rồi tự động gửi lại.
let isRefreshing = false;
let failedQueue: Array<{
  generation: number;
  resolve: (token: string) => void;
  reject: (error: unknown) => void;
}> = [];

// [BF-001] Quản lý generation phiên và chuỗi token thuộc cùng một phiên đăng nhập
let currentAuthGeneration = 0;
const sessionPreviousTokens = new Set<string>();

function processQueue(error: unknown, token: string | null = null, targetGen = currentAuthGeneration) {
  const queue = failedQueue;
  failedQueue = [];
  queue.forEach((prom) => {
    if (error || !token || prom.generation !== targetGen) {
      prom.reject(error ?? new Error('Phiên làm việc đã thay đổi hoặc không nhận được access token mới.'));
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
  currentAuthGeneration++;
  sessionPreviousTokens.clear();
  isRefreshing = false;
  failedQueue.forEach((prom) => {
    prom.reject(new Error('Phiên làm việc đã kết thúc.'));
  });
  failedQueue = [];

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
 * [A06, BF-001] Làm mới access token với khóa đơn (single-flight) và ràng buộc chủ sở hữu phiên.
 * Nếu đang có một lượt refresh chạy, lời gọi sau chỉ xếp hàng chờ kết quả.
 * Nếu phiên đăng nhập đổi sang tài khoản khác trước khi refresh cũ trả về, hủy bỏ token cũ.
 */
export function refreshAccessToken(): Promise<string> {
  const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
  const currentAccess = localStorage.getItem(ACCESS_TOKEN_KEY);
  if (!refreshToken) {
    return Promise.reject(new Error('Không có refresh token.'));
  }

  if (isRefreshing) {
    const queueGen = currentAuthGeneration;
    return new Promise<string>((resolve, reject) => {
      failedQueue.push({ generation: queueGen, resolve, reject });
    });
  }

  const startGeneration = currentAuthGeneration;
  const startRefreshToken = refreshToken;
  const startAccessToken = currentAccess;
  isRefreshing = true;

  return (async () => {
    try {
      // Dùng axios gốc (không qua interceptor) để tránh vòng lặp 401 vô hạn
      const res = await axios.post(`${API_BASE_URL}/api/auth/refresh-token`, { refreshToken: startRefreshToken }, { timeout: 15000 });

      // [BF-001] Kiểm tra xem phiên có bị đổi (logout/login tài khoản khác) trong lúc request đang gửi không
      const latestRefreshAfter = localStorage.getItem(REFRESH_TOKEN_KEY);
      const latestAccessAfter = localStorage.getItem(ACCESS_TOKEN_KEY);
      if (
        startGeneration !== currentAuthGeneration ||
        latestRefreshAfter !== startRefreshToken ||
        (startAccessToken && latestAccessAfter && latestAccessAfter !== startAccessToken)
      ) {
        const staleErr: any = new Error('Phiên đăng nhập đã thay đổi trong lúc làm mới token. Bỏ qua token cũ.');
        staleErr.isStaleSession = true;
        throw staleErr;
      }

      const newAccessToken: string | undefined = res.data?.accessToken;
      const newRefreshToken: string | undefined = res.data?.refreshToken;
      if (!newAccessToken) {
        throw new Error('Phản hồi refresh-token không chứa accessToken.');
      }

      if (startAccessToken) {
        sessionPreviousTokens.add(startAccessToken);
      }

      localStorage.setItem(ACCESS_TOKEN_KEY, newAccessToken);
      if (newRefreshToken) {
        localStorage.setItem(REFRESH_TOKEN_KEY, newRefreshToken);
      }
      httpClient.defaults.headers.common.Authorization = `Bearer ${newAccessToken}`;

      processQueue(null, newAccessToken, startGeneration);
      return newAccessToken;
    } catch (refreshErr: any) {
      if (
        refreshErr?.isStaleSession ||
        startGeneration !== currentAuthGeneration ||
        localStorage.getItem(REFRESH_TOKEN_KEY) !== startRefreshToken
      ) {
        if (refreshErr && typeof refreshErr === 'object') {
          refreshErr.isStaleSession = true;
        }
        processQueue(refreshErr, null, startGeneration);
        throw refreshErr;
      }

      processQueue(refreshErr, null, startGeneration);
      throw refreshErr;
    } finally {
      isRefreshing = false;
    }
  })();
}

// 1. Tự động đính kèm JWT Bearer Token vào mọi Request, trừ endpoint đăng nhập/đăng ký/làm mới token
httpClient.interceptors.request.use(
  (config) => {
    if (isAnonymousAuthUrl(config.url)) {
      removeAuthHeader(config);
      return config;
    }
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
    if (isAnonymousAuthUrl(originalRequest?.url)) {
      return Promise.reject(error);
    }

    // [A06, BF-001] Lỗi 401: Token hết hạn -> Tự động Refresh Token có hàng đợi chống Race Condition
    if (error.response.status === 401 && originalRequest && !originalRequest._retry) {
      originalRequest._retry = true;

      // 1. Request này được gửi bằng token cũ nhưng một lượt refresh khác đã hoàn tất trước đó
      const usedToken = extractBearer(originalRequest);
      const currentToken = localStorage.getItem(ACCESS_TOKEN_KEY);
      if (usedToken && currentToken && usedToken !== currentToken) {
        // [BF-001 Kịch bản 2] Kiểm tra chủ sở hữu ý định:
        // Nếu usedToken không thuộc chuỗi token của cùng phiên đăng nhập (ví dụ: người dùng A gửi request,
        // sau đó người dùng B đăng nhập), TUYỆT ĐỐI KHÔNG replay mutation hay request của A bằng token B.
        const isSameSession = sessionPreviousTokens.has(usedToken);
        if (!isSameSession) {
          return Promise.reject(
            new Error('Yêu cầu thuộc về phiên đăng nhập trước đó đã kết thúc. Từ chối gửi lại tự động.')
          );
        }

        setAuthHeader(originalRequest, currentToken);
        return httpClient(originalRequest);
      }

      // 2. Nếu đang có một lượt refresh token chạy: đưa vào failedQueue xếp hàng chờ token mới
      const reqGeneration = currentAuthGeneration;
      const reqRefreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          failedQueue.push({
            generation: reqGeneration,
            resolve: (token: string) => {
              if (reqGeneration !== currentAuthGeneration) {
                reject(new Error('Phiên làm việc đã thay đổi.'));
                return;
              }
              setAuthHeader(originalRequest, token);
              resolve(httpClient(originalRequest));
            },
            reject: (err: unknown) => {
              reject(err);
            },
          });
        });
      }

      if (!reqRefreshToken) {
        clearAuthSession();
        if (window.location.pathname !== '/login') {
          window.location.href = '/login';
        }
        return Promise.reject(error);
      }

      try {
        const newToken = await refreshAccessToken();
        if (reqGeneration !== currentAuthGeneration) {
          return Promise.reject(new Error('Phiên làm việc đã thay đổi.'));
        }
        setAuthHeader(originalRequest, newToken);
        return httpClient(originalRequest);
      } catch (refreshErr: any) {
        // [BF-001] Chỉ xóa phiên khi lỗi thuộc đúng phiên hiện tại (không xóa nhầm phiên mới B vừa đăng nhập)
        if (
          !refreshErr?.isStaleSession &&
          reqGeneration === currentAuthGeneration &&
          localStorage.getItem(REFRESH_TOKEN_KEY) === reqRefreshToken
        ) {
          clearAuthSession();
          if (window.location.pathname !== '/session-expired') {
            window.location.href = '/session-expired';
          }
        }
        return Promise.reject(refreshErr);
      }
    }

    return Promise.reject(error);
  }
);
