import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { CheckCircle2, Eye, EyeOff, Loader2, Plus, RotateCcw, Send, Shuffle, Trash2, X, Zap } from 'lucide-react';
import { sendWebhook, whoAmI, type WebhookIdentity } from './api';
import {
  CHANNELS,
  buildPayload,
  calculateTotal,
  mutatePayload,
  newExternalOrderId,
  parseResponse,
  validateOrder,
  type Channel,
  type ItemInput,
  type OrderInput,
  type WebhookPayload,
} from './webhookPayload';

type SendKind = 'Đơn mới' | 'Gửi lại' | 'Đổi nội dung' | 'Bắn đồng thời';

interface Connection {
  key: number;
  secret: string;
  identity: WebhookIdentity;
}

interface LastSent {
  connectionKey: number;
  payload: WebhookPayload;
}

interface LogEntry {
  id: number;
  at: Date;
  kind: SendKind;
  shop: string;
  externalOrderId: string;
  status: number;
  ok: boolean;
  errorCode?: string;
  summary: string;
  durationMs: number;
  request: WebhookPayload;
  response: unknown;
}

const STORAGE_KEY = 'oisms-simulator-form';
const MAX_LOG = 300;

// Trang Kết nối sàn trong Admin để lấy secret. Deploy truyền VITE_ADMIN_URL lúc build; dev mặc định cổng 5173.
const ADMIN_URL = ((import.meta.env.VITE_ADMIN_URL as string | undefined) || (import.meta.env.DEV ? 'http://localhost:5173' : '')).replace(/\/$/, '');
const ADMIN_SHOP_INTEGRATIONS_URL = ADMIN_URL ? `${ADMIN_URL}/shop-integrations` : '';

const defaultForm: OrderInput = {
  channel: 'Shopee',
  externalShopId: '',
  customerName: 'Khách giả lập',
  customerPhone: '0901234567',
  shippingAddress: '123 Lê Lợi, Quận 1, TP.HCM',
  items: [{ skuCode: '', quantity: '1', unitPrice: '' }],
};

function loadForm(): OrderInput {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return defaultForm;
    const saved = JSON.parse(raw) as Partial<OrderInput>;
    return {
      ...defaultForm,
      ...saved,
      channel: CHANNELS.includes(saved.channel as Channel) ? (saved.channel as Channel) : 'Shopee',
      items: Array.isArray(saved.items) && saved.items.length > 0 ? saved.items : defaultForm.items,
    };
  } catch {
    return defaultForm;
  }
}

let nextLogId = 1;
let nextConnectionKey = 1;

const channelLabel = (c: Channel) => (c === 'TikTok' ? 'TikTok Shop' : c);
const connectionLabel = (c: Connection) => `${channelLabel(c.identity.channel)} · ${c.identity.externalShopId}`;

