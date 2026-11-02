// Negative control for the live runner: corrupt only the client's proxy GET.
// Direct reads from the test backend remain the independent stock oracle.
const mode = process.env.POS_STOCK_NEGATIVE_CONTROL;
if (!['wrong', 'stale'].includes(mode)) throw new Error('Unknown B02 negative control');
const backend = new URL(process.env.POS_TEST_BACKEND_URL || 'http://localhost:5001');
const fetchOriginal = globalThis.fetch;
const logOriginal = console.log;
let dropped = false;
console.log = (...args) => {
  if (args.join(' ').includes('Ngắt transport thật bằng proxy/')) dropped = true;
  logOriginal(...args);
};
globalThis.fetch = async (...args) => {
  const response = await fetchOriginal(...args);
  const url = new URL(typeof args[0] === 'string' ? args[0] : args[0].url);
  if (!dropped || !response.ok || url.origin === backend.origin ||
      !/\/api\/pos\/barcode\//.test(url.pathname)) return response;
  const actual = await response.clone().json();
  if (!Number.isFinite(actual.available)) throw new Error('Negative control requires numeric stock');
  const injected = mode === 'stale' ? actual.available + 1 : (actual.available === 123 ? 124 : 123);
  const data = { ...actual, available: injected, onHand: injected + actual.reserved };
  const headers = new Headers(response.headers);
  headers.delete('content-length');
  logOriginal('[B02-STOCK-NEGATIVE-CONTROL]', JSON.stringify({ mode, actual: actual.available, injected }));
  return new Response(JSON.stringify(data), { status: response.status, headers });
};
