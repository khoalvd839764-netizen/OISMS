import type {
  PosProductSearchResponse,
  Branch,
  PosCheckoutRequest,
  PosUser,
  CheckoutResult,
  PendingCheckoutRecord,
  PendingCheckoutStatus,
  CheckoutOwner,
  PosReceiptDto,
  ReceiptData,
  ReceiptItemData,
} from '../types/pos';

export interface ReceiptMapperSupplementary {
  tenantName?: string;
  fallbackCashierName?: string;
  fallbackBranchName?: string;
}

/**
 * B01-F02: Kiểm tra tính hợp lệ và cấu trúc của dữ liệu receipt snapshot từ backend theo đúng contract.
 * Không chấp nhận snapshot rỗng, items chứa object rỗng ({}), hoặc sai kiểu dữ liệu (vd: branchAddress: 42).
 */
export function validatePosReceiptSnapshot(
  dto: any
): { valid: boolean; reason?: string } {
  if (!dto || typeof dto !== 'object' || Array.isArray(dto)) {
    return { valid: false, reason: 'Dữ liệu receipt từ máy chủ phải là một đối tượng (object) hợp lệ.' };
  }

  const orderNum = dto.orderNumber || dto.receiptNumber;
  if (!orderNum || typeof orderNum !== 'string' || orderNum.trim().length === 0) {
    return { valid: false, reason: 'Mã hóa đơn/đơn hàng (orderNumber/receiptNumber) bị thiếu hoặc không đúng định dạng chuỗi.' };
  }

  if (typeof dto.totalAmount !== 'number' || !Number.isFinite(dto.totalAmount) || dto.totalAmount < 0) {
    return { valid: false, reason: 'Tổng tiền đơn hàng (totalAmount) phải là số dương hoặc 0 hợp lệ.' };
  }

  if (!dto.createdAt || typeof dto.createdAt !== 'string' || dto.createdAt.trim().length === 0 || isNaN(Date.parse(dto.createdAt))) {
    return { valid: false, reason: 'Thời gian tạo hóa đơn (createdAt) từ máy chủ bị thiếu hoặc không đúng định dạng ngày tháng hợp lệ.' };
  }

  if (!Array.isArray(dto.items) || dto.items.length === 0) {
    return { valid: false, reason: 'Danh sách mặt hàng (items) trong hóa đơn bị rỗng hoặc không phải mảng.' };
  }

  for (let i = 0; i < dto.items.length; i++) {
    const item = dto.items[i];
    if (!item || typeof item !== 'object' || Array.isArray(item)) {
      return { valid: false, reason: `Mặt hàng thứ ${i + 1} không hợp lệ (không phải object).` };
    }

    const hasName = typeof item.productName === 'string' && item.productName.trim().length > 0;
    const hasSku = typeof item.skuCode === 'string' && item.skuCode.trim().length > 0;
    if (!hasName && !hasSku) {
      return { valid: false, reason: `Mặt hàng thứ ${i + 1} bị thiếu cả tên sản phẩm lẫn mã SKU (dữ liệu rỗng).` };
    }

    if (typeof item.quantity !== 'number' || !Number.isFinite(item.quantity) || item.quantity <= 0) {
      return { valid: false, reason: `Số lượng (quantity) của mặt hàng "${item.productName || item.skuCode}" phải lớn hơn 0.` };
    }

    if (typeof item.unitPrice !== 'number' || !Number.isFinite(item.unitPrice) || item.unitPrice < 0) {
      return { valid: false, reason: `Đơn giá (unitPrice) của mặt hàng "${item.productName || item.skuCode}" phải là số không âm.` };
    }

    if (item.subtotal != null) {
      if (typeof item.subtotal !== 'number' || !Number.isFinite(item.subtotal) || item.subtotal < 0) {
        return { valid: false, reason: `Thành tiền (subtotal) của mặt hàng "${item.productName || item.skuCode}" không hợp lệ.` };
      }
    }
  }

  // Kiểm tra kiểu dữ liệu các trường metadata nếu có (phải là string)
  const stringFields = [
    { key: 'branchName', label: 'Tên chi nhánh' },
    { key: 'branchAddress', label: 'Địa chỉ chi nhánh' },
    { key: 'branchPhone', label: 'Số điện thoại chi nhánh' },
    { key: 'cashierName', label: 'Tên thu ngân' },
    { key: 'customerName', label: 'Tên khách hàng' },
    { key: 'customerPhone', label: 'Số điện thoại khách hàng' },
    { key: 'paymentMethod', label: 'Phương thức thanh toán' },
  ];

  for (const field of stringFields) {
    const val = dto[field.key];
    if (val != null && typeof val !== 'string') {
      return { valid: false, reason: `${field.label} (${field.key}) phải có kiểu chuỗi ký tự (string).` };
    }
  }

  if (dto.amountGiven != null && (typeof dto.amountGiven !== 'number' || !Number.isFinite(dto.amountGiven) || dto.amountGiven < 0)) {
    return { valid: false, reason: 'Tiền khách đưa (amountGiven) không hợp lệ.' };
  }

  if (dto.changeDue != null && (typeof dto.changeDue !== 'number' || !Number.isFinite(dto.changeDue) || dto.changeDue < 0)) {
    return { valid: false, reason: 'Tiền thừa trả khách (changeDue) không hợp lệ.' };
  }

  return { valid: true };
}

/**
 * Ánh xạ (Map) dữ liệu hóa đơn từ snapshot chính thức do Server trả về (PosReceiptDto) sang ReceiptData.
 *
 * Tuân thủ nghiêm ngặt các nguyên tắc:
 * 1. PR24-F03 & T02-AC-02: Lấy items, tên sản phẩm, số lượng, đơn giá và thành tiền từ snapshot của server,
 *    không lấy từ giỏ hàng local và không dùng dữ liệu local để vá response.
 * 2. T02-AC-05: Địa chỉ, hotline chi nhánh, thông tin khách hàng nếu null/rỗng thì giữ undefined,
 *    tuyệt đối không bịa đặt số điện thoại giả (như '1900 6868') hay địa chỉ giả ('Chi nhánh bán lẻ POS').
 * 3. Bảo vệ bí mật kinh doanh: Không đưa COGS, CostPrice, TotalCost vào receipt bán lẻ.
 * 4. B01-F02 v1.2: Bắt buộc lấy timestamp chính thức do server trả về (dto.createdAt), không dùng new Date() của client làm fallback.
 */
