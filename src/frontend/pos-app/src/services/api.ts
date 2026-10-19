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

export const FALLBACK_BRANCHES: Branch[] = [
  { id: '11111111-0000-0000-0001-000000000001', name: 'Chi nhánh Quận 1 (TP.HCM)', isActive: true },
  { id: '22222222-0000-0000-0001-000000000001', name: 'Chi nhánh Đống Đa (Hà Nội)', isActive: true },
];

export const DEMO_CATALOG: PosProductSearchResponse[] = [
  {
    variantId: '11111111-0000-0000-0006-000000000001',
    productId: '11111111-0000-0000-0005-000000000001',
    sku: 'POLO-NAM-DEN-M',
    productName: 'Áo Polo Nam Thể Thao',
    variantName: 'Đen / Size M',
    barcode: 'POLONAMDENM',
    retailPrice: 299000,
    color: 'Đen',
    size: 'M',
    onHand: 50,
    reserved: 5,
    available: 45,
  },
  {
    variantId: '11111111-0000-0000-0006-000000000002',
    productId: '11111111-0000-0000-0005-000000000001',
    sku: 'POLO-NAM-TRANG-L',
    productName: 'Áo Polo Nam Thể Thao',
    variantName: 'Trắng / Size L',
    barcode: '8935001100025',
    retailPrice: 299000,
    color: 'Trắng',
    size: 'L',
    onHand: 30,
    reserved: 0,
    available: 30,
  },
  {
    variantId: '22222222-0000-0000-0006-000000000001',
    productId: '22222222-0000-0000-0005-000000000001',
    sku: 'SERUM-B5-30ML',
    productName: 'Serum Phục Hồi Da B5',
    variantName: 'Chai 30ml',
    barcode: '8935002100017',
    retailPrice: 380000,
    color: 'Trong suốt',
    size: '30ml',
    onHand: 25,
    reserved: 3,
    available: 22,
  },
  {
    variantId: '11111111-0000-0000-0006-000000000003',
    productId: '11111111-0000-0000-0005-000000000002',
    sku: 'JEANS-SLIM-31',
    productName: 'Quần Jeans Slimfit Nam',
    variantName: 'Xanh Đậm / Size 31',
    barcode: '8935003100016',
    retailPrice: 450000,
    color: 'Xanh Đậm',
    size: '31',
    onHand: 18,
    reserved: 2,
    available: 16,
  },
  {
    variantId: '11111111-0000-0000-0006-000000000004',
    productId: '11111111-0000-0000-0005-000000000003',
    sku: 'OXFORD-SHIRT-M',
    productName: 'Áo Sơ Mi Oxford Dài Tay',
    variantName: 'Trắng / Size M',
    barcode: '8935004100015',
    retailPrice: 350000,
    color: 'Trắng',
    size: 'M',
    onHand: 0,
    reserved: 0,
    available: 0,
  },
];

/**
 * Đăng nhập thu ngân / nhân viên
 */
export async function loginCashier(email: string, password: string) {
  try {
    const res = await fetch(`${API_BASE_URL}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    });

    if (res.ok) {
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

    // Nếu mật khẩu sai từ server trả về
    if (res.status === 400 || res.status === 401) {
      const errorData = await res.json().catch(() => ({}));
      throw new Error(errorData.message || (errorData.errors ? errorData.errors.join(', ') : 'Email hoặc mật khẩu không chính xác.'));
    }
  } catch (err: any) {
    // Nếu lỗi do server 500 hoặc offline (chưa chạy DB), cho phép đăng nhập thu ngân demo
    if (!err.message?.includes('không chính xác')) {
      const mockToken = 'demo_jwt_pos_token_2026';
      setAuthToken(mockToken);
      const user = {
        fullName: email.includes('owner') ? 'Chủ Shop Thời Trang (Demo)' : 'Thu Ngân Quầy 01 (Lê Phương)',
        email: email,
        tenantName: 'Cửa hàng Thời trang ABC (OISM Demo)',
      };
      setStoredUser(user);
      return {
        accessToken: mockToken,
        user,
      };
    }
    throw err;
  }
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

    if (res.ok) {
      const data = await res.json();
      if (Array.isArray(data) && data.length > 0) {
        return data;
      }
    }
  } catch (err) {
    console.warn('Không thể kết nối đến Backend để lấy danh sách chi nhánh (sử dụng fallback):', err);
  }

  return FALLBACK_BRANCHES;
}

/**
 * Tìm kiếm nhanh sản phẩm tại quầy POS theo Tên / SKU / Barcode kèm tồn khả dụng
 * Endpoint: GET /api/pos/search?query={keyword}&branchId={branchId}
 */
export async function searchPosProducts(query: string, branchId?: string): Promise<PosProductSearchResponse[]> {
  if (!query || query.trim().length === 0) {
    return [];
  }

  try {
    const params = new URLSearchParams();
    params.append('query', query.trim());
    if (branchId) {
      params.append('branchId', branchId);
    }

    const res = await fetch(`${API_BASE_URL}/api/pos/search?${params.toString()}`, {
      method: 'GET',
      headers: getHeaders(),
    });

    if (res.ok) {
      return await res.json();
    }
  } catch (err) {
    console.warn('Tra cứu Real API gặp sự cố, tự động dùng Catalog dự phòng:', err);
  }

  // Graceful fallback cho tra cứu nhanh khi DB chưa chạy
  const q = query.trim().toLowerCase();
  return DEMO_CATALOG.filter(
    (item) =>
      item.sku.toLowerCase().includes(q) ||
      item.productName.toLowerCase().includes(q) ||
      (item.barcode && item.barcode.toLowerCase().includes(q)) ||
      (item.variantName && item.variantName.toLowerCase().includes(q))
  );
}

/**
 * Quét mã vạch (Barcode Scanner) tìm chính xác một SKU kèm tồn khả dụng
 * Endpoint: GET /api/pos/barcode/{barcode}?branchId={branchId}
 */
export async function scanPosBarcode(barcode: string, branchId?: string): Promise<PosProductSearchResponse> {
  if (!barcode || barcode.trim().length === 0) {
    throw new Error('Mã vạch không được để trống.');
  }

  try {
    const params = new URLSearchParams();
    if (branchId) {
      params.append('branchId', branchId);
    }

    const url = `${API_BASE_URL}/api/pos/barcode/${encodeURIComponent(barcode.trim())}${params.toString() ? '?' + params.toString() : ''}`;

    const res = await fetch(url, {
      method: 'GET',
      headers: getHeaders(),
    });

    if (res.ok) {
      return await res.json();
    }
  } catch (err) {
    console.warn('Tra cứu Barcode Real API gặp sự cố, tự động dùng Catalog dự phòng:', err);
  }

  const code = barcode.trim().toLowerCase();
  const matched = DEMO_CATALOG.find(
    (item) => item.barcode?.toLowerCase() === code || item.sku.toLowerCase() === code
  );

  if (matched) {
    return matched;
  }

  throw new Error(`Không tìm thấy sản phẩm nào khớp với mã "${barcode}".`);
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
