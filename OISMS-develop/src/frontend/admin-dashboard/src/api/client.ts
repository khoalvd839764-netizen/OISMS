import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import type { ApiError } from '../types/api.types';

// Storage keys
export const TOKEN_STORAGE_KEY = 'oism_access_token';
export const REFRESH_TOKEN_STORAGE_KEY = 'oism_refresh_token';
export const USER_STORAGE_KEY = 'oism_user_profile';

const API_BASE_URL = import.meta.env.VITE_API_URL || '/api';

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 15000,
});

let isRefreshing = false;
let failedQueue: Array<{
  resolve: (value?: unknown) => void;
  reject: (reason?: unknown) => void;
}> = [];

const processQueue = (error: unknown, token: string | null = null) => {
  failedQueue.forEach((promise) => {
    if (error) {
      promise.reject(error);
    } else {
      promise.resolve(token);
    }
  });
  failedQueue = [];
};

// Request Interceptor: Attach Access Token
apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const token = localStorage.getItem(TOKEN_STORAGE_KEY);
    if (token && config.headers) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

interface BackendErrorPayload {
  message?: string;
  error?: string;
  errors?: string[] | Record<string, string[]>;
}

// Response Interceptor: Handle 401 Refresh, 403 Forbidden, Network Errors
apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<BackendErrorPayload>) => {
    const originalRequest = error.config as InternalAxiosRequestConfig & { _retry?: boolean };

    // 1. Network / Connection Errors
    if (!error.response) {
      window.dispatchEvent(
        new CustomEvent('oism:network-error', {
          detail: { message: 'Không thể kết nối đến máy chủ. Vui lòng kiểm tra mạng hoặc dịch vụ backend.' },
        })
      );
      const apiErr: ApiError = {
        code: 'NETWORK_ERROR',
        message: 'Lỗi kết nối máy chủ. Vui lòng kiểm tra đường truyền mạng hoặc trạng thái Backend API.',
      };
      return Promise.reject(apiErr);
    }

    const status = error.response.status;
    const responseData = error.response.data;

    // Extract human-readable message from either { message }, { errors: string[] }, or { errors: Record<string, string[]> }
    let extractedMessage = responseData?.message || responseData?.error;
    let flatErrors: string[] | undefined;
    if (responseData?.errors) {
      if (Array.isArray(responseData.errors)) {
        flatErrors = responseData.errors;
        if (!extractedMessage) extractedMessage = responseData.errors.join(' ');
      } else if (typeof responseData.errors === 'object') {
        flatErrors = Object.values(responseData.errors).flat();
        if (!extractedMessage) extractedMessage = flatErrors.join(' ');
      }
    }

    // 2. 403 Forbidden
    if (status === 403) {
      window.dispatchEvent(
        new CustomEvent('oism:forbidden', {
          detail: { message: extractedMessage || 'Bạn không có quyền thực hiện thao tác này.' },
        })
      );
      const apiErr: ApiError = {
        code: 'FORBIDDEN',
        status: 403,
        message: extractedMessage || 'Bạn không có quyền truy cập hoặc thực hiện chức năng này (403 Forbidden).',
      };
      return Promise.reject(apiErr);
    }

    // 3. 401 Unauthorized - Silent Refresh Token
    const isAuthEndpoint =
      originalRequest.url?.includes('/auth/login') ||
      originalRequest.url?.includes('/auth/register-tenant') ||
      originalRequest.url?.includes('/auth/refresh');

    if (status === 401 && !originalRequest._retry && !isAuthEndpoint) {
      const refreshToken = localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY);

      if (!refreshToken) {
        clearSession();
        window.dispatchEvent(new CustomEvent('oism:session-expired'));
        return Promise.reject({
          code: 'UNAUTHORIZED',
          status: 401,
          message: 'Phiên đăng nhập đã hết hạn.',
        } satisfies ApiError);
      }

      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          failedQueue.push({ resolve, reject });
        })
          .then((token) => {
            if (originalRequest.headers) {
              originalRequest.headers.Authorization = `Bearer ${token}`;
            }
            return apiClient(originalRequest);
          })
          .catch((err) => Promise.reject(err));
      }

      originalRequest._retry = true;
      isRefreshing = true;

      try {
        const refreshResponse = await axios.post(`${API_BASE_URL}/auth/refresh`, {
          refreshToken,
        });

        const { accessToken: newAccessToken, refreshToken: newRefreshToken, user } = refreshResponse.data;

        localStorage.setItem(TOKEN_STORAGE_KEY, newAccessToken);
        if (newRefreshToken) {
          localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, newRefreshToken);
        }
        if (user) {
          localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
        }

        apiClient.defaults.headers.common['Authorization'] = `Bearer ${newAccessToken}`;
        processQueue(null, newAccessToken);
        window.dispatchEvent(new CustomEvent('oism:auth-change'));

        if (originalRequest.headers) {
          originalRequest.headers.Authorization = `Bearer ${newAccessToken}`;
        }
        return apiClient(originalRequest);
      } catch (refreshErr) {
        processQueue(refreshErr, null);
        clearSession();
        window.dispatchEvent(new CustomEvent('oism:session-expired'));
        return Promise.reject({
          code: 'SESSION_EXPIRED',
          status: 401,
          message: 'Phiên làm việc đã hết hạn. Vui lòng đăng nhập lại.',
        } satisfies ApiError);
      } finally {
        isRefreshing = false;
      }
    }

    // 4. Other Standard HTTP Errors (400, 404, 409, 500, etc.)
    const apiError: ApiError & { errors?: string[] } = {
      status,
      code: error.code,
      message: extractedMessage || `Yêu cầu thất bại (HTTP ${status}).`,
      errors: flatErrors,
      details:
        responseData?.errors && !Array.isArray(responseData.errors)
          ? responseData.errors
          : undefined,
    };

    return Promise.reject(apiError);
  }
);

export function clearSession() {
  localStorage.removeItem(TOKEN_STORAGE_KEY);
  localStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY);
  localStorage.removeItem(USER_STORAGE_KEY);
  window.dispatchEvent(new CustomEvent('oism:auth-change'));
}

export function parseApiError(err: unknown): {
  message: string;
  errors?: string[];
  status?: number;
} {
  if (err && typeof err === 'object') {
    const obj = err as {
      message?: string;
      errors?: string[];
      status?: number;
      details?: Record<string, string[]>;
    };
    const errorsList =
      obj.errors ||
      (obj.details ? Object.values(obj.details).flat() : undefined);
    if (obj.message) {
      return {
        message: obj.message,
        errors: errorsList,
        status: obj.status,
      };
    }
  }
  if (err instanceof Error) {
    return { message: err.message };
  }
  return { message: 'Đã xảy ra lỗi không xác định khi giao tiếp với máy chủ.' };
}
