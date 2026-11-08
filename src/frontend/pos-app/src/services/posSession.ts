/** Independent POS session; never consumes the retired app's pos_access_token. */
export const POS_ACCESS_KEY = 'oisms_pos_v2_access';
export const POS_REFRESH_KEY = 'oisms_pos_v2_refresh';
export const POS_TENANT_KEY = 'oisms_pos_v2_tenant';
export const POS_IDENTITY_KEY = 'oisms_pos_v2_identity';
const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
let generation = 0;
let refreshFlight: { generation: number; promise: Promise<string> } | null = null;
let observedIdentity = localStorage.getItem(POS_IDENTITY_KEY);
export function invalidatePosSession(): number {
  generation++; refreshFlight = null; observedIdentity = localStorage.getItem(POS_IDENTITY_KEY); return generation;
}
export function getPosGeneration() {
  if (localStorage.getItem(POS_IDENTITY_KEY) !== observedIdentity) invalidatePosSession();
  return generation;
}
export const getPosAccessToken = () => localStorage.getItem(POS_ACCESS_KEY);

export function clearPosSession(): number {
  generation++;
  refreshFlight = null;
  for (const key of [POS_ACCESS_KEY, POS_REFRESH_KEY, POS_TENANT_KEY, POS_IDENTITY_KEY]) localStorage.removeItem(key);
  observedIdentity = null;
  // Pending/QR snapshots intentionally survive logout; readers enforce tenant/user/branch ownership.
  return generation;
}

function expire(expected: number) {
  if (expected !== generation) return;
  clearPosSession();
  window.dispatchEvent(new CustomEvent('pos-auth-expired'));
}

export function savePosSession(data: { accessToken: string; refreshToken?: string; user?: { id?: string; tenantId?: string } }, expected: number) {
  if (expected !== getPosGeneration()) throw new Error('Phiên đăng nhập đã thay đổi.');
  if (!data.accessToken) throw new Error('Máy chủ không trả về access token.');
  localStorage.setItem(POS_ACCESS_KEY, data.accessToken);
  if (data.refreshToken) localStorage.setItem(POS_REFRESH_KEY, data.refreshToken);
  if (data.user?.tenantId) localStorage.setItem(POS_TENANT_KEY, data.user.tenantId);
  if (data.user?.id && data.user.tenantId) {
    observedIdentity = `${data.user.tenantId}:${data.user.id}`;
    localStorage.setItem(POS_IDENTITY_KEY, observedIdentity);
  }
}

export async function loginPos(email: string, password: string) {
  const expected = clearPosSession();
  const response = await fetch(`${API_BASE_URL}/api/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password }),
  });
  const data = await response.json();
  if (expected !== generation) throw new Error('Phiên đăng nhập đã thay đổi.');
  if (!response.ok) throw new Error(data.detail || data.message || 'Email hoặc mật khẩu không chính xác.');
  if (!data.user?.id || !data.user?.tenantId || !data.user.roles?.some((r: string) => ['Owner', 'Staff', 'Cashier'].includes(r))) {
    throw new Error('Tài khoản không có quyền sử dụng POS.');
  }
  savePosSession(data, expected);
  return data.user;
}

export async function refreshPosToken(): Promise<string> {
  const expected = getPosGeneration();
  if (refreshFlight?.generation === expected) return refreshFlight.promise;
  const refreshToken = localStorage.getItem(POS_REFRESH_KEY);
  if (!refreshToken) { expire(expected); throw new Error('Phiên đăng nhập đã hết hạn.'); }
  const promise = (async () => {
    try {
      const response = await fetch(`${API_BASE_URL}/api/auth/refresh-token`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken }),
      });
      const data = await response.json();
      if (expected !== getPosGeneration()) throw new Error('Phiên đăng nhập đã thay đổi.');
      if (localStorage.getItem(POS_REFRESH_KEY) !== refreshToken) {
        const rotated = getPosAccessToken();
        if (rotated) return rotated; // another POS tab already refreshed this same identity
        throw new Error('Phiên đăng nhập đã thay đổi.');
      }
      if (!response.ok || !data.accessToken) throw new Error('Không thể làm mới phiên đăng nhập.');
      savePosSession(data, expected);
      return data.accessToken as string;
    } catch (error) { if (expected === getPosGeneration() && localStorage.getItem(POS_REFRESH_KEY) === refreshToken) expire(expected); throw error; }
    finally { if (refreshFlight?.generation === expected) refreshFlight = null; }
  })();
  refreshFlight = { generation: expected, promise };
  return promise;
}

/** Single-flight refresh; retries preserve the exact checkout body/RequestKey. */
export async function authenticatedFetch(input: RequestInfo | URL, init: RequestInit = {}): Promise<Response> {
  const expected = getPosGeneration();
  const token = getPosAccessToken();
  const send = (access: string | null) => fetch(input, { ...init, headers: { ...init.headers, ...(access ? { Authorization: `Bearer ${access}` } : {}) } });
  let response = await send(token);
  if (expected !== getPosGeneration()) throw new Error('Phiên đăng nhập đã thay đổi.');
  if (response.status === 401) {
    const current = getPosAccessToken();
    const renewed = current && current !== token ? current : await refreshPosToken();
    if (expected !== getPosGeneration()) throw new Error('Phiên đăng nhập đã thay đổi.');
    response = await send(renewed);
    if (expected !== getPosGeneration()) throw new Error('Phiên đăng nhập đã thay đổi.');
    if (response.status === 401) expire(expected);
  }
  return response;
}

export async function logoutPos() {
  const refreshToken = localStorage.getItem(POS_REFRESH_KEY);
  clearPosSession(); // invalidate immediately, before any asynchronous revocation
  window.dispatchEvent(new CustomEvent('pos-session-ended'));
  if (refreshToken) await fetch(`${API_BASE_URL}/api/auth/logout`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken }),
  }).catch(() => {});
}

export async function getPosMe() {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/me`);
  if (!response.ok) throw new Error('Không thể xác thực phiên POS.');
  const user = await response.json();
  if (!user?.id || !user?.tenantId || !user.roles?.some((r: string) => ['Owner', 'Staff', 'Cashier'].includes(r))) throw new Error('Không có quyền sử dụng POS.');
  return user;
}
