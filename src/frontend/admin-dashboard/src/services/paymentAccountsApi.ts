import { httpClient } from './httpClient';

export interface PaymentAccount {
  id: string;
  tenantId: string;
  bankBin: string;
  bankName: string;
  bankShortName: string;
  accountNumber: string;
  accountName: string;
  isDefault: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreatePaymentAccountRequest {
  bankBin: string;
  bankName: string;
  bankShortName: string;
  accountNumber: string;
  accountName: string;
  isDefault?: boolean;
}

export interface UpdatePaymentAccountRequest {
  bankName: string;
  bankShortName: string;
  accountName: string;
  isDefault?: boolean;
}

export interface VietQrBankItem {
  id: number;
  name: string;
  code: string;
  bin: string;
  shortName: string;
  logo: string;
}

export const FALLBACK_BANKS: VietQrBankItem[] = [
  { id: 1, name: 'Ngân hàng TMCP Ngoại Thương Việt Nam', code: 'VCB', bin: '970436', shortName: 'Vietcombank', logo: '' },
  { id: 2, name: 'Ngân hàng TMCP Quân Đội', code: 'MB', bin: '970422', shortName: 'MBBank', logo: '' },
  { id: 3, name: 'Ngân hàng TMCP Kỹ Thương Việt Nam', code: 'TCB', bin: '970407', shortName: 'Techcombank', logo: '' },
  { id: 4, name: 'Ngân hàng TMCP Công Thương Việt Nam', code: 'CTG', bin: '970415', shortName: 'VietinBank', logo: '' },
  { id: 5, name: 'Ngân hàng TMCP Đầu tư và Phát triển Việt Nam', code: 'BIDV', bin: '970418', shortName: 'BIDV', logo: '' },
  { id: 6, name: 'Ngân hàng TMCP Á Châu', code: 'ACB', bin: '970416', shortName: 'ACB', logo: '' },
  { id: 7, name: 'Ngân hàng TMCP Việt Nam Thịnh Vượng', code: 'VPB', bin: '970432', shortName: 'VPBank', logo: '' },
  { id: 8, name: 'Ngân hàng TMCP Tiên Phong', code: 'TPB', bin: '970423', shortName: 'TPBank', logo: '' },
  { id: 9, name: 'Ngân hàng TMCP Sài Gòn Thương Tín', code: 'STB', bin: '970403', shortName: 'Sacombank', logo: '' },
  { id: 10, name: 'Ngân hàng TMCP Hàng Hải Việt Nam', code: 'MSB', bin: '970426', shortName: 'MSB', logo: '' },
];

export async function fetchVietQrBanks(): Promise<VietQrBankItem[]> {
  try {
    const res = await fetch('https://api.vietqr.io/v2/banks', {
      method: 'GET',
      headers: { Accept: 'application/json' },
    });
    if (res.ok) {
      const data = await res.json();
      if (data.code === '00' && Array.isArray(data.data)) {
        return data.data.map((b: any) => ({
          id: b.id,
          name: b.name,
          code: b.code,
          bin: b.bin,
          shortName: b.shortName || b.short_name || b.name,
          logo: b.logo || '',
        }));
      }
    }
  } catch (err) {
    console.warn('Không thể tải danh mục ngân hàng từ VietQR, sử dụng danh mục dự phòng.', err);
  }
  return FALLBACK_BANKS;
}

export const paymentAccountsApi = {
  async list(): Promise<PaymentAccount[]> {
    const res = await httpClient.get<PaymentAccount[]>('/api/payment-accounts');
    return Array.isArray(res.data) ? res.data : [];
  },

  async listActive(): Promise<PaymentAccount[]> {
    const res = await httpClient.get<PaymentAccount[]>('/api/payment-accounts/active');
    return Array.isArray(res.data) ? res.data : [];
  },

  async create(data: CreatePaymentAccountRequest): Promise<PaymentAccount> {
    const res = await httpClient.post<PaymentAccount>('/api/payment-accounts', data);
    return res.data;
  },

  async update(id: string, data: UpdatePaymentAccountRequest): Promise<PaymentAccount> {
    const res = await httpClient.put<PaymentAccount>(`/api/payment-accounts/${id}`, data);
    return res.data;
  },

  async setDefault(id: string): Promise<PaymentAccount> {
    const res = await httpClient.post<PaymentAccount>(`/api/payment-accounts/${id}/set-default`);
    return res.data;
  },

  async deactivate(id: string): Promise<PaymentAccount> {
    const res = await httpClient.post<PaymentAccount>(`/api/payment-accounts/${id}/deactivate`);
    return res.data;
  },
};
