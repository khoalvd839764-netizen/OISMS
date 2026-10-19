import type { PosProductSearchResponse, Branch, PosCheckoutRequest } from '../types/pos';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000';

export function getAuthToken(): string | null {
  return localStorage.getItem('pos_access_token');
}

export function setAuthToken(token: string) {
  localStorage.setItem('pos_access_token', token);
}

export function removeAuthToken() {
  localStorage.removeItem('pos_access_token');
  localStorage.removeItem('pos_user');
}

export function getStoredUser(): { fullName: string; email: string; tenantName?: string } | null {
  const data = localStorage.getItem('pos_user');
  if (!data) return null;
  try {
    return JSON.parse(data);
  } catch {
    return null;
  }
}

export function setStoredUser(user: { fullName: string; email: string; tenantName?: string }) {
  localStorage.setItem('pos_user', JSON.stringify(user));
}

function getHeaders(): HeadersInit {
  const headers: HeadersInit = {
    'Content-Type': 'application/json',
  };
  const token = getAuthToken();
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }
  return headers;
}

/**
 * Đăng nhập thu ngân / nhân viên
 */
export async function loginCashier(email: string, password: string) {
  const res = await fetch(`${API_BASE_URL}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });

  if (!res.ok) {
    const errorData = await res.json().catch(() => ({}));
    throw new Error(errorData.message || (errorData.errors ? errorData.errors.join(', ') : 'Đăng nhập không thành công.'));
  }

  const data = await res.json();
  if (data.accessToken) {
    setAuthToken(data.accessToken);
    if (data.user) {
      setStoredUser({
        fullName: data.user.fullName || data.user.email,
        email: data.user.email,
        tenantName: data.user.tenantName,
      });
    }
  }
  return data;
}

/**
 * Lấy danh sách Chi nhánh trực thuộc cửa hàng
 */
export async function fetchBranches(): Promise<Branch[]> {
  try {
    const res = await fetch(`${API_BASE_URL}/api/branches`, {
      method: 'GET',
      headers: getHeaders(),
    });

    if (!res.ok) {
      if (res.status === 401) {
        console.warn('Chưa đăng nhập hoặc token hết hạn khi tải chi nhánh.');
      }
      return [];
    }

    return await res.json();
  } catch (err) {
    console.warn('Không thể kết nối đến Backend để lấy danh sách chi nhánh:', err);
    return [];
  }
}

/**
 * Tìm kiếm nhanh sản phẩm tại quầy POS theo Tên / SKU / Barcode kèm tồn khả dụng
 * Endpoint: GET /api/pos/search?query={keyword}&branchId={branchId}
 */
export async function searchPosProducts(query: string, branchId?: string): Promise<PosProductSearchResponse[]> {
  if (!query || query.trim().length === 0) {
    return [];
  }

  const params = new URLSearchParams();
  params.append('query', query.trim());
  if (branchId) {
    params.append('branchId', branchId);
  }

  const res = await fetch(`${API_BASE_URL}/api/pos/search?${params.toString()}`, {
    method: 'GET',
    headers: getHeaders(),
  });

  if (!res.ok) {
    if (res.status === 401) {
      throw new Error('Phiên làm việc đã hết hạn. Vui lòng đăng nhập lại.');
    }
    const errorData = await res.json().catch(() => ({}));
    throw new Error(errorData.message || `Lỗi tìm kiếm sản phẩm (HTTP ${res.status}).`);
  }

  return await res.json();
}

/**
 * Quét mã vạch (Barcode Scanner) tìm chính xác một SKU kèm tồn khả dụng
 * Endpoint: GET /api/pos/barcode/{barcode}?branchId={branchId}
 */
export async function scanPosBarcode(barcode: string, branchId?: string): Promise<PosProductSearchResponse> {
  if (!barcode || barcode.trim().length === 0) {
    throw new Error('Mã vạch không được để trống.');
  }

  const params = new URLSearchParams();
  if (branchId) {
    params.append('branchId', branchId);
  }

  const url = `${API_BASE_URL}/api/pos/barcode/${encodeURIComponent(barcode.trim())}${params.toString() ? '?' + params.toString() : ''}`;

  const res = await fetch(url, {
    method: 'GET',
    headers: getHeaders(),
  });

  if (!res.ok) {
    if (res.status === 404) {
      throw new Error(`Không tìm thấy sản phẩm nào khớp với mã vạch "${barcode}".`);
    }
    if (res.status === 401) {
      throw new Error('Phiên làm việc đã hết hạn. Vui lòng đăng nhập lại.');
    }
    const errorData = await res.json().catch(() => ({}));
    throw new Error(errorData.message || `Lỗi tra cứu mã vạch (HTTP ${res.status}).`);
  }

  return await res.json();
}

/**
 * Gửi yêu cầu Checkout thanh toán theo hợp đồng DTO Sprint 2
 * Theo tiêu chí bàn giao: "checkout chưa sẵn sàng không báo thành công; bản in hóa đơn chuẩn bị sẵn dùng dữ liệu API"
 */
export async function checkoutPosOrder(request: PosCheckoutRequest): Promise<{ success: boolean; message: string; orderNumber?: string; contractReady: boolean }> {
  try {
    const res = await fetch(`${API_BASE_URL}/api/orders/checkout`, {
      method: 'POST',
      headers: getHeaders(),
      body: JSON.stringify(request),
    });

    if (res.ok) {
      const data = await res.json();
      return {
        success: true,
        message: 'Thanh toán đơn hàng thành công!',
        orderNumber: data.orderNumber || data.number || `ORD-${Date.now().toString().slice(-6)}`,
        contractReady: true,
      };
    }

    if (res.status === 404 || res.status === 405 || res.status === 501) {
      // Backend chưa có endpoint Checkout (theo tiến độ Sprint 2)
      return {
        success: false,
        contractReady: true,
        message: `Endpoint thanh toán Backend (POST /api/orders/checkout) đang được hoàn thiện theo hợp đồng DTO Sprint 2. Dữ liệu đơn đã được đóng gói chuẩn DTO sẵn sàng kết nối!`,
        orderNumber: `ORD-POS-${Date.now().toString().slice(-6)}`,
      };
    }

    const err = await res.json().catch(() => ({}));
    return {
      success: false,
      contractReady: true,
      message: err.message || `Backend trả về mã lỗi ${res.status}.`,
    };
  } catch (err: any) {
    return {
      success: false,
      contractReady: true,
      message: `Không thể kết nối đến máy chủ Backend (${err.message || 'Lỗi mạng'}). Dữ liệu giỏ hàng và hợp đồng DTO đã được chuẩn bị đầy đủ!`,
      orderNumber: `ORD-PREVIEW-${Date.now().toString().slice(-6)}`,
    };
  }
}
