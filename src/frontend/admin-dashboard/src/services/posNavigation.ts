export const POS_URL = (import.meta.env.VITE_POS_URL || 'http://localhost:5174').replace(/\/$/, '');
if (!['http:', 'https:'].includes(new URL(POS_URL).protocol)) throw new Error('VITE_POS_URL không hợp lệ.');

/** Own-origin session is revoked before handing a Cashier to the independent POS. */
export async function openStandalonePos(role?: string, logout?: () => Promise<void>) {
  if (role === 'Cashier' && logout) await logout();
  window.location.replace(POS_URL);
}
