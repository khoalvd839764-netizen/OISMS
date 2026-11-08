export const POS_URL = (import.meta.env.VITE_POS_URL || 'http://localhost:5174').replace(/\/$/, '');
if (!['http:', 'https:'].includes(new URL(POS_URL).protocol)) throw new Error('VITE_POS_URL không hợp lệ.');
export const POS_LOGIN_URL = `${POS_URL}/login`;

/** Cashier đã đăng nhập nhầm ở form Admin: mở thẳng màn đăng nhập POS kèm dòng nhắc. */
export function openPosLoginFromAdmin() {
  window.location.replace(`${POS_LOGIN_URL}#from=admin-login`);
}

/** Own-origin session is revoked before handing a Cashier to the independent POS. */
export async function openStandalonePos(role?: string, logout?: () => Promise<void>) {
  if (role === 'Cashier' && logout) await logout();
  window.location.replace(POS_URL);
}
