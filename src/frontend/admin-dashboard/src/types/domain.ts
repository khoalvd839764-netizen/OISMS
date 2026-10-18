export type Role = "Owner" | "Staff" | "Cashier";
export type OrderStatus = "Draft" | "Pending" | "Reserved" | "Confirmed" | "Completed" | "Cancelled";

export interface Session {
  userId: string;
  tenantId: string;
  role: Role;
  branchIds: string[];
  warehouseIds: string[];
}

export interface Sku {
  id: string;
  productId: string;
  code: string;
  name: string;
  variant: string;
  barcode: string;
  sellingPrice: number;
  wholesalePrice: number;
  averageCost: number;
  onHand: number;
  reserved: number;
  threshold: number;
  image: string;
  categoryId?: string | null;
  categoryName?: string | null;
  brandId?: string | null;
  brandName?: string | null;
  branchId?: string;
  branchName?: string;
  warehouseId?: string;
  warehouseName?: string;
}

export interface Order {
  id: string;
  code: string;
  channel: "POS" | "Admin" | "Shopee" | "TikTok" | "Lazada";
  branch?: string;
  createdAt: string;
  amount: number;
  cogs?: number;
  status: OrderStatus;
  customer: string;
  itemCount?: number;
  expiresIn?: string;
  items?: Array<{
    skuCode: string;
    name: string;
    quantity: number;
    price: number;
    costSnapshot: number;
  }>;
}

export interface LedgerEntry {
  id: string;
  createdAt: string;
  warehouse: string;
  sku: string;
  direction: "IN" | "OUT";
  transaction: string;
  quantity: number;
  balanceAfter: number;
  reference: string;
}

export interface ApiResult<T> {
  data?: T;
  error?: {
    code: "INSUFFICIENT_STOCK" | "SKU_ALREADY_EXISTS" | "INVALID_STATE" | "ALREADY_PROCESSED";
    message: string;
  };
}
