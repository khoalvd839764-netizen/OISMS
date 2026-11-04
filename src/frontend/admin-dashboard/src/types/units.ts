export interface Unit {
  id: string;
  code: string;
  name: string;
  symbol: string;
  measureGroup: string;
  decimalPlaces: number;
  isActive: boolean;
  createdAt?: string;
}

export interface CreateUnitRequest {
  code: string;
  name: string;
  symbol: string;
  measureGroup: string;
  decimalPlaces: number;
}

export interface SkuUnit {
  id: string;
  skuId: string;
  unitId: string;
  unitCode: string;
  unitName: string;
  unitSymbol: string;
  measureGroup: string;
  factorToBase: number;
  canPurchase: boolean;
  canSell: boolean;
  isDefaultPurchase: boolean;
  isDefaultSale: boolean;
  barcode?: string | null;
  quantityDecimalPlaces: number;
  salePrice?: number | null;
  version: number;
  isActive: boolean;
  isBase: boolean;
}

export interface CreateSkuUnitRequest {
  unitId: string;
  factorToBase: number;
  canPurchase?: boolean;
  canSell?: boolean;
  isDefaultPurchase?: boolean;
  isDefaultSale?: boolean;
  barcode?: string | null;
  quantityDecimalPlaces?: number;
  salePrice?: number | null;
}

export interface UpdateSkuUnitRequest {
  factorToBase?: number;
  canPurchase?: boolean;
  canSell?: boolean;
  isDefaultPurchase?: boolean;
  isDefaultSale?: boolean;
  barcode?: string | null;
  quantityDecimalPlaces?: number;
  salePrice?: number | null;
  isActive?: boolean;
}

export interface SkuUnitFactorHistory {
  id: string;
  skuUnitId: string;
  oldFactor?: number | null;
  newFactor: number;
  oldVersion?: number | null;
  newVersion: number;
  changedBy?: string | null;
  changedAt: string;
}
