import { httpClient } from './httpClient';

export type ShopChannel = 'Shopee' | 'TikTok' | 'Lazada';

export interface ShopIntegration {
  id: string;
  channel: ShopChannel;
  externalShopId: string;
  branchId: string;
  branchName: string;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

/** Secret chỉ có trong response lúc tạo hoặc đổi; backend chỉ lưu bản băm nên không xem lại được. */
export interface ShopIntegrationSecret {
  integration: ShopIntegration;
  webhookSecret: string;
}

export function readShopIntegrationError(err: unknown, fallback: string): string {
  const data = (err as { response?: { data?: Record<string, unknown> } })?.response?.data;
  if (data) {
    if (typeof data.detail === 'string') return data.detail;
    if (typeof data.message === 'string') return data.message;
  }
  if (!(err as { response?: unknown })?.response) return 'Không kết nối được máy chủ.';
  return fallback;
}

export const shopIntegrationService = {
  async list(): Promise<ShopIntegration[]> {
    const res = await httpClient.get<ShopIntegration[]>('/api/shop-integrations');
    return Array.isArray(res.data) ? res.data : [];
  },

  async create(payload: { channel: ShopChannel; externalShopId: string; branchId: string }): Promise<ShopIntegrationSecret> {
    const res = await httpClient.post<ShopIntegrationSecret>('/api/shop-integrations', payload);
    return res.data;
  },

  async update(id: string, payload: { branchId: string; isActive: boolean }): Promise<ShopIntegration> {
    const res = await httpClient.put<ShopIntegration>(`/api/shop-integrations/${id}`, payload);
    return res.data;
  },

  async rotateSecret(id: string): Promise<ShopIntegrationSecret> {
    const res = await httpClient.post<ShopIntegrationSecret>(`/api/shop-integrations/${id}/rotate-secret`);
    return res.data;
  },
};

/**
 * Địa chỉ app giả lập. Deploy truyền VITE_SIMULATOR_URL lúc build; chạy dev mặc định cổng 5175.
 * Không có thì ẩn nút mở app.
 */
export const SIMULATOR_URL: string =
  (import.meta.env.VITE_SIMULATOR_URL as string | undefined)?.replace(/\/$/, '') ||
  (import.meta.env.DEV ? 'http://localhost:5175' : '');
