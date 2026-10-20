export type Role = "Owner" | "Staff" | "Cashier";
export type OrderStatus = "Draft" | "Pending" | "Reserved" | "Confirmed" | "Completed" | "Cancelled";

export interface Session {
  userId: string;
  tenantId: string;
  role: Role;
  branchIds: string[];
  warehouseIds: string[];
}

export interface BranchStockBreakdown {
  branchId: string;
  branchName: string;
  warehouseId?: string;
  warehouseName?: string;
  onHand: number;
  reserved: number;
  available: number;
  averageCost: number;
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
  isActive?: boolean;
  productIsActive?: boolean;
  categoryId?: string | null;
  categoryName?: string | null;
  brandId?: string | null;
  brandName?: string | null;
  branchId?: string;
  branchName?: string;
  warehouseId?: string;
  warehouseName?: string;
  /** Tổng giá trị tồn kho của SKU cộng dồn theo đúng từng chi nhánh: sum(OnHand_branch * AverageCost_branch) */
  totalInventoryValue?: number;
  /** Số dư chi tiết theo từng Chi nhánh / Kho */
  branchBalances?: BranchStockBreakdown[];
}

export interface Order {
  id: string;
  code: string;
  tenantId?: string;
  channel: "POS" | "Admin" | "Shopee" | "TikTok" | "Lazada";
  branchId?: string;
  branch?: string;
  warehouseId?: string;
  warehouse?: string;
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
  branchId?: string;
  branchName?: string;
  warehouseId?: string;
  warehouse: string;
  skuId?: string;
  sku: string;
  direction: "IN" | "OUT";
  transaction: string;
  quantity: number;
  unitCost?: number;
  balanceAfter: number;
  reference: string;
}

export interface PagedMeta {
  pageIndex: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ApiResult<T> {
  data?: T;
  meta?: PagedMeta;
  error?: {
    code: "INSUFFICIENT_STOCK" | "SKU_ALREADY_EXISTS" | "INVALID_STATE" | "ALREADY_PROCESSED" | "NETWORK_ERROR";
    message: string;
  };
}
