// src/types/pricing.ts
// Kiểu dữ liệu Bảng giá theo thời gian và Phiếu đổi giá (OISMS-47-U04)

export interface PriceList {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

export interface CreatePriceChangeRequestItem {
  priceListCode: string;
  skuId: string;
  unitId: string;
  newPrice: number;
}

export interface CreatePriceChangeRequest {
  reason: string;
  requestedValidFrom?: string | null; // ISO-8601
  items: CreatePriceChangeRequestItem[];
}

export interface PriceChangeRequestItemDto {
  id: string;
  priceListId: string;
  priceListCode: string;
  skuId: string;
  skuCode: string;
  unitId: string;
  unitName: string;
  newPrice: number;
}

export interface PriceChangeRequestResponse {
  id: string;
  requestKey: string;
  kind: string;
  status: 'Pending' | 'Approved' | 'Rejected' | string;
  reason: string;
  requestedValidFrom?: string | null;
  createdBy?: string | null;
  createdAt: string;
  reviewedBy?: string | null;
  reviewedAt?: string | null;
  reviewNote?: string | null;
  isReplay: boolean;
  items: PriceChangeRequestItemDto[];
}

export interface EffectivePrice {
  skuUnitPriceId: string;
  priceListId: string;
  priceListCode: string;
  skuId: string;
  unitId: string;
  price: number;
  validFrom: string;
}

export interface PriceHistoryItem {
  id: string;
  priceListCode: string;
  skuId: string;
  unitId: string;
  price: number;
  validFrom: string;
  validTo?: string | null;
  isActive: boolean;
  cancelledAt?: string | null;
  cancelReason?: string | null;
}

export interface PriceMismatchItem {
  skuId: string;
  skuCode: string;
  unitId?: string | null;
  currentPrice: number;
}
