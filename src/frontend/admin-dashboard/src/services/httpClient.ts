import axios from 'axios';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000';

export const httpClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 15000,
});

// 1. Tự động đính kèm JWT Bearer Token vào mọi Request
httpClient.interceptors.request.use(
  (config) => {
    const token = localStorage.getItem('oism_access_token');
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// 2. Interceptor xử lý lỗi tập trung: 401 (Refresh Token), 403 (Forbidden), Lỗi mạng
httpClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    // Lỗi mạng hoặc server không phản hồi
    if (!error.response) {
      window.dispatchEvent(
        new CustomEvent('oism_network_error', {
          detail: 'Không thể kết nối đến máy chủ Backend (http://localhost:5000). Vui lòng kiểm tra lại dịch vụ.',
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
    if (requestUrl.includes('/api/auth/login') || requestUrl.includes('/api/auth/refresh-token') || requestUrl.includes('/api/auth/register-tenant')) {
      return Promise.reject(error);
    }

    // Lỗi 401: Token hết hạn -> Tự động Refresh Token
    if (error.response.status === 401 && !originalRequest._retry) {
      originalRequest._retry = true;
      const refreshToken = localStorage.getItem('oism_refresh_token');

      if (refreshToken) {
        try {
          const res = await axios.post(`${API_BASE_URL}/api/auth/refresh-token`, { refreshToken });
          const newAccessToken = res.data.accessToken;
          const newRefreshToken = res.data.refreshToken;
          localStorage.setItem('oism_access_token', newAccessToken);
          if (newRefreshToken) {
            localStorage.setItem('oism_refresh_token', newRefreshToken);
          }
          originalRequest.headers.Authorization = `Bearer ${newAccessToken}`;
          return httpClient(originalRequest);
        } catch {
          localStorage.removeItem('oism_access_token');
          localStorage.removeItem('oism_refresh_token');
          window.location.href = '/session-expired';
          return Promise.reject(error);
        }
      } else {
        localStorage.removeItem('oism_access_token');
        if (window.location.pathname !== '/login') {
          window.location.href = '/login';
        }
      }
    }

    return Promise.reject(error);
  }
);