export function mapServerReceiptToReceiptData(
  dto: PosReceiptDto,
  supplementary?: ReceiptMapperSupplementary
): ReceiptData {
  if (!dto) {
    throw new Error('Dữ liệu PosReceiptDto từ máy chủ không được rỗng.');
  }

  // B01-F02: Xác thực hợp đồng dữ liệu trước khi ánh xạ
  const validation = validatePosReceiptSnapshot(dto);
  if (!validation.valid) {
    throw new Error(`Dữ liệu hóa đơn từ máy chủ không hợp lệ: ${validation.reason}`);
  }

  // B01-F02 v1.2: Định dạng thời gian tạo trực tiếp từ timestamp chính thức của server (PosReceiptDto.createdAt).
  // Tuyệt đối không lấy giờ máy client (new Date()) làm fallback cho hóa đơn chính thức.
  if (!dto.createdAt || typeof dto.createdAt !== 'string' || dto.createdAt.trim().length === 0) {
    throw new Error('Dữ liệu thời gian tạo hóa đơn (createdAt) từ máy chủ bị thiếu hoặc không hợp lệ.');
  }
  const parsedDate = new Date(dto.createdAt);
  if (isNaN(parsedDate.getTime())) {
    throw new Error(`Thời gian tạo hóa đơn từ máy chủ "${dto.createdAt}" không đúng định dạng ngày tháng hợp lệ.`);
  }
  const formattedCreatedAt = parsedDate.toLocaleString('vi-VN');

  // Map danh sách mặt hàng từ snapshot server
  const items: ReceiptItemData[] = (dto.items || []).map((item) => {
    const productName = (typeof item.productName === 'string' && item.productName.trim()) ||
      (typeof item.skuCode === 'string' && item.skuCode.trim()) || 'Sản phẩm';
    const variantPart = (typeof item.variantName === 'string' && item.variantName.trim()) ? ` - ${item.variantName.trim()}` : '';
    const quantity = Number(item.quantity) || 0;
    const unitPrice = Number(item.unitPrice) || 0;
    const subtotal = item.subtotal != null ? Number(item.subtotal) : quantity * unitPrice;

    return {
      name: `${productName}${variantPart}`,
      sku: (typeof item.skuCode === 'string' && item.skuCode.trim()) || '',
      skuId: (typeof item.skuId === 'string' && item.skuId.trim()) || undefined,
      quantity,
      unitPrice,
      subtotal,
    };
  });

  const safeTrimString = (val: any): string | undefined =>
    typeof val === 'string' && val.trim().length > 0 ? val.trim() : undefined;

  const branchAddress = safeTrimString(dto.branchAddress);
  const branchPhone = safeTrimString(dto.branchPhone);
  const customerName = safeTrimString(dto.customerName);
  const customerPhone = safeTrimString(dto.customerPhone);

  const totalAmount = Number(dto.totalAmount) || 0;
  const amountGiven = dto.amountGiven != null ? Number(dto.amountGiven) : totalAmount;
  const changeDue = dto.changeDue != null ? Number(dto.changeDue) : 0;

  return {
    receiptNumber: dto.receiptNumber || dto.orderNumber || '',
    orderNumber: dto.orderNumber || dto.receiptNumber || '',
    orderId: dto.orderId,
    createdAt: formattedCreatedAt,
    tenantName: safeTrimString(supplementary?.tenantName) || 'CỬA HÀNG OISMS',
    branchName: safeTrimString(dto.branchName) || safeTrimString(supplementary?.fallbackBranchName) || 'Chi nhánh POS',
    branchAddress,
    branchPhone,
    cashierName: safeTrimString(dto.cashierName) || safeTrimString(supplementary?.fallbackCashierName) || 'Thu ngân',
    customerName,
    customerPhone,
    items,
    totalAmount,
    paymentMethod: dto.paymentMethod || 'Cash',
    amountGiven,
    changeDue,
    isDraft: false,
  };
}

export type { PosReceiptDto, PosReceiptItemDto, ReceiptData, ReceiptItemData } from '../types/pos';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000';

// Storage key cho pending checkout attempt (RFC 4122 v4 UUID, snapshot payload, owner identity)
const PENDING_STORAGE_KEY = 'pos_pending_checkout_v1';

// In-memory fallback cho môi trường test runner headless / Node VM
let memoryStorage: Record<string, string> = {};

function getStorage(): Storage | null {
  try {
    if (typeof window !== 'undefined' && window.sessionStorage) {
      return window.sessionStorage;
    }
    if (typeof sessionStorage !== 'undefined') {
      return sessionStorage;
    }
    if (typeof window !== 'undefined' && window.localStorage) {
      return window.localStorage;
    }
    if (typeof localStorage !== 'undefined') {
      return localStorage;
    }
  } catch {
    // Mode ẩn danh hoặc iframe sandbox
  }
  return null;
}

function readRawStorage(key: string): string | null {
  const storage = getStorage();
  if (storage) {
    try {
      return storage.getItem(key);
    } catch {
      return memoryStorage[key] ?? null;
    }
  }
  return memoryStorage[key] ?? null;
}

function writeRawStorage(key: string, value: string): void {
  const storage = getStorage();
  if (storage) {
    try {
      storage.setItem(key, value);
      return;
    } catch {
      // Fallback xuống memory
    }
  }
  memoryStorage[key] = value;
}

function removeRawStorage(key: string): void {
  const storage = getStorage();
  if (storage) {
    try {
      storage.removeItem(key);
    } catch {
      // Bỏ qua
    }
  }
  delete memoryStorage[key];
}

