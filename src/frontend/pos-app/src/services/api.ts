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

// Real API client - No mock or demo fallback in production logic

/**
 * Đăng nhập thu ngân / nhân viên
 * Xử lý đầy đủ các mã lỗi HTTP 400, 401, 403, 500, lỗi mạng.
 * Không trả về undefined; không tự động fallback đăng nhập demo.
 */
export async function loginCashier(email: string, password: string) {
  let res: Response;
  try {
    res = await fetch(`${API_BASE_URL}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    });
  } catch (_netErr: any) {
    throw new Error('Không thể kết nối đến máy chủ Backend (Lỗi mạng hoặc server chưa khởi động).');
  }

  if (res.ok) {
    const data = await res.json().catch(() => null);
    if (!data || !data.accessToken) {
      throw new Error('Dữ liệu phản hồi từ máy chủ không hợp lệ (thiếu token).');
    }
    setAuthToken(data.accessToken);
    if (data.user) {
      setStoredUser({
        fullName: data.user.fullName || data.user.email || email,
        email: data.user.email || email,
        tenantName: data.user.tenantName,
      });
    } else {
      setStoredUser({
        fullName: email,
        email: email,
      });
    }
    return data;
  }

  // Xử lý các mã lỗi HTTP cụ thể
  if (res.status === 400 || res.status === 401) {
    const errData = await res.json().catch(() => ({}));
    const msg = errData.message || (errData.errors ? (Array.isArray(errData.errors) ? errData.errors.join(', ') : JSON.stringify(errData.errors)) : 'Email hoặc mật khẩu không chính xác.');
    throw new Error(msg);
  }

  if (res.status === 403) {
    throw new Error('Tài khoản bị khóa hoặc cửa hàng đang tạm ngưng hoạt động.');
  }

  if (res.status >= 500) {
    throw new Error(`Máy chủ gặp sự cố nội bộ (Mã lỗi ${res.status}). Vui lòng liên hệ quản trị viên.`);
  }

  throw new Error(`Đăng nhập thất bại (HTTP ${res.status}).`);
}

/**
 * Lấy danh sách Chi nhánh trực thuộc cửa hàng
 * Bỏ fallback dữ liệu mẫu; trả về danh sách thật hoặc mảng rỗng nếu lỗi.
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
 * Không dùng dữ liệu mẫu; trả về dữ liệu thật từ database.
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
 * Không dùng dữ liệu mẫu; ném lỗi khi không tìm thấy.
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
 * Ghi rõ: DTO đang đề xuất/chờ backend thống nhất contract.
 * Không giả lập thành công khi Backend chưa có endpoint hoặc trả lỗi.
 */
export async function checkoutPosOrder(request: PosCheckoutRequest): Promise<{ success: boolean; message: string; orderNumber?: string; receipt?: any; isPendingBackend?: boolean }> {
  try {
    // 1. Thử gọi route chuẩn Canonical: POST /api/pos/checkout
    let res = await fetch(`${API_BASE_URL}/api/pos/checkout`, {
      method: 'POST',
      headers: {
        ...getHeaders(),
        'Idempotency-Key': request.requestKey,
        'X-Request-Key': request.requestKey,
      },
      body: JSON.stringify(request),
    });

    // 2. Fallback sang Alias POST /api/orders/checkout nếu route canonical chưa có
    if (res.status === 404 || res.status === 405) {
      res = await fetch(`${API_BASE_URL}/api/orders/checkout`, {
        method: 'POST',
        headers: {
          ...getHeaders(),
          'Idempotency-Key': request.requestKey,
          'X-Request-Key': request.requestKey,
        },
        body: JSON.stringify(request),
      });
    }

    if (res.ok) {
      const data = await res.json().catch(() => ({}));
      return {
        success: true,
        message: data.message || 'Thanh toán đơn hàng thành công!',
        orderNumber: data.orderNumber || data.receipt?.orderNumber || data.number,
        receipt: data.receipt,
      };
    }

    if (res.status === 401) {
      removeAuthToken();
      window.dispatchEvent(new CustomEvent('pos-auth-expired'));
      throw new Error('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
    }

    if (res.status === 409) {
      const err = await res.json().catch(() => ({}));
      return {
        success: false,
        message: err.message || 'Xung đột dữ liệu: Tồn kho khả dụng không đủ do kênh online vừa giữ chỗ trước hoặc trùng lặp RequestKey.',
      };
    }

    if (res.status === 404 || res.status === 405 || res.status === 501) {
      return {
        success: false,
        isPendingBackend: true,
        message: 'API thanh toán (POST /api/pos/checkout và /api/orders/checkout) chưa được triển khai trên Backend Swagger. DTO checkout đề xuất đã được chuẩn bị đầy đủ và đang chờ thống nhất hợp đồng API giữa 2 bên.',
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
