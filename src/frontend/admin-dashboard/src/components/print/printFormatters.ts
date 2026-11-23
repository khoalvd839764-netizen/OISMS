export function formatPrintNumber(val: number | null | undefined): string {
  if (val === null || val === undefined || Number.isNaN(val)) return '—';
  return new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 }).format(val);
}

export function formatPrintCurrency(val: number | null | undefined): string {
  if (val === null || val === undefined || Number.isNaN(val)) return '—';
  return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
}

export function formatPrintDateTimeVn(iso?: string | Date | null): string {
  if (!iso) return '—';
  const date = typeof iso === 'string' ? new Date(iso) : iso;
  if (Number.isNaN(date.getTime())) return '—';
  return new Intl.DateTimeFormat('vi-VN', {
    timeZone: 'Asia/Ho_Chi_Minh',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).format(date);
}