/**
 * Sinh UUID v4 chuẩn RFC 4122 cho RequestKey / Idempotency Key.
 * Tuyệt đối không dùng chuỗi `req-${Date.now()}`.
 */
export function generateUuidV4(): string {
  if (typeof crypto !== 'undefined') {
    if (typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }
    if (typeof crypto.getRandomValues === 'function') {
      const bytes = new Uint8Array(16);
      crypto.getRandomValues(bytes);
      bytes[6] = (bytes[6] & 0x0f) | 0x40;
      bytes[8] = (bytes[8] & 0x3f) | 0x80;
      const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
      return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }
  }

  // Fallback chuẩn RFC 4122 dùng Math.random
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

export const UUID_V4_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export function isValidUuid(id: string): boolean {
  return UUID_V4_REGEX.test(id);
}

/**
 * Kiểm tra xem 2 định danh người dùng có cùng một tài khoản và tenant hay không.
 * POS-T01-F01: Tạo owner từ định danh tenant/user đã xác thực và branch.
 * Tuyệt đối không dùng tên hiển thị (tenantName, cashierName, fullName) để kết luận cùng tài khoản.
 * Thiếu định danh phải xử lý an toàn (fail-closed), không mặc định cho phép.
 */
export function isSameUserAndTenant(
  owner: Partial<CheckoutOwner> | null | undefined,
  user: Partial<PosUser> | null | undefined
): boolean {
  if (!owner || !user) return false;

  // 1. Đối chiếu định danh Tenant (ưu tiên tenantId, fallback tenantCode).
  // Tuyệt đối không đối chiếu tenantName hiển thị vì các tenant khác nhau có thể trùng tên.
  const ownerTenant = owner.tenantId || owner.tenantCode;
  const userTenant = user.tenantId || user.tenantCode;
  if (!ownerTenant || !userTenant) {
    return false; // Thiếu định danh tenant -> fail closed
  }
  if (owner.tenantId && user.tenantId && owner.tenantId !== user.tenantId) {
    return false;
  }
  if (owner.tenantCode && user.tenantCode && owner.tenantCode !== user.tenantCode) {
    return false;
  }

  // 2. Đối chiếu định danh Người dùng (ưu tiên userId, fallback email).
  // Tuyệt đối không đối chiếu cashierName hoặc fullName hiển thị.
  const hasUserOwner = Boolean(owner.userId || owner.email);
  const hasUserUser = Boolean(user.id || user.email);
  if (!hasUserOwner || !hasUserUser) {
    return false; // Thiếu định danh user -> fail closed
  }
  if (owner.userId && user.id && owner.userId !== user.id) {
    return false;
  }
  if (owner.email && user.email && owner.email.trim().toLowerCase() !== user.email.trim().toLowerCase()) {
    return false;
  }
  // Nếu một bên chỉ có userId mà bên kia chỉ có email (không so khớp được): fail closed
  if (owner.userId && !owner.email && !user.id && user.email) {
    return false;
  }
  if (!owner.userId && owner.email && user.id && !user.email) {
    return false;
  }

  return true;
}

/**
 * Kiểm tra xem người dùng và chi nhánh có khớp hoàn toàn với owner của pending checkout hay không.
 */
export function isSameOwner(
  owner: CheckoutOwner | null | undefined,
  user: Partial<PosUser> | null | undefined,
  branchId?: string
): boolean {
  if (!isSameUserAndTenant(owner, user)) return false;
  if (branchId && owner && owner.branchId !== branchId) return false;
  return true;
}

function isValidPendingRecord(item: any): item is PendingCheckoutRecord {
  return (
    item &&
    typeof item === 'object' &&
    item.version === 1 &&
    typeof item.requestKey === 'string' &&
    item.owner &&
    typeof item.owner === 'object'
  );
}

function getAllStoredPendingRecords(): PendingCheckoutRecord[] {
  const raw = readRawStorage(PENDING_STORAGE_KEY);
  if (!raw) return [];
  try {
    const parsed = JSON.parse(raw);
    if (!parsed) return [];
    if (Array.isArray(parsed)) {
      return parsed.filter(isValidPendingRecord);
    }
    if (Array.isArray(parsed.records)) {
      return parsed.records.filter(isValidPendingRecord);
    }
    if (isValidPendingRecord(parsed)) {
      return [parsed];
    }
    return [];
  } catch {
    return [];
  }
}

function saveAllPendingRecords(records: PendingCheckoutRecord[]): void {
  if (records.length === 0) {
    removeRawStorage(PENDING_STORAGE_KEY);
  } else {
    writeRawStorage(PENDING_STORAGE_KEY, JSON.stringify({ version: 1, records }));
  }
}

/**
 * Lưu bản ghi checkout đang xử lý (pending attempt) vào sessionStorage (theo tab).
 * POS-T01-F01 & POS-T01-F03: Giữ attempt theo chi nhánh và owner, không để branch khác ghi đè.
 * Không lưu token hay mật khẩu vào bản ghi này.
 */
export function savePendingCheckout(record: PendingCheckoutRecord): void {
  const normalized: PendingCheckoutRecord = {
    ...record,
    version: 1,
    updatedAt: Date.now(),
  };

  const existingList = getAllStoredPendingRecords();
  // Tìm record cũ của cùng owner và chi nhánh
  const index = existingList.findIndex(
    (item) => isSameUserAndTenant(item.owner, record.owner) && item.owner.branchId === record.owner.branchId
  );

  if (index >= 0) {
    existingList[index] = normalized;
  } else {
    existingList.push(normalized);
  }

  saveAllPendingRecords(existingList);
}

/**
 * Đọc bản ghi checkout đang xử lý từ storage.
 * Nếu truyền user/branchId: kiểm tra tính sở hữu, ngăn chặn cross-user/cross-tenant rò rỉ.
 */
export function getPendingCheckout(
  user?: Partial<PosUser> | null,
  branchId?: string
): PendingCheckoutRecord | null {
  const records = getAllStoredPendingRecords();
  if (records.length === 0) return null;

  // Nếu không truyền cả user lẫn branchId: trả về record đầu tiên
  if (!user && !branchId) {
    return records[0] || null;
  }

  const matched = records.find((item) => {
    if (user && !isSameUserAndTenant(item.owner, user)) {
      return false;
    }
    if (branchId && item.owner.branchId !== branchId) {
      return false;
    }
    return true;
  });

  return matched || null;
}

/**
 * Tìm pending checkout record của người dùng trên bất kỳ chi nhánh nào.
 * Dùng khi reload trang để khôi phục đúng chi nhánh và payload trước đó (POS-T01-F03).
 */
export function getUserPendingCheckout(user: Partial<PosUser> | null | undefined): PendingCheckoutRecord | null {
  if (!user) return null;
  const records = getAllStoredPendingRecords();
  return records.find((item) => isSameUserAndTenant(item.owner, user)) || null;
}

/**
 * Cập nhật trạng thái của pending checkout (ví dụ: chuyển từ 'submitting' sang 'unknown').
 */
export function updatePendingCheckoutStatus(
  status: PendingCheckoutStatus,
  errorMessage?: string,
  errorCode?: string,
  user?: Partial<PosUser> | null,
  branchId?: string
): void {
  const current = getPendingCheckout(user, branchId);
  if (!current) return;

  savePendingCheckout({
    ...current,
    status,
    lastErrorMessage: errorMessage ?? current.lastErrorMessage,
    lastErrorCode: errorCode ?? current.lastErrorCode,
    updatedAt: Date.now(),
  });
}

/**
 * Xóa pending checkout sau khi đã có kết quả xác định.
 * Nếu có truyền user / branchId, chỉ xóa đúng record đó, bảo vệ pending của branch/user khác.
 */
export function clearPendingCheckout(
  user?: Partial<PosUser> | null,
  branchId?: string
): void {
  const records = getAllStoredPendingRecords();
  if (records.length === 0) return;

  if (!user && !branchId) {
    removeRawStorage(PENDING_STORAGE_KEY);
    return;
  }

  const remaining = records.filter((item) => {
    if (user && branchId) {
      return !(isSameUserAndTenant(item.owner, user) && item.owner.branchId === branchId);
    }
    if (user) {
      return !isSameUserAndTenant(item.owner, user);
    }
    if (branchId) {
      return item.owner.branchId !== branchId;
    }
    return false;
  });

  saveAllPendingRecords(remaining);
}

/**
 * Reset memory storage dùng trong kiểm thử tự động
 */
export function _resetPendingStorage(): void {
  memoryStorage = {};
}

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

export function getStoredUser(): PosUser | null {
  const data = localStorage.getItem('pos_user');
  if (!data) return null;
  try {
    return JSON.parse(data);
  } catch {
    return null;
  }
}

export function setStoredUser(user: PosUser) {
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
        id: data.user.id,
        tenantId: data.user.tenantId,
        tenantCode: data.user.tenantCode,
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
 * - Gọi canonical POST /api/pos/checkout, fallback sang alias POST /api/orders/checkout khi 404/405.
 * - Header Idempotency-Key và X-Request-Key luôn khớp body.
 * - Đọc RFC 7807 ProblemDetails detail / errorCode.
 * - Phân loại rõ: Thành công, Thất bại xác định (409, 400, 403), Hết phiên (401), Kết quả chưa xác định (502/504, lỗi mạng).
 */
export async function checkoutPosOrder(request: PosCheckoutRequest): Promise<CheckoutResult> {
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

    // 2. Fallback sang Alias POST /api/orders/checkout nếu route canonical chưa có (404/405)
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
      let data: any;
      try {
        data = await res.json();
      } catch (_jsonErr) {
        // POS-T01-F04: Response 2xx nhưng JSON bị hỏng / parse SyntaxError -> Kết quả chưa xác định
        // Tuyệt đối không suy ra success: true hoặc xóa pending
        return {
          success: false,
          statusCode: res.status,
          isUnknown: true,
          isDefinitiveError: false,
          message: 'Máy chủ phản hồi HTTP 2xx nhưng dữ liệu xác nhận không đọc được (lỗi parse JSON). Giao dịch cần đối soát; mã yêu cầu và giỏ hàng được giữ nguyên.',
        };
      }

      // B01-F02, POS-T01-F04 & T02-AC-03: Kiểm tra hợp đồng receipt snapshot từ server
      const rawReceipt = data?.receipt || (data?.items && Array.isArray(data.items) ? data : null);

      const validation = validatePosReceiptSnapshot(rawReceipt);
      if (!validation.valid) {
        // B01-F02: Response 2xx nhưng receipt hỏng hoặc vi phạm contract -> Giữ trạng thái cần đối soát
        // Tuyệt đối không xóa pending hoặc in phiếu giả bằng dữ liệu local.
        return {
          success: false,
          statusCode: res.status,
          isUnknown: true,
          isDefinitiveError: false,
          message: `Máy chủ phản hồi HTTP 2xx nhưng dữ liệu hóa đơn (receipt snapshot) không hợp lệ: ${validation.reason}. Giao dịch cần đối soát; mã yêu cầu và giỏ hàng được giữ nguyên để thử lại an toàn.`,
        };
      }

      const confirmedOrderNumber = String(rawReceipt.orderNumber || rawReceipt.receiptNumber);

      return {
        success: true,
        statusCode: res.status,
        message: data.message || 'Thanh toán đơn hàng thành công!',
        orderNumber: confirmedOrderNumber,
        receipt: rawReceipt as PosReceiptDto,
      };
    }

    if (res.status === 401) {
      removeAuthToken();
      window.dispatchEvent(new CustomEvent('pos-auth-expired'));
      return {
        success: false,
        statusCode: 401,
        isAuthExpired: true,
        message: 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.',
      };
    }

    // Đọc ProblemDetails RFC 7807
    let errBody: any = {};
    try {
      errBody = await res.json();
    } catch {
      errBody = {};
    }

    const errorCode: string | undefined =
      errBody.errorCode ||
      errBody.extensions?.errorCode ||
      (typeof errBody.type === 'string' ? errBody.type.split('/').pop() : undefined);

    let detail: string | undefined = errBody.detail;
    if (!detail && errBody.errors) {
      if (Array.isArray(errBody.errors)) {
        detail = errBody.errors.join('; ');
      } else if (typeof errBody.errors === 'object') {
        detail = Object.values(errBody.errors).flat().join('; ');
      } else {
        detail = String(errBody.errors);
      }
    }

    // 409 Conflict: Thiếu tồn kho (INSUFFICIENT_STOCK) hoặc Xung đột Payload Idempotency
    if (res.status === 409) {
      let message = detail || errBody.message;
      if (errorCode === 'INSUFFICIENT_STOCK') {
        message = detail || 'Xung đột dữ liệu: Tồn kho khả dụng không đủ cho số lượng yêu cầu.';
      } else if (errorCode === 'IDEMPOTENCY_PAYLOAD_MISMATCH') {
        message = detail || 'Khóa yêu cầu (RequestKey) trùng lặp nhưng nội dung giỏ hàng đã thay đổi. Vui lòng thử lại với đơn hàng mới.';
      } else if (!message) {
        message = 'Xung đột dữ liệu: Tồn kho khả dụng không đủ do kênh online vừa giữ chỗ trước hoặc trùng lặp RequestKey.';
      }

      return {
        success: false,
        statusCode: 409,
        errorCode: errorCode || 'CONFLICT',
        detail,
        isDefinitiveError: true,
        message,
      };
    }

    // 400 Bad Request: Lỗi xác thực hoặc Lệch giá (PRICE_MISMATCH)
    if (res.status === 400) {
      let message = detail || errBody.message;
      if (errorCode === 'PRICE_MISMATCH') {
        message = detail || 'Đơn giá niêm yết của một số sản phẩm đã thay đổi trên hệ thống. Vui lòng kiểm tra lại giỏ hàng.';
      } else if (errorCode === 'VALIDATION_ERROR') {
        message = detail || 'Dữ liệu đơn hàng không hợp lệ.';
      } else if (errorCode === 'INSUFFICIENT_CASH_RECEIVED') {
        message = detail || 'Tiền khách đưa chưa đủ để thanh toán đơn hàng.';
      } else if (!message) {
        message = 'Dữ liệu không hợp lệ (HTTP 400).';
      }

      return {
        success: false,
        statusCode: 400,
        errorCode: errorCode || 'VALIDATION_ERROR',
        detail,
        isDefinitiveError: true,
        message,
      };
    }

    // 403 Forbidden
    if (res.status === 403) {
      return {
        success: false,
        statusCode: 403,
        errorCode: errorCode || 'FORBIDDEN',
        detail,
        isDefinitiveError: true,
        message: detail || errBody.message || 'Tài khoản không có quyền thực hiện thanh toán tại chi nhánh này.',
      };
    }

    // 404 / 405 / 501: Route chưa sẵn sàng trên backend
    if (res.status === 404 || res.status === 405 || res.status === 501) {
      return {
        success: false,
        statusCode: res.status,
        isPendingBackend: true,
        isDefinitiveError: true,
        message: 'API thanh toán (POST /api/pos/checkout và /api/orders/checkout) chưa được triển khai trên Backend Swagger. DTO checkout đề xuất đã được chuẩn bị đầy đủ và đang chờ thống nhất hợp đồng API giữa 2 bên.',
      };
    }

    // 5xx: Máy chủ gặp sự cố
    // POS-T01-F02: Không mặc định HTTP 500 đồng nghĩa giao dịch đã rollback.
    // Mọi mã lỗi 5xx (500, 502, 503, 504) đều có thể xảy ra sau commit ở database,
    // do đó là lỗi chưa xác định (isUnknown: true, isDefinitiveError: false) để giữ nguyên key/payload khi retry.
    // (Trừ 501 Not Implemented đã được xử lý ở trên)
    const isServerErrorUnknown = res.status >= 500 && res.status !== 501;
    return {
      success: false,
      statusCode: res.status,
      errorCode: errorCode || 'SERVER_ERROR',
      detail,
      isUnknown: isServerErrorUnknown,
      isDefinitiveError: !isServerErrorUnknown,
      message: detail || errBody.message || `Máy chủ gặp sự cố (Mã lỗi ${res.status}). Kết quả giao dịch chưa xác định, mã yêu cầu và giỏ hàng được giữ nguyên để đối soát hoặc thử lại an toàn.`,
    };
  } catch (err: any) {
    // Lỗi mạng hoặc timeout kết nối khi gọi fetch -> Kết quả chưa xác định
    return {
      success: false,
      isUnknown: true,
      detail: err.message,
      message: `Không thể kết nối đến máy chủ để thực hiện thanh toán (${err.message || 'Lỗi mạng'}). Kết quả giao dịch chưa xác định. Giỏ hàng và mã yêu cầu được giữ nguyên để thử lại.`,
    };
  }
}