export default function App() {
  const [form, setForm] = useState<OrderInput>(loadForm);
  // Kết nối chỉ giữ trong bộ nhớ tab: đóng tab là mất, secret không bị lưu lại trên máy.
  const [connections, setConnections] = useState<Connection[]>([]);
  const [selectedKey, setSelectedKey] = useState<number | null>(null);
  const [secretDraft, setSecretDraft] = useState('');
  const [showSecret, setShowSecret] = useState(false);
  const [checking, setChecking] = useState(false);
  const [connectError, setConnectError] = useState<string | null>(null);
  const [lastSent, setLastSent] = useState<LastSent | null>(null);
  const [burstCount, setBurstCount] = useState('20');
  const [burstSameId, setBurstSameId] = useState(false);
  const [busy, setBusy] = useState(false);
  const [log, setLog] = useState<LogEntry[]>([]);
  const [expanded, setExpanded] = useState<number | null>(null);
  const [burstSummary, setBurstSummary] = useState<string | null>(null);

  // Không lưu secret, chỉ lưu thông tin đơn để mở lại trang không phải nhập lại.
  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(form));
    } catch {
      // trình duyệt chặn storage thì bỏ qua
    }
  }, [form]);

  const selected = connections.find((c) => c.key === selectedKey) ?? null;
  const lastConnection = lastSent ? connections.find((c) => c.key === lastSent.connectionKey) ?? null : null;

  // Sàn và mã shop luôn lấy từ kết nối đang chọn, không nhập tay.
  const effectiveForm = useMemo<OrderInput>(
    () => ({ ...form, channel: selected?.identity.channel ?? 'Shopee', externalShopId: selected?.identity.externalShopId ?? '' }),
    [form, selected],
  );
  const errors = useMemo(() => {
    const list = validateOrder(effectiveForm);
    return selected ? list : ['Chưa chọn kết nối gian hàng.', ...list.filter((e) => !e.includes('mã gian hàng'))];
  }, [effectiveForm, selected]);
  const canSend = !busy && errors.length === 0 && selected !== null;
  const itemsValid = effectiveForm.items.length > 0 && errors.every((e) => !e.startsWith('Dòng'));
  const total = itemsValid ? calculateTotal(effectiveForm.items) : null;

  async function addConnection() {
    const secret = secretDraft.trim();
    if (!secret) return;
    const existing = connections.find((c) => c.secret === secret);
    if (existing) {
      setSelectedKey(existing.key);
      setSecretDraft('');
      setConnectError(null);
      return;
    }
    setChecking(true);
    setConnectError(null);
    try {
      const result = await whoAmI(secret);
      if (result.status !== 200) {
        const summary = result.networkError ? `Lỗi mạng: ${result.networkError}` : parseResponse(result.status, result.body).summary;
        setConnectError(summary);
        return;
      }
      const identity = result.body as WebhookIdentity;
      const connection: Connection = { key: nextConnectionKey++, secret, identity };
      setConnections((list) => [...list, connection]);
      setSelectedKey(connection.key);
      setSecretDraft('');
      setShowSecret(false);
    } finally {
      setChecking(false);
    }
  }

  function removeConnection(key: number) {
    setConnections((list) => list.filter((c) => c.key !== key));
    if (selectedKey === key) setSelectedKey(null);
    if (lastSent?.connectionKey === key) setLastSent(null);
  }

  const update = (patch: Partial<OrderInput>) => setForm((f) => ({ ...f, ...patch }));
  const updateItem = (index: number, patch: Partial<ItemInput>) =>
    setForm((f) => ({ ...f, items: f.items.map((item, i) => (i === index ? { ...item, ...patch } : item)) }));

  async function send(kind: SendKind, connection: Connection, payload: WebhookPayload): Promise<LogEntry> {
    const result = await sendWebhook(connection.identity.channel, connection.secret, payload);
    const parsed = result.networkError
      ? { ok: false, summary: `Lỗi mạng: ${result.networkError}` }
      : parseResponse(result.status, result.body);
    const entry: LogEntry = {
      id: nextLogId++,
      at: new Date(),
      kind,
      shop: connectionLabel(connection),
      externalOrderId: payload.externalOrderId,
      status: result.status,
      durationMs: result.durationMs,
      request: payload,
      response: result.body,
      ...parsed,
    };
    setLog((current) => [entry, ...current].slice(0, MAX_LOG));
    return entry;
  }

  async function run(action: () => Promise<void>) {
    setBusy(true);
    try {
      await action();
    } finally {
      setBusy(false);
    }
  }

  const sendNew = () =>
    run(async () => {
      if (!selected) return;
      const payload = buildPayload(effectiveForm, newExternalOrderId(effectiveForm.channel), new Date());
      setLastSent({ connectionKey: selected.key, payload });
      await send('Đơn mới', selected, payload);
    });

  // Gửi lại đúng payload cũ qua đúng kết nối đã gửi: backend trả 200 và kết quả lần trước.
  const resend = () => run(async () => void (lastSent && lastConnection && (await send('Gửi lại', lastConnection, lastSent.payload))));

  // Cùng mã đơn nhưng khác nội dung: backend trả 409 WEBHOOK_PAYLOAD_MISMATCH.
  const resendChanged = () =>
    run(async () => void (lastSent && lastConnection && (await send('Đổi nội dung', lastConnection, mutatePayload(lastSent.payload)))));

  const burst = () =>
    run(async () => {
      if (!selected) return;
      const count = Math.min(Math.max(Number.parseInt(burstCount, 10) || 0, 1), 200);
      const now = new Date();
      const sharedId = newExternalOrderId(effectiveForm.channel);
      const payloads = Array.from({ length: count }, () =>
        buildPayload(effectiveForm, burstSameId ? sharedId : newExternalOrderId(effectiveForm.channel), now),
      );
      setBurstSummary(null);
      const started = performance.now();
      const results = await Promise.all(payloads.map((p) => send('Bắn đồng thời', selected, p)));
      const elapsed = Math.round(performance.now() - started);
      const byStatus = new Map<string, number>();
      for (const r of results) {
        const key = r.status === 0 ? 'Lỗi mạng' : `${r.status}${r.errorCode ? ` ${r.errorCode}` : ''}`;
        byStatus.set(key, (byStatus.get(key) ?? 0) + 1);
      }
      const parts = [...byStatus.entries()].sort().map(([k, v]) => `${v} × ${k}`);
      setBurstSummary(`${count} yêu cầu trong ${elapsed} ms: ${parts.join(', ')}`);
    });

  return (
    <div className="mx-auto max-w-7xl px-4 py-6">
      <header className="mb-6">
        <h1 className="text-2xl font-bold text-slate-900">Giả lập đơn sàn TMĐT</h1>
        <p className="mt-1 text-sm text-slate-600">
          Bắn đơn Shopee, TikTok Shop, Lazada vào OISMS qua webhook. Kết nối và secret chỉ giữ trong tab này, đóng tab là mất.
        </p>
      </header>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,420px)_1fr]">
        <section className="space-y-4">
          <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
            <h2 className="mb-3 font-semibold text-slate-900">Kết nối gian hàng</h2>
            <form
              className="mb-3"
              onSubmit={(e) => {
                e.preventDefault();
                void addConnection();
              }}
            >
              <label htmlFor="secret-input" className="mb-1 block text-xs font-medium text-slate-600">
                Dán webhook secret
              </label>
              <div className="flex gap-2">
                <input
                  id="secret-input"
                  className={`${inputClass} font-mono`}
                  type={showSecret ? 'text' : 'password'}
                  value={secretDraft}
                  onChange={(e) => setSecretDraft(e.target.value)}
                  placeholder="whsec_..."
                  autoComplete="off"
                />
                <button type="button" onClick={() => setShowSecret((s) => !s)} className={iconButtonClass} aria-label={showSecret ? 'Ẩn secret' : 'Hiện secret'}>
                  {showSecret ? <EyeOff size={16} /> : <Eye size={16} />}
                </button>
                <button type="submit" disabled={checking || !secretDraft.trim()} className={`${secondaryButtonClass} h-[38px] shrink-0`}>
                  {checking ? <Loader2 size={14} className="animate-spin" /> : <Plus size={14} />} Thêm
                </button>
              </div>
            </form>
            {connectError && <p className="-mt-1 mb-3 rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-800">{connectError}</p>}

            {connections.length === 0 ? (
              <div className="rounded-lg bg-slate-50 p-3 text-xs text-slate-600">
                <p className="mb-1.5 font-semibold text-slate-800">Chưa có secret? Lấy trong Admin:</p>
                <ol className="list-decimal space-y-0.5 pl-4">
                  <li>Đăng nhập Admin bằng tài khoản Owner.</li>
                  <li>
                    Vào <b>Kết nối sàn</b> (cuối thanh menu), bấm <b>Tạo kết nối</b>
                    {ADMIN_SHOP_INTEGRATIONS_URL && (
                      <>
                        {' '}
                        hoặc{' '}
                        <a className="font-medium text-indigo-600 underline" href={ADMIN_SHOP_INTEGRATIONS_URL} target="_blank" rel="noopener noreferrer">
                          mở trang đó
                        </a>
                      </>
                    )}
                    .
                  </li>
                  <li>Copy secret trong hộp thoại (chỉ hiện một lần) và dán vào ô trên.</li>
                </ol>
                <p className="mt-1.5">Lỡ mất secret thì bấm <b>Đổi secret</b> ở cùng trang để lấy secret mới.</p>
              </div>
            ) : (
              <ul className="space-y-2">
                {connections.map((c) => {
                  const active = c.key === selectedKey;
                  return (
                    <li key={c.key}>
                      <div className={`flex items-center gap-2 rounded-lg border px-3 py-2 ${active ? 'border-indigo-600 bg-indigo-50' : 'border-slate-200 hover:bg-slate-50'}`}>
                        <button type="button" onClick={() => setSelectedKey(c.key)} className="flex min-w-0 flex-1 items-center gap-2 text-left"
                          aria-pressed={active}
                          aria-label={`Chọn kết nối ${connectionLabel(c)}`}
                        >
                          {active ? <CheckCircle2 size={16} className="shrink-0 text-indigo-600" /> : <span className="h-4 w-4 shrink-0 rounded-full border border-slate-300" />}
                          <span className="min-w-0">
                            <span className="block truncate text-sm font-medium text-slate-900">{connectionLabel(c)}</span>
                            <span className={`block truncate text-xs ${c.identity.branchIsActive ? 'text-slate-500' : 'text-amber-700'}`}>
                              {c.identity.branchName || 'Không rõ chi nhánh'}
                              {!c.identity.branchIsActive && ' · chi nhánh đã tắt, đơn sẽ bị từ chối'}
                            </span>
                          </span>
                        </button>
                        <button
                          type="button"
                          onClick={() => removeConnection(c.key)}
                          className="rounded p-1 text-slate-400 hover:bg-slate-200 hover:text-slate-700"
                          aria-label={`Bỏ kết nối ${connectionLabel(c)}`}
                        >
                          <X size={14} />
                        </button>
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>

          <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
            <h2 className="mb-3 font-semibold text-slate-900">Đơn hàng</h2>
            <Field label="Khách hàng">
              <input className={inputClass} value={form.customerName} onChange={(e) => update({ customerName: e.target.value })} />
            </Field>
            <div className="grid grid-cols-2 gap-2">
              <Field label="Điện thoại">
                <input className={inputClass} value={form.customerPhone} onChange={(e) => update({ customerPhone: e.target.value })} />
              </Field>
              <Field label="Địa chỉ">
                <input className={inputClass} value={form.shippingAddress} onChange={(e) => update({ shippingAddress: e.target.value })} />
              </Field>
            </div>

            <div className="mt-2 space-y-2">
              <div className="grid grid-cols-[1fr_70px_100px_32px] gap-2 text-xs font-medium text-slate-500">
                <span>Mã SKU</span>
                <span>SL</span>
                <span>Đơn giá</span>
                <span />
              </div>
              {form.items.map((item, index) => (
                <div key={index} className="grid grid-cols-[1fr_70px_100px_32px] gap-2">
                  <input className={`${inputClass} font-mono`} value={item.skuCode} onChange={(e) => updateItem(index, { skuCode: e.target.value })} placeholder="SKU-001" />
                  <input className={inputClass} inputMode="decimal" value={item.quantity} onChange={(e) => updateItem(index, { quantity: e.target.value })} />
                  <input className={inputClass} inputMode="decimal" value={item.unitPrice} onChange={(e) => updateItem(index, { unitPrice: e.target.value })} placeholder="75000" />
                  <button
                    type="button"
                    className={iconButtonClass}
                    disabled={form.items.length === 1}
                    onClick={() => update({ items: form.items.filter((_, i) => i !== index) })}
                    aria-label="Xóa dòng"
                  >
                    <Trash2 size={14} />
                  </button>
                </div>
              ))}
              <button
                type="button"
                onClick={() => update({ items: [...form.items, { skuCode: '', quantity: '1', unitPrice: '' }] })}
                className="inline-flex items-center gap-1 text-sm font-medium text-indigo-600 hover:text-indigo-800"
              >
                <Plus size={14} /> Thêm dòng
              </button>
            </div>

            <div className="mt-3 flex items-center justify-between border-t border-slate-100 pt-3 text-sm">
              <span className="text-slate-500">Tổng tiền gửi lên</span>
              <span className="font-mono font-semibold">{total === null ? '—' : total.toLocaleString('vi-VN')}</span>
            </div>

            {errors.length > 0 && (
              <ul className="mt-3 list-disc space-y-0.5 rounded-lg bg-amber-50 py-2 pl-6 pr-3 text-xs text-amber-800">
                {errors.map((e) => (
                  <li key={e}>{e}</li>
                ))}
              </ul>
            )}
          </div>

          <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
            <h2 className="mb-3 font-semibold text-slate-900">Gửi</h2>
            <button type="button" disabled={!canSend} onClick={sendNew} className={primaryButtonClass}>
              <Send size={16} /> Gửi đơn mới
            </button>
            <div className="mt-2 grid grid-cols-2 gap-2">
              <button type="button" disabled={busy || !lastSent || !lastConnection} onClick={resend} className={secondaryButtonClass} title="Cùng mã đơn, cùng nội dung: mong đợi 200">
                <RotateCcw size={14} /> Gửi lại
              </button>
              <button type="button" disabled={busy || !lastSent || !lastConnection} onClick={resendChanged} className={secondaryButtonClass} title="Cùng mã đơn, khác nội dung: mong đợi 409">
                <Shuffle size={14} /> Đổi nội dung
              </button>
            </div>
            {lastSent && lastConnection && (
              <p className="mt-2 text-xs text-slate-500">
                Đơn gần nhất: <span className="font-mono">{lastSent.payload.externalOrderId}</span> · {connectionLabel(lastConnection)}
              </p>
            )}

            <div className="mt-4 border-t border-slate-100 pt-4">
              <div className="flex items-end gap-2">
                <Field label="Số đơn bắn cùng lúc (1–200)" className="mb-0 flex-1">
                  <input className={inputClass} inputMode="numeric" value={burstCount} onChange={(e) => setBurstCount(e.target.value)} />
                </Field>
                <button type="button" disabled={!canSend} onClick={burst} className={`${secondaryButtonClass} h-[38px]`}>
                  <Zap size={14} /> Bắn
                </button>
              </div>
              <label className="mt-2 flex items-center gap-2 text-xs text-slate-600">
                <input type="checkbox" checked={burstSameId} onChange={(e) => setBurstSameId(e.target.checked)} />
                Dùng chung một mã đơn (kiểm tra gửi lặp đồng thời)
              </label>
              {burstSummary && <p className="mt-2 rounded-lg bg-slate-50 p-2 text-xs text-slate-700">{burstSummary}</p>}
            </div>
          </div>
        </section>

        <section className="min-w-0 rounded-xl border border-slate-200 bg-white shadow-sm">
          <div className="flex items-center justify-between border-b border-slate-200 px-4 py-3">
            <h2 className="font-semibold text-slate-900">Nhật ký ({log.length})</h2>
            <button type="button" onClick={() => setLog([])} disabled={log.length === 0} className="text-sm text-slate-500 hover:text-slate-800 disabled:opacity-40">
              Xóa
            </button>
          </div>
          {log.length === 0 ? (
            <p className="px-4 py-10 text-center text-sm text-slate-500">Chưa gửi đơn nào.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs text-slate-500">
                  <tr>
                    <th className="px-4 py-2 font-medium">Giờ</th>
                    <th className="px-2 py-2 font-medium">Loại</th>
                    <th className="px-2 py-2 font-medium">Gian hàng</th>
                    <th className="px-2 py-2 font-medium">Mã đơn sàn</th>
                    <th className="px-2 py-2 font-medium">HTTP</th>
                    <th className="px-2 py-2 font-medium">Kết quả</th>
                    <th className="px-4 py-2 text-right font-medium">ms</th>
                  </tr>
                </thead>
                <tbody>
                  {log.map((entry) => (
                    <LogRow key={entry.id} entry={entry} open={expanded === entry.id} onToggle={() => setExpanded((e) => (e === entry.id ? null : entry.id))} />
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </div>
    </div>
  );
}

function LogRow({ entry, open, onToggle }: { entry: LogEntry; open: boolean; onToggle: () => void }) {
  const tone = entry.status === 0 ? 'bg-slate-200 text-slate-700' : entry.ok ? (entry.status === 200 ? 'bg-sky-100 text-sky-800' : 'bg-emerald-100 text-emerald-800') : entry.status >= 500 ? 'bg-rose-200 text-rose-900' : 'bg-amber-100 text-amber-900';
  return (
    <>
      <tr className="cursor-pointer border-t border-slate-100 hover:bg-slate-50" onClick={onToggle}>
        <td className="whitespace-nowrap px-4 py-2 font-mono text-xs text-slate-500">{entry.at.toLocaleTimeString('vi-VN')}</td>
        <td className="whitespace-nowrap px-2 py-2 text-xs">{entry.kind}</td>
        <td className="whitespace-nowrap px-2 py-2 text-xs">{entry.shop}</td>
        <td className="whitespace-nowrap px-2 py-2 font-mono text-xs">{entry.externalOrderId}</td>
        <td className="px-2 py-2">
          <span className={`rounded px-1.5 py-0.5 font-mono text-xs font-semibold ${tone}`}>{entry.status || '—'}</span>
        </td>
        <td className="px-2 py-2 text-xs">
          {entry.errorCode && <span className="mr-1 font-mono font-semibold">{entry.errorCode}</span>}
          {entry.summary}
        </td>
        <td className="px-4 py-2 text-right font-mono text-xs text-slate-500">{entry.durationMs}</td>
      </tr>
      {open && (
        <tr className="border-t border-slate-100 bg-slate-50">
          <td colSpan={7} className="px-4 py-3">
            <div className="grid gap-3 md:grid-cols-2">
              <Json title="Request" value={entry.request} />
              <Json title="Response" value={entry.response} />
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

function Json({ title, value }: { title: string; value: unknown }) {
  return (
    <div className="min-w-0">
      <div className="mb-1 text-xs font-medium text-slate-500">{title}</div>
      <pre className="max-h-72 overflow-auto rounded-lg bg-slate-900 p-3 text-xs text-slate-100">{typeof value === 'string' ? value : JSON.stringify(value, null, 2)}</pre>
    </div>
  );
}

function Field({ label, children, className = 'mb-3' }: { label: string; children: ReactNode; className?: string }) {
  return (
    <label className={`block ${className}`}>
      <span className="mb-1 block text-xs font-medium text-slate-600">{label}</span>
      {children}
    </label>
  );
}

const inputClass =
  'w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100';
const iconButtonClass =
  'inline-flex h-[38px] w-8 shrink-0 items-center justify-center rounded-lg border border-slate-300 text-slate-600 hover:bg-slate-50 disabled:opacity-40';
const primaryButtonClass =
  'inline-flex w-full items-center justify-center gap-2 rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-semibold text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-slate-300';
const secondaryButtonClass =
  'inline-flex items-center justify-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40';
