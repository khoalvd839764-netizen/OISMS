import type { PosProductSearchResponse, Branch, PosCheckoutRequest } from '../types/pos';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000';

export function getAuthToken(): string | null {
  return localStorage.getItem('oism_access_token') || localStorage.getItem('pos_access_token');
}

export function setAuthToken(token: string) {
  localStorage.setItem('oism_access_token', token);
  localStorage.setItem('pos_access_token', token);
}

export function removeAuthToken() {
  localStorage.removeItem('oism_access_token');
  localStorage.removeItem('pos_access_token');
  localStorage.removeItem('pos_user');
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
 * Lấy danh sách Chi nhánh trực thuộc cửa hàng
 */
export async function fetchBranches(): Promise<Branch[]> {
  try {
    const res = await fetch(`${API_BASE_URL}/api/branches`, {
      method: 'GET',
      headers: getHeaders(),
    });

    if (res.status === 401) {
      removeAuthToken();
      window.dispatchEvent(new CustomEvent('pos-auth-expired'));
      return [];
    }

    if (res.ok) {
      const data = await res.json();
      if (Array.isArray(data)) {
        return data;
      }
    }
  } catch (err) {
    console.error('Lỗi kết nối khi lấy danh sách chi nhánh:', err);
  }

  return [];
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

  try {
    const res = await fetch(`${API_BASE_URL}/api/pos/search?${params.toString()}`, {
      method: 'GET',
      headers: getHeaders(),
    });

    if (res.status === 401) {
      removeAuthToken();
      window.dispatchEvent(new CustomEvent('pos-auth-expired'));
      throw new Error('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
    }

    if (res.ok) {
      const data = await res.json();
      return Array.isArray(data) ? data : [];
    }

    if (res.status === 404) {
      return [];
    }

    const err = await res.json().catch(() => ({}));
    throw new Error(err.message || `Lỗi máy chủ khi tra cứu (HTTP ${res.status})`);
  } catch (err: any) {
    if (err.message?.includes('Phiên đăng nhập đã hết hạn')) {
      throw err;
    }
    console.error('Lỗi tra cứu sản phẩm:', err);
    throw new Error(err.message || 'Không thể kết nối đến máy chủ để tìm kiếm sản phẩm.');
  }
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

  let res: Response;
  try {
    res = await fetch(url, {
      method: 'GET',
      headers: getHeaders(),
    });
  } catch (_err: any) {
    throw new Error('Không thể kết nối đến máy chủ khi quét mã vạch.');
  }

  if (res.status === 401) {
    removeAuthToken();
    window.dispatchEvent(new CustomEvent('pos-auth-expired'));
    throw new Error('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
  }

  if (res.ok) {
    const data = await res.json();
    return data;
  }

  if (res.status === 404) {
    throw new Error(`Không tìm thấy sản phẩm nào khớp với mã vạch "${barcode}".`);
  }

  const err = await res.json().catch(() => ({}));
  throw new Error(err.message || `Lỗi máy chủ khi quét mã (HTTP ${res.status}).`);
}

/**
 * Gửi yêu cầu Checkout thanh toán theo đề xuất DTO POS
 */
export async function checkoutPosOrder(request: PosCheckoutRequest): Promise<{ success: boolean; message: string; orderNumber?: string; isPendingBackend?: boolean }> {
  try {
    const res = await fetch(`${API_BASE_URL}/api/orders/checkout`, {
      method: 'POST',
      headers: getHeaders(),
      body: JSON.stringify(request),
    });

    if (res.ok) {
      const data = await res.json().catch(() => ({}));
      return {
        success: true,
        message: 'Thanh toán đơn hàng thành công!',
        orderNumber: data.orderNumber || data.number,
      };
    }

    if (res.status === 404 || res.status === 405 || res.status === 501) {
      // Backend chưa có endpoint POST /api/orders/checkout
      return {
        success: false,
        isPendingBackend: true,
        message: 'API thanh toán (POST /api/orders/checkout) chưa được triển khai trên Backend Swagger. DTO checkout đề xuất đã được chuẩn bị đầy đủ và đang chờ thống nhất hợp đồng API giữa 2 bên.',
      };
    }

    const err = await res.json().catch(() => ({}));
    return {
      success: false,
      message: err.message || `Thanh toán thất bại (Mã lỗi ${res.status}).`,
    };
  } catch (err: any) {
    return {
      success: false,
      message: `Không thể kết nối đến máy chủ để thực hiện thanh toán (${err.message || 'Lỗi mạng'}). Giỏ hàng được giữ nguyên.`,
    };
  }
}
