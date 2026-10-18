export type Role = 'Owner' | 'Staff' | 'Cashier';

export interface UserProfileDto {
  id: string;
  tenantId: string;
  tenantCode: string;
  tenantName: string;
  email: string;
  fullName: string;
  phoneNumber?: string;
  roles: Role[];
}

export interface AuthResponse {
  tokenType: string;
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  user: UserProfileDto;
}

export interface LoginRequest {
  email: string;
  password: string;
  tenantCode?: string;
}

export interface RegisterTenantRequest {
  tenantName: string;
  tenantCode: string;
  fullName: string;
  email: string;
  phoneNumber?: string;
  password: string;
}

export interface RefreshTokenRequest {
  refreshToken: string;
}
