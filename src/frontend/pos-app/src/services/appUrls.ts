function appUrl(value: string | undefined, fallback: string): string {
  const url = new URL(value || fallback);
  if (!['http:', 'https:'].includes(url.protocol)) throw new Error('URL ứng dụng không hợp lệ.');
  return url.href.replace(/\/$/, '');
}
export const ADMIN_URL = appUrl(import.meta.env.VITE_ADMIN_URL, 'http://localhost:5173');
