import type {
  CheckoutOwner,
  PendingCheckoutRecord,
  PendingCheckoutStatus,
  PosUser,
} from '../types/pos';

const PENDING_STORAGE_KEY = 'pos_pending_checkout_v1';

// In-memory fallback storage for headless test runners or environments without sessionStorage
let memoryStorage: Record<string, string> = {};

function getStorage(): Storage | null {
  try {
    if (typeof window !== 'undefined' && window.sessionStorage) {
      return window.sessionStorage;
    }
    if (typeof sessionStorage !== 'undefined') {
      return sessionStorage;
    }
  } catch {
    // sessionStorage can throw in private browsing or restricted iframes
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
      // Fallback to memory
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
      // Ignore
    }
  }
  delete memoryStorage[key];
}

/**
 * Tạo UUID v4 chuẩn RFC 4122 cho RequestKey / Idempotency Key.
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
      // RFC 4122 Section 4.4: Version 4 & Variant 10xx
      bytes[6] = (bytes[6] & 0x0f) | 0x40;
      bytes[8] = (bytes[8] & 0x3f) | 0x80;
      const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
      return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }
  }

  // Math.random RFC 4122 compliant fallback
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
 * Dành cho test: reset bộ nhớ fallback
 */
export function _resetMemoryStorage(): void {
  memoryStorage = {};
}