// =========================================================================
// Realtime & Stock Synchronization Architecture (S3-POS-B02 / POS-STOCK-F01)
// =========================================================================

export interface StockItemState {
  available: number;
  onHand?: number | null;
  reserved?: number | null;
  skuId?: string;
  skuCode?: string;
  barcode?: string;
  isStale?: boolean;
  error?: string | null;
  updatedAt: number;
}

export interface StockRefreshResult {
  success: boolean;
  updated: Record<string, number>;
  errors: Record<string, string>;
}

const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function isUuid(val?: string | null): boolean {
  return typeof val === 'string' && UUID_REGEX.test(val.trim());
}

const stockStateByBranch: Record<string, Record<string, StockItemState>> = {};
const branchStockSubscribers: Record<string, Set<(stocks: Record<string, StockItemState>) => void>> = {};
const skuIdentityRegistry: Map<string, { skuId?: string; skuCode?: string; barcode?: string }> = new Map();

let globalStockVersion = 0;
const stockVersionByBranch: Record<string, Record<string, number>> = {};

export function registerSkuIdentity(skuId?: string, skuCode?: string, barcode?: string): void {
  const cleanSkuId = typeof skuId === 'string' && skuId.trim() ? skuId.trim() : undefined;
  const cleanSkuCode = typeof skuCode === 'string' && skuCode.trim() ? skuCode.trim() : undefined;
  const cleanBarcode = typeof barcode === 'string' && barcode.trim() ? barcode.trim() : undefined;

  const validUuid = isUuid(cleanSkuId) ? cleanSkuId : undefined;

  // Find any existing entry by validUuid, cleanSkuCode, cleanBarcode, or cleanSkuId
  const existing =
    (validUuid ? skuIdentityRegistry.get(validUuid) : undefined) ||
    (cleanSkuCode ? skuIdentityRegistry.get(cleanSkuCode.toLowerCase()) : undefined) ||
    (cleanBarcode ? skuIdentityRegistry.get(cleanBarcode) : undefined) ||
    (cleanSkuId ? skuIdentityRegistry.get(cleanSkuId) || skuIdentityRegistry.get(cleanSkuId.toLowerCase()) : undefined);

  const entry = existing || {};

  // Never overwrite a valid canonical UUID with a non-UUID SKU code
  if (validUuid) {
    entry.skuId = validUuid;
  }
  if (cleanSkuCode) {
    entry.skuCode = cleanSkuCode;
  }
  if (cleanBarcode) {
    entry.barcode = cleanBarcode;
  }

  if (entry.skuId) {
    skuIdentityRegistry.set(entry.skuId, entry);
  }
  if (entry.skuCode) {
    skuIdentityRegistry.set(entry.skuCode.toLowerCase(), entry);
  }
  if (entry.barcode) {
    skuIdentityRegistry.set(entry.barcode, entry);
  }
}

