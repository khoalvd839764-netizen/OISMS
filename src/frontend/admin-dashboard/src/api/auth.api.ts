import { apiClient, TOKEN_STORAGE_KEY, REFRESH_TOKEN_STORAGE_KEY, USER_STORAGE_KEY, clearSession } from './client';
import type { AuthResponse, LoginRequest, RegisterTenantRequest, UserProfileDto } from '../types/auth.types';

export const authApi = {
  async login(payload: LoginRequest): Promise<AuthResponse> {
    const response = await apiClient.post<AuthResponse>('/auth/login', payload);
    const data = response.data;
    saveSession(data);
    return data;
  },

  async registerTenant(payload: RegisterTenantRequest): Promise<AuthResponse> {
    const response = await apiClient.post<AuthResponse>('/auth/register-tenant', payload);
    const data = response.data;
    if (data?.accessToken) {
      saveSession(data);
    }
    return data;
  },

  async refresh(refreshToken: string): Promise<AuthResponse> {
    const response = await apiClient.post<AuthResponse>('/auth/refresh', { refreshToken });
    const data = response.data;
    saveSession(data);
    return data;
  },

  async logout(): Promise<void> {
    const refreshToken = localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY);
    try {
      if (refreshToken) {
        await apiClient.post('/auth/logout', { refreshToken });
      }
    } finally {
      clearSession();
    }
  },

  async getMe(): Promise<UserProfileDto> {
    const response = await apiClient.get<UserProfileDto>('/auth/me');
    const user = response.data;
    localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
    window.dispatchEvent(new CustomEvent('oism:auth-change'));
    return user;
  },
};

export function saveSession(auth: AuthResponse) {
  localStorage.setItem(TOKEN_STORAGE_KEY, auth.accessToken);
  localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, auth.refreshToken);
  localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(auth.user));
  window.dispatchEvent(new CustomEvent('oism:auth-change'));
}
