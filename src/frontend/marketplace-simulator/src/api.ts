import type { Channel, WebhookPayload } from './webhookPayload';

// Để trống thì gọi cùng domain: Vite proxy khi dev, nginx hoặc Caddy khi deploy.
const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

export interface SendResult {
  status: number;
  body: unknown;
  durationMs: number;
  networkError?: string;
}

export interface WebhookIdentity {
  channel: Channel;
  externalShopId: string;
  branchName: string;
  branchIsActive: boolean;
}

async function request(path: string, init: RequestInit): Promise<SendResult> {
  const started = performance.now();
  try {
    const response = await fetch(`${API_BASE_URL}${path}`, init);
    const raw = await response.text();
    let body: unknown = raw;
    try {
      body = raw ? JSON.parse(raw) : null;
    } catch {
      // body không phải JSON, giữ nguyên chuỗi
    }
    return { status: response.status, body, durationMs: Math.round(performance.now() - started) };
  } catch (error) {
    return {
      status: 0,
      body: null,
      durationMs: Math.round(performance.now() - started),
      networkError: error instanceof Error ? error.message : String(error),
    };
  }
}

/** Hỏi backend secret thuộc gian hàng nào. 200 kèm WebhookIdentity; secret không dùng được trả 401. */
export function whoAmI(secret: string): Promise<SendResult> {
  return request('/api/webhooks/simulator/whoami', { method: 'GET', headers: { 'X-Webhook-Secret': secret } });
}

export function sendWebhook(channel: Channel, secret: string, payload: WebhookPayload): Promise<SendResult> {
  return request(`/api/webhooks/simulator/${channel.toLowerCase()}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Webhook-Secret': secret },
    body: JSON.stringify(payload),
  });
}