export function resolveCanonicalVariantId(key: string): string {
  if (!key) return key;
  const trimmed = key.trim();
  const entry = skuIdentityRegistry.get(trimmed) || skuIdentityRegistry.get(trimmed.toLowerCase());
  if (entry?.skuId && isUuid(entry.skuId)) {
    return entry.skuId;
  }
  if (isUuid(trimmed)) {
    return trimmed;
  }
  if (entry?.skuCode) {
    return entry.skuCode.toLowerCase();
  }
  return trimmed.toLowerCase();
}

export function getStockVersion(branchId: string, key: string): number {
  if (!branchId || !key) return 0;
  const branchMap = stockVersionByBranch[branchId];
  if (!branchMap) return 0;
  const canonical = resolveCanonicalVariantId(key);
  return Math.max(
    branchMap[canonical] || 0,
    branchMap[key] || 0,
    branchMap[key.toLowerCase()] || 0
  );
}

export function bumpStockVersion(
  branchId: string,
  key: string,
  aliases?: Array<string | undefined | null>,
  explicitVersion?: number
): number {
  if (!branchId) return 0;
  if (!stockVersionByBranch[branchId]) {
    stockVersionByBranch[branchId] = {};
  }
  const branchMap = stockVersionByBranch[branchId];
  const nextVer = explicitVersion !== undefined ? explicitVersion : ++globalStockVersion;
  const canonical = resolveCanonicalVariantId(key);

  branchMap[canonical] = Math.max(branchMap[canonical] || 0, nextVer);
  branchMap[key] = Math.max(branchMap[key] || 0, nextVer);
  branchMap[key.toLowerCase()] = Math.max(branchMap[key.toLowerCase()] || 0, nextVer);

  const entry =
    skuIdentityRegistry.get(key) ||
    skuIdentityRegistry.get(key.toLowerCase()) ||
    skuIdentityRegistry.get(canonical);
  if (entry) {
    if (entry.skuId) branchMap[entry.skuId] = Math.max(branchMap[entry.skuId] || 0, nextVer);
    if (entry.skuCode) {
      branchMap[entry.skuCode] = Math.max(branchMap[entry.skuCode] || 0, nextVer);
      branchMap[entry.skuCode.toLowerCase()] = Math.max(branchMap[entry.skuCode.toLowerCase()] || 0, nextVer);
    }
    if (entry.barcode) branchMap[entry.barcode] = Math.max(branchMap[entry.barcode] || 0, nextVer);
  }

  if (aliases) {
    for (const a of aliases) {
      if (a) {
        branchMap[a] = Math.max(branchMap[a] || 0, nextVer);
        branchMap[a.toLowerCase()] = Math.max(branchMap[a.toLowerCase()] || 0, nextVer);
      }
    }
  }

  return nextVer;
}

export async function getSkuBalanceAtBranch(
  skuId: string,
  branchId: string
): Promise<{ skuId?: string; available: number; onHand?: number; reserved?: number; skuCode?: string; barcode?: string } | null> {
  const token = getAuthToken();
  const url = `${API_BASE_URL}/api/inventory/balances/sku/${encodeURIComponent(skuId)}/branch/${encodeURIComponent(branchId)}`;
  try {
    const res = await fetch(url, {
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
    });
    if (!res.ok) return null;
    const data = await res.json();
    const item = Array.isArray(data) ? data[0] : data;
    if (!item) return null;
    const available = item.available !== undefined
      ? Number(item.available)
      : (item.onHand !== undefined && item.reserved !== undefined
          ? Math.max(0, Number(item.onHand) - Number(item.reserved))
          : 0);
    return {
      skuId: item.skuId || item.variantId || skuId,
      available,
      onHand: item.onHand,
      reserved: item.reserved,
      skuCode: item.skuCode || item.sku,
      barcode: item.barcode,
    };
  } catch {
    return null;
  }
}

function notifyBranchStockSubscribers(branchId: string) {
  const branchMap = stockStateByBranch[branchId] || {};
  const subs = branchStockSubscribers[branchId];
  if (subs) {
    for (const sub of subs) {
      try {
        sub({ ...branchMap });
      } catch (err) {
        console.error('Lỗi branchStockSubscriber:', err);
      }
    }
  }
}

export function subscribeBranchStock(
  branchId: string,
  subscriber: (stocks: Record<string, StockItemState>) => void
): () => void {
  if (!branchStockSubscribers[branchId]) {
    branchStockSubscribers[branchId] = new Set();
  }
  branchStockSubscribers[branchId].add(subscriber);
  subscriber({ ...(stockStateByBranch[branchId] || {}) });
  return () => {
    branchStockSubscribers[branchId]?.delete(subscriber);
  };
}

export function getCachedStock(branchId: string, key: string): StockItemState | null {
  if (!branchId || !key) return null;
  return stockStateByBranch[branchId]?.[key] || null;
}

export function setAuthoritativeStock(
  branchId: string,
  key: string,
  available: number,
  metadata?: { onHand?: number | null; reserved?: number | null; skuId?: string; skuCode?: string; barcode?: string }
): void {
  if (!branchId || !key) return;
  if (!stockStateByBranch[branchId]) {
    stockStateByBranch[branchId] = {};
  }
  const validUuid = isUuid(key) ? key : (isUuid(metadata?.skuId) ? metadata?.skuId : undefined);
  const skuId = metadata?.skuId || (validUuid ? key : undefined);
  const skuCode = metadata?.skuCode || (!validUuid ? key : undefined);
  const barcode = metadata?.barcode;

  registerSkuIdentity(skuId, skuCode, barcode);

  // Bump freshness version across all writers
  bumpStockVersion(branchId, key, [skuId, skuCode, barcode]);

  stockStateByBranch[branchId][key] = {
    available: Math.max(0, available),
    onHand: metadata?.onHand,
    reserved: metadata?.reserved,
    skuId: skuId || (stockStateByBranch[branchId][key] as any)?.skuId,
    skuCode: skuCode || (stockStateByBranch[branchId][key] as any)?.skuCode,
    barcode: barcode || (stockStateByBranch[branchId][key] as any)?.barcode,
    isStale: false,
    error: null,
    updatedAt: Date.now(),
  };
  notifyBranchStockSubscribers(branchId);
}

export function markStockStale(branchId: string, keys: string[], errorMessage: string): void {
  if (!branchId || keys.length === 0) return;
  if (!stockStateByBranch[branchId]) {
    stockStateByBranch[branchId] = {};
  }
  for (const k of keys) {
    const existing = stockStateByBranch[branchId][k];
    stockStateByBranch[branchId][k] = {
      available: existing ? existing.available : 0,
      onHand: existing?.onHand,
      reserved: existing?.reserved,
      skuId: existing?.skuId,
      skuCode: existing?.skuCode,
      barcode: existing?.barcode,
      isStale: true,
      error: errorMessage,
      updatedAt: Date.now(),
    };
  }
  notifyBranchStockSubscribers(branchId);
}

export async function invalidateAndRefetchStock(
  branchId: string,
  itemsToRefresh: Array<{ skuId?: string; skuCode?: string; barcode?: string | null }>
): Promise<StockRefreshResult> {
  const result: StockRefreshResult = {
    success: true,
    updated: {},
    errors: {},
  };
  if (!branchId || itemsToRefresh.length === 0) {
    return result;
  }

  await Promise.all(
    itemsToRefresh.map(async (item) => {
      const lookupKey = item.skuId || item.skuCode || item.barcode;
      if (!lookupKey) return;

      registerSkuIdentity(item.skuId, item.skuCode, item.barcode || undefined);

      const canonicalId = resolveCanonicalVariantId(item.skuId || item.skuCode || item.barcode || lookupKey);
      const readVersion = bumpStockVersion(branchId, canonicalId, [item.skuId, item.skuCode, item.barcode || undefined]);

      try {
        let freshItem: any = null;
        let lastError: Error | null = null;

        // 1. Nếu có barcode, tra cứu theo barcode
        if (item.barcode) {
          try {
            freshItem = await scanPosBarcode(item.barcode, branchId);
          } catch (e: any) {
            lastError = e;
          }
        }

        // 2. Nếu có skuCode, tra cứu theo skuCode
        if (!freshItem && item.skuCode) {
          try {
            const searchHits = await searchPosProducts(item.skuCode, branchId);
            freshItem = searchHits.find(
              (h) =>
                (item.skuId && (h.variantId === item.skuId || h.sku === item.skuId)) ||
                (item.skuCode && (h.sku.toLowerCase() === item.skuCode.toLowerCase() || h.variantId === item.skuCode)) ||
                (item.barcode && h.barcode === item.barcode)
            );
          } catch (e: any) {
            lastError = e;
          }
        }

        // 3. Nếu chưa tìm được và có skuId, thử giải quyết qua identity registry (nếu đã biết skuCode)
        if (!freshItem && item.skuId) {
          const known = skuIdentityRegistry.get(item.skuId);
          if (known?.skuCode) {
            try {
              const searchHits = await searchPosProducts(known.skuCode, branchId);
              freshItem = searchHits.find(
                (h) => h.variantId === item.skuId || h.sku.toLowerCase() === known.skuCode!.toLowerCase()
              );
            } catch (e: any) {
              lastError = e;
            }
          }
        }

        // 4. Nếu chưa tìm được và CHỈ có skuId (như từ sự kiện StockLevelChanged):
        // Gọi API số dư chuẩn backend GET /api/inventory/balances/sku/{skuId}/branch/{branchId}
        const targetSkuId = (isUuid(item.skuId) ? item.skuId : undefined) || (isUuid(canonicalId) ? canonicalId : undefined);
        if (!freshItem && targetSkuId && !item.skuCode) {
          try {
            const bal = await getSkuBalanceAtBranch(targetSkuId, branchId);
            if (
              bal &&
              bal.available !== undefined &&
              (!bal.skuId || bal.skuId === targetSkuId)
            ) {
              freshItem = {
                variantId: targetSkuId,
                sku: bal.skuCode || targetSkuId,
                barcode: bal.barcode || item.barcode,
                available: bal.available,
                onHand: bal.onHand,
                reserved: bal.reserved,
              };
            }
          } catch (e: any) {
            lastError = e;
          }
        }

        if (!freshItem && lastError) {
          throw lastError;
        }

        // Freshness guard: Nếu đã có direct write hoặc read mới hơn cho item này -> bỏ qua response cũ
        const currentVersion = getStockVersion(branchId, canonicalId);
        if (readVersion < currentVersion) {
          return;
        }

        if (freshItem && freshItem.available !== undefined && freshItem.available !== null) {
          const newStock = Number(freshItem.available);
          registerSkuIdentity(freshItem.variantId || item.skuId, freshItem.sku || item.skuCode, freshItem.barcode || item.barcode);

          const keysToUpdate = [
            freshItem.variantId,
            freshItem.sku,
            item.skuId,
            item.skuCode,
          ].filter((k): k is string => Boolean(k));

          for (const k of keysToUpdate) {
            setAuthoritativeStock(branchId, k, newStock, {
              onHand: freshItem.onHand,
              reserved: freshItem.reserved,
              skuId: freshItem.variantId || item.skuId,
              skuCode: freshItem.sku || item.skuCode,
              barcode: freshItem.barcode || item.barcode,
            });
            result.updated[k] = newStock;
          }
        } else {
          // B02-F03: Mặt hàng vắng mặt / không khớp -> Đánh dấu stale, không lấy hàng khác gán đè
          result.success = false;
          result.errors[lookupKey] = 'Không tìm thấy mặt hàng khớp chính xác trong kho chi nhánh.';
          const keysToStale = [item.skuId, item.skuCode, lookupKey].filter((k): k is string => Boolean(k));
          markStockStale(
            branchId,
            keysToStale,
            'Không tìm thấy mặt hàng khớp chính xác trong kho chi nhánh.'
          );
        }
      } catch (err: any) {
        const currentVersion = getStockVersion(branchId, canonicalId);
        if (readVersion < currentVersion) return;

        result.success = false;
        result.errors[lookupKey] = err.message || 'Lỗi kết nối khi làm mới tồn.';
        const keysToStale = [item.skuId, item.skuCode, lookupKey].filter((k): k is string => Boolean(k));
        markStockStale(
          branchId,
          keysToStale,
          'Dữ liệu tồn chưa đồng bộ do lỗi kết nối. Bấm để thử lại.'
        );
      }
    })
  );

  return result;
}

export async function refreshBranchStock(branchId: string): Promise<StockRefreshResult> {
  const branchMap = stockStateByBranch[branchId] || {};
  const itemsToRefresh: Array<{ skuId?: string; skuCode?: string; barcode?: string }> = [];
  const seen = new Set<string>();

  for (const [key, state] of Object.entries(branchMap)) {
    const isUuidKey = isUuid(key);
    const skuId = (state as any)?.skuId || (isUuidKey ? key : undefined);
    const skuCode = (state as any)?.skuCode || (!isUuidKey ? key : undefined);
    const barcode = (state as any)?.barcode;
    const canon = skuId || skuCode || key;
    if (!seen.has(canon)) {
      seen.add(canon);
      itemsToRefresh.push({ skuId, skuCode, barcode });
    }
  }

  if (itemsToRefresh.length > 0) {
    return await invalidateAndRefetchStock(branchId, itemsToRefresh);
  }
  return { success: true, updated: {}, errors: {} };
}

export function _resetStockState(branchId?: string): void {
  if (branchId) {
    delete stockStateByBranch[branchId];
    delete stockVersionByBranch[branchId];
    notifyBranchStockSubscribers(branchId);
  } else {
    for (const b of Object.keys(stockStateByBranch)) {
      delete stockStateByBranch[b];
    }
    for (const b of Object.keys(stockVersionByBranch)) {
      delete stockVersionByBranch[b];
    }
    skuIdentityRegistry.clear();
    globalStockVersion = 0;
  }
}

export interface RealtimeAdapter {
  start: (user: any, branchId: string) => Promise<void>;
  stop: () => Promise<void>;
  switchBranch: (branchId: string) => Promise<void>;
  subscribeStatus: (cb: (status: any) => void) => () => void;
  subscribeOrder: (cb: (order: any) => void) => () => void;
  subscribeWarning: (cb: (warning: any) => void) => () => void;
  isMuted?: () => boolean;
  setMuted?: (val: boolean) => void;
  playNotificationSound?: () => void;
}

let activeRealtimeAdapter: RealtimeAdapter | null = null;

export function registerRealtimeAdapter(adapter: RealtimeAdapter): void {
  activeRealtimeAdapter = adapter;
}

export async function startRealtimeConnection(user: any, branchId: string): Promise<void> {
  if (activeRealtimeAdapter) {
    return activeRealtimeAdapter.start(user, branchId);
  }
}

export async function stopRealtimeConnection(): Promise<void> {
  if (activeRealtimeAdapter) {
    return activeRealtimeAdapter.stop();
  }
}

export async function switchRealtimeBranch(branchId: string): Promise<void> {
  if (activeRealtimeAdapter) {
    return activeRealtimeAdapter.switchBranch(branchId);
  }
}

export function subscribeRealtimeStatus(cb: (status: any) => void): () => void {
  if (activeRealtimeAdapter) {
    return activeRealtimeAdapter.subscribeStatus(cb);
  }
  cb('disconnected');
  return () => {};
}

export function subscribeOrderCreated(cb: (order: any) => void): () => void {
  if (activeRealtimeAdapter) {
    return activeRealtimeAdapter.subscribeOrder(cb);
  }
  return () => {};
}

export function subscribeLowStockWarning(cb: (warning: any) => void): () => void {
  if (activeRealtimeAdapter) {
    return activeRealtimeAdapter.subscribeWarning(cb);
  }
  return () => {};
}

export function isSoundMuted(): boolean {
  if (activeRealtimeAdapter?.isMuted) {
    return activeRealtimeAdapter.isMuted();
  }
  return false;
}

export function setSoundMuted(val: boolean): void {
  if (activeRealtimeAdapter?.setMuted) {
    activeRealtimeAdapter.setMuted(val);
  }
}
