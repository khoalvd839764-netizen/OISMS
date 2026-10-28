import { httpClient } from "./httpClient";
import type { ApiResult, BranchStockBreakdown, LedgerEntry, Order, PagedMeta, Sku } from "../types/domain";

function getActiveTenantId(): string {
  return localStorage.getItem("oism_tenant_id") || "default";
}

export function getTenantOrdersStorageKey(tenantId?: string): string {
  const tid = tenantId || getActiveTenantId();
  return `oism_orders_${tid}`;
}

export function getPendingReceiptStorageKey(tenantId?: string): string {
  const tid = tenantId || getActiveTenantId();
  return `oism_pending_receipt_${tid}`;
}

export function getPendingReservationStorageKey(tenantId?: string): string {
  const tid = tenantId || getActiveTenantId();
  return `oism_pending_reservation_${tid}`;
}

export function getPendingOrderStorageKey(tenantId?: string, userId?: string): string {
  const tid = tenantId || getActiveTenantId();
  const uid = userId || 'default';
  return `oism_pending_order_${tid}_${uid}`;
}

// [F2] Trình sinh UUID RFC4122 chuẩn cho ClientRequestId / Idempotency-Key
export function generateUuid(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === "x" ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

// Tải toàn bộ các trang từ endpoint có phân trang (tối đa 100 bản ghi/trang của Backend)
// [F6] Loại bỏ giới hạn cứng 10 trang (1.000 bản ghi), phân trang đầy đủ qua toàn bộ totalPages
async function fetchAllPages<T>(
  endpoint: string,
  baseParams: Record<string, string | number | undefined> = {}
): Promise<{ items: T[]; meta: PagedMeta }> {
  const firstParams = new URLSearchParams();
  Object.entries({ ...baseParams, pageIndex: 1, pageSize: 100 }).forEach(([k, v]) => {
    if (v !== undefined && String(v).trim() !== "") firstParams.set(k, String(v));
  });

  const firstRes = await httpClient.get(`${endpoint}?${firstParams.toString()}`);
  const rawData = firstRes.data;

  if (Array.isArray(rawData)) {
    return {
      items: rawData,
      meta: {
        pageIndex: 1,
        pageSize: rawData.length || 100,
        totalCount: rawData.length,
        totalPages: 1,
      },
    };
  }

  const firstItems: T[] = Array.isArray(rawData?.items) ? rawData.items : [];
  const totalCount = Number(rawData?.totalCount) || firstItems.length;
  const pageSize = Number(rawData?.pageSize) || 100;
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  if (totalPages <= 1) {
    return {
      items: firstItems,
      meta: { pageIndex: 1, pageSize, totalCount, totalPages },
    };
  }

  const allItems: T[] = [...firstItems];
  // [F6] Tải toàn bộ trang theo batch (kích thước 5) để không nghẽn connection pool và không bỏ sót dòng nào
  const BATCH_SIZE = 5;
  for (let page = 2; page <= totalPages; page += BATCH_SIZE) {
    const batchPromises: Promise<any>[] = [];
    const endPage = Math.min(page + BATCH_SIZE - 1, totalPages);
    for (let pIdx = page; pIdx <= endPage; pIdx++) {
      const p = new URLSearchParams();
      Object.entries({ ...baseParams, pageIndex: pIdx, pageSize }).forEach(([k, v]) => {
        if (v !== undefined && String(v).trim() !== "") p.set(k, String(v));
      });
      batchPromises.push(httpClient.get(`${endpoint}?${p.toString()}`));
    }

    const batchResponses = await Promise.all(batchPromises);
    for (const r of batchResponses) {
      const pageItems: T[] = Array.isArray(r.data?.items) ? r.data.items : [];
      allItems.push(...pageItems);
    }
  }

  return {
    items: allItems,
    meta: { pageIndex: 1, pageSize, totalCount, totalPages },
  };
}

export const apiAdapter = {
  getOrdersStorageKey(tenantId?: string) {
    return getTenantOrdersStorageKey(tenantId);
  },
  getPendingReceiptStorageKey(tenantId?: string) {
    return getPendingReceiptStorageKey(tenantId);
  },
  getPendingReservationStorageKey(tenantId?: string) {
    return getPendingReservationStorageKey(tenantId);
  },
  getPendingOrderStorageKey(tenantId?: string, userId?: string) {
    return getPendingOrderStorageKey(tenantId, userId);
  },

  // --- CATALOG & PRODUCTS ---
  async getSkus(): Promise<ApiResult<Sku[]>> {
    try {
      // [A05 & A09] Không nuốt lỗi .catch(() => []) và nạp đủ tất cả các trang (pageSize=100)
      const [prodRes, balResult] = await Promise.all([
        httpClient.get("/api/products"),
        fetchAllPages<any>("/api/inventory/balances"),
      ]);

      const products = prodRes.data;
      const rawBalances = balResult.items;

      // [A02 & A03] Tổng hợp số dư theo từng chi nhánh để tính chính xác TotalValue = sum(OnHand_branch * AverageCost_branch)
      const balanceMap = new Map<
        string,
        {
          onHand: number;
          reserved: number;
          totalValue: number;
          weightedAverageCost: number;
          branchBalances: BranchStockBreakdown[];
        }
      >();

      rawBalances.forEach((b: any) => {
        const onHand = Number(b.onHand) || 0;
        const reserved = Number(b.reserved) || 0;
        const avgCost = Number(b.averageCost) || 0;
        const branchValue = onHand * avgCost;

        const prev = balanceMap.get(b.skuId) || {
          onHand: 0,
          reserved: 0,
          totalValue: 0,
          weightedAverageCost: 0,
          branchBalances: [],
        };

        const nextOnHand = prev.onHand + onHand;
        const nextReserved = prev.reserved + reserved;
        const nextTotalValue = prev.totalValue + branchValue;
        const nextWeightedCost =
          nextOnHand > 0 ? Math.round(nextTotalValue / nextOnHand) : avgCost || prev.weightedAverageCost;

        const nextBranches: BranchStockBreakdown[] = [
          ...prev.branchBalances,
          {
            branchId: b.branchId || "",
            branchName: b.branchName || "Chi nhánh chính",
            warehouseId: b.warehouseId || undefined,
            warehouseName: b.warehouseName || "Tất cả các kho",
            onHand,
            reserved,
            available: Math.max(0, onHand - reserved),
            averageCost: avgCost,
          },
        ];

        balanceMap.set(b.skuId, {
          onHand: nextOnHand,
          reserved: nextReserved,
          totalValue: nextTotalValue,
          weightedAverageCost: nextWeightedCost,
          branchBalances: nextBranches,
        });
      });

      const list: Sku[] = [];

      if (Array.isArray(products)) {
        products.forEach((p: any) => {
          const prodActive = p.isActive !== false;
          if (Array.isArray(p.variants) && p.variants.length > 0) {
            p.variants.forEach((v: any) => {
              const bal = balanceMap.get(v.id);
              const variantActive = prodActive && v.isActive !== false;
              const fallbackCost = Number(v.costPrice) || 0;
              const onHand = bal ? bal.onHand : 0;
              const averageCost = bal ? bal.weightedAverageCost : fallbackCost;
              const totalInventoryValue = bal ? bal.totalValue : onHand * fallbackCost;

              list.push({
                id: v.id,
                productId: p.id,
                code: v.sku || v.skuCode || `SKU-${v.id?.slice(0, 8)}`,
                name: p.name,
                variant: v.variantName || "",
                barcode: v.barcode || "",
                sellingPrice: Number(v.retailPrice) || 0,
                wholesalePrice: Number(v.wholesalePrice) || 0,
                averageCost,
                onHand,
                reserved: bal ? bal.reserved : 0,
                threshold: 5,
                image: "",
                isActive: variantActive,
                productIsActive: prodActive,
                categoryId: p.categoryId || null,
                categoryName: p.categoryName || null,
                brandId: p.brandId || null,
                brandName: p.brandName || null,
                totalInventoryValue,
                branchBalances: bal?.branchBalances || [],
              });
            });
          } else {
            list.push({
              id: p.id,
              productId: p.id,
              code: p.code || `PROD-${p.id?.slice(0, 8)}`,
              name: p.name,
              variant: "",
              barcode: "",
              sellingPrice: 0,
              wholesalePrice: 0,
              averageCost: 0,
              onHand: 0,
              reserved: 0,
              threshold: 5,
              image: "",
              isActive: prodActive,
              productIsActive: prodActive,
              categoryId: p.categoryId || null,
              categoryName: p.categoryName || null,
              brandId: p.brandId || null,
              brandName: p.brandName || null,
              totalInventoryValue: 0,
              branchBalances: [],
            });
          }
        });
      }
      return { data: list };
    } catch (err: any) {
      // [A09] Trả đúng lỗi kết nối/API về UI thay vì mảng rỗng gây hiểu nhầm dữ liệu = 0
      return {
        error: {
          code: !err?.response ? "NETWORK_ERROR" : "INVALID_STATE",
          message:
            err?.response?.data?.message ||
            "Không thể tải danh sách sản phẩm & tồn kho từ máy chủ. Vui lòng kiểm tra kết nối và tải lại trang.",
        },
      };
    }
  },

  async getCategories() {
    try {
      const res = await httpClient.get("/api/categories");
      const raw = Array.isArray(res.data) ? res.data : [];
      const flatList: Array<{ id: string; name: string; rawName: string; parentId?: string | null }> = [];
      const traverse = (items: any[], level = 0) => {
        for (const item of items) {
          flatList.push({
            id: item.id,
            name: level > 0 ? `${"— ".repeat(level)}${item.name}` : item.name,
            rawName: item.name,
            parentId: item.parentId || null,
          });
          if (Array.isArray(item.subCategories) && item.subCategories.length > 0) {
            traverse(item.subCategories, level + 1);
          }
        }
      };
      traverse(raw);
      return { data: flatList };
    } catch (err: any) {
      return {
        error: { message: err?.response?.data?.message || "Không thể tải danh mục hàng hóa từ máy chủ." },
      };
    }
  },

  async createCategory(payload: { name: string; code?: string; parentId?: string | null }) {
    try {
      const res = await httpClient.post("/api/categories", {
        name: payload.name.trim(),
        description: payload.code?.trim() || null,
        parentId: payload.parentId || null,
      });
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message:
            err?.response?.data?.message ||
            err?.response?.data?.errors?.[0] ||
            "Tạo danh mục thất bại (kiểm tra không lặp quan hệ cha-con)",
        },
      };
    }
  },

  async updateCategory(id: string, payload: { name: string; description?: string; parentId?: string | null; isActive?: boolean }) {
    try {
      const res = await httpClient.put(`/api/categories/${id}`, {
        name: payload.name.trim(),
        description: payload.description?.trim() || null,
        parentId: payload.parentId || null,
        isActive: payload.isActive ?? true,
      });
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message:
            err?.response?.data?.message ||
            err?.response?.data?.errors?.[0] ||
            "Cập nhật danh mục thất bại (Phát hiện vòng lặp quan hệ cha-con đệ quy)",
        },
      };
    }
  },

  async getBrands() {
    try {
      const res = await httpClient.get("/api/brands");
      return { data: Array.isArray(res.data) ? res.data : [] };
    } catch (err: any) {
      return {
        error: { message: err?.response?.data?.message || "Không thể tải danh sách thương hiệu từ máy chủ." },
      };
    }
  },

  async createBrand(payload: { name: string; code?: string; description?: string }) {
    try {
      const res = await httpClient.post("/api/brands", {
        name: payload.name.trim(),
        description: payload.description || payload.code || null,
      });
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message:
            err?.response?.data?.message ||
            err?.response?.data?.errors?.[0] ||
            "Tạo thương hiệu thất bại",
        },
      };
    }
  },

  async updateBrand(id: string, payload: { name: string; description?: string; isActive?: boolean }) {
    try {
      const res = await httpClient.put(`/api/brands/${id}`, {
        name: payload.name.trim(),
        description: payload.description?.trim() || null,
        isActive: payload.isActive ?? true,
      });
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message:
            err?.response?.data?.message ||
            err?.response?.data?.errors?.[0] ||
            "Cập nhật thương hiệu thất bại",
        },
      };
    }
  },

  async getProducts() {
    return this.getSkus();
  },

  async createProduct(payload: {
    name: string;
    categoryId?: string;
    brandId?: string;
    description?: string;
    variants?: Array<{
      sku: string;
      variantName?: string;
      barcode?: string;
      retailPrice: number;
      wholesalePrice?: number;
      color?: string;
      size?: string;
    }>;
  }) {
    try {
      const res = await httpClient.post("/api/products", {
        name: payload.name,
        description: payload.description,
        categoryId: payload.categoryId || null,
        brandId: payload.brandId || null,
        variants:
          payload.variants?.map((v) => ({
            sku: v.sku.trim(),
            variantName: v.variantName?.trim() || "Mặc định",
            barcode: v.barcode?.trim() || null,
            retailPrice: Number(v.retailPrice) || 0,
            wholesalePrice: Number(v.wholesalePrice) || 0,
            color: v.color || null,
            size: v.size || null,
          })) || [],
      });
      return { data: res.data };
    } catch (err: any) {
      const msg =
        err?.response?.data?.message ||
        err?.response?.data?.errors?.[0] ||
        "Lỗi tạo sản phẩm";
      return {
        error: {
          code: err?.response?.status === 409 ? "SKU_ALREADY_EXISTS" : "INVALID_STATE",
          message: msg,
        },
      };
    }
  },

  async updateVariantPrices(
    productId: string,
    variantId: string,
    payload: { retailPrice: number; wholesalePrice: number }
  ) {
    try {
      const res = await httpClient.put(`/api/products/${productId}/variants/${variantId}/prices`, {
        retailPrice: Number(payload.retailPrice) || 0,
        wholesalePrice: Number(payload.wholesalePrice) || 0,
      });
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message:
            err?.response?.data?.message ||
            err?.response?.data?.errors?.[0] ||
            "Cập nhật giá bán biến thể thất bại",
        },
      };
    }
  },

  async toggleVariantStatus(productId: string, variantId: string) {
    try {
      const res = await httpClient.put(`/api/products/${productId}/variants/${variantId}/toggle-status`);
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message: err?.response?.data?.message || "Không thể đổi trạng thái biến thể SKU",
        },
      };
    }
  },

  async deleteProduct(productId: string) {
    try {
      const res = await httpClient.delete(`/api/products/${productId}`);
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message: err?.response?.data?.message || "Vô hiệu hóa sản phẩm thất bại",
        },
      };
    }
  },

  // --- INVENTORY & BALANCES ---
  // [A05 & A09] Truyền pageSize=100 và nạp đủ các trang hoặc lọc theo Chi nhánh / Kho cụ thể
  async getBalances(filter?: {
    branchId?: string;
    warehouseId?: string;
    searchTerm?: string;
    pageIndex?: number;
    pageSize?: number;
  }): Promise<ApiResult<Sku[]>> {
    try {
      const result = await fetchAllPages<any>("/api/inventory/balances", {
        branchId: filter?.branchId && filter.branchId !== "all" ? filter.branchId : undefined,
        warehouseId: filter?.warehouseId && filter.warehouseId !== "all" ? filter.warehouseId : undefined,
        searchTerm: filter?.searchTerm || undefined,
      });

      const list: Sku[] = result.items.map((b: any) => {
        const onHand = Number(b.onHand) || 0;
        const avgCost = Number(b.averageCost) || 0;
        return {
          id: b.skuId,
          productId: b.productId || b.skuId,
          code: b.skuCode || "",
          name: b.productName || "",
          variant: b.variantName || "",
          barcode: b.barcode || "",
          sellingPrice: Number(b.retailPrice) || 0,
          wholesalePrice: Number(b.wholesalePrice) || 0,
          averageCost: avgCost,
          onHand,
          reserved: Number(b.reserved) || 0,
          threshold: 5,
          image: "",
          isActive: b.isActive !== false,
          branchId: b.branchId || undefined,
          branchName: b.branchName || "Chi nhánh chính",
          warehouseId: b.warehouseId || undefined,
          warehouseName: b.warehouseName || "Tất cả các kho",
          totalInventoryValue: onHand * avgCost,
        };
      });

      return { data: list, meta: result.meta };
    } catch (err: any) {
      return {
        error: {
          code: !err?.response ? "NETWORK_ERROR" : "INVALID_STATE",
          message:
            err?.response?.data?.message ||
            "Không thể tải dữ liệu tồn kho từ máy chủ. Vui lòng kiểm tra kết nối mạng và thử lại.",
        },
      };
    }
  },

  async getSkuBalanceAtBranch(skuId: string, branchId: string) {
    try {
      const res = await httpClient.get(`/api/inventory/balances/sku/${skuId}/branch/${branchId}`);
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message: err?.response?.data?.message || "Không tìm thấy tồn kho SKU tại chi nhánh",
        },
      };
    }
  },

  async getLedger(filter?: {
    branchId?: string;
    warehouseId?: string;
    type?: string;
  }): Promise<ApiResult<LedgerEntry[]>> {
    try {
      const result = await fetchAllPages<any>("/api/inventory/ledger", {
        branchId: filter?.branchId && filter.branchId !== "all" ? filter.branchId : undefined,
        warehouseId: filter?.warehouseId && filter.warehouseId !== "all" ? filter.warehouseId : undefined,
        type: filter?.type && filter.type !== "all" ? filter.type : undefined,
      });

      const list: LedgerEntry[] = result.items.map((l: any) => ({
        id: l.id,
        createdAt: l.createdAt
          ? new Date(l.createdAt).toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" })
          : "",
        branchId: l.branchId,
        branchName: l.branchName || "Chi nhánh chính",
        warehouseId: l.warehouseId,
        warehouse: l.warehouseName ? `${l.branchName ? l.branchName + " / " : ""}${l.warehouseName}` : "Kho chính",
        skuId: l.skuId,
        sku: `${l.skuCode} · ${l.productName}`,
        direction: (l.type === "IN" ? "IN" : "OUT") as "IN" | "OUT",
        // IN/OUT is a direction; transfers and stock counts can have either direction.
        transaction: ({
          GoodsReceipt: "Nhập kho nhà cung cấp",
          Order: "Xuất kho đơn hàng",
          StockTransfer: "Điều chuyển kho",
          StockCount: "Kiểm kê cân kho",
          InventoryAdjustment: "Điều chỉnh tồn kho",
          SalesReturn: "Khách trả hàng",
        } as Record<string, string>)[l.documentType] || (
          l.receiptItemId || /^GR-/.test(l.documentNumber || "") ? "Nhập kho nhà cung cấp" :
          /^(ORD|POS)-/.test(l.documentNumber || "") ? "Xuất kho đơn hàng" :
          /^TRF-/.test(l.documentNumber || "") ? "Điều chuyển kho" :
          /^CNT-/.test(l.documentNumber || "") ? "Kiểm kê cân kho" : "Biến động kho"
        ),
        quantity: Number(l.quantity) || 0,
        unitCost: Number(l.unitCost) || 0,
        balanceAfter: Number(l.balanceAfter) || 0,
        reference: l.documentNumber || l.referenceId || "—",
      }));

      return { data: list, meta: result.meta };
    } catch (err: any) {
      return {
        error: {
          code: !err?.response ? "NETWORK_ERROR" : "INVALID_STATE",
          message:
            err?.response?.data?.message ||
            "Không thể tải sổ cái kho (Ledger) từ máy chủ. Vui lòng thử lại.",
        },
      };
    }
  },

  // [F2] Hỗ trợ Idempotency-Key và requestKey (UUID) chống giữ chỗ trùng lặp khi retry
  async reserveStock(payload: {
    branchId: string;
    warehouseId: string;
    skuId: string;
    quantity: number;
    durationMinutes?: number;
    idempotencyKey?: string;
  }) {
    try {
      const headers: Record<string, string> = {};
      if (payload.idempotencyKey) {
        headers["Idempotency-Key"] = payload.idempotencyKey;
        headers["X-Request-Key"] = payload.idempotencyKey;
      }
      const body: Record<string, any> = {
        branchId: payload.branchId,
        warehouseId: payload.warehouseId,
        skuId: payload.skuId,
        quantity: Number(payload.quantity) || 1,
        durationMinutes: payload.durationMinutes || 15,
      };
      if (payload.idempotencyKey) {
        body.requestKey = payload.idempotencyKey;
      }
      const res = await httpClient.post(
        "/api/inventory/reserve",
        body,
        { headers }
      );
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          code: err?.response?.status === 409 ? "INSUFFICIENT_STOCK" : "INVALID_STATE",
          message:
            err?.response?.data?.message ||
            err?.response?.data?.errors?.[0] ||
            "Giữ chỗ tồn kho thất bại",
        },
      };
    }
  },

  // --- GOODS RECEIPTS & SUPPLIERS ---
  async getSuppliers() {
    try {
      const res = await httpClient.get("/api/suppliers");
      return { data: Array.isArray(res.data) ? res.data : [] };
    } catch (err: any) {
      return {
        error: {
          message: err?.response?.data?.message || "Không thể tải danh sách nhà cung cấp từ máy chủ.",
        },
      };
    }
  },

  async createSupplier(payload: { name: string; code?: string; phone?: string; email?: string }) {
    try {
      const res = await httpClient.post("/api/suppliers", {
        name: payload.name.trim(),
        code: payload.code?.trim() || null,
        phone: payload.phone?.trim() || null,
        email: payload.email?.trim() || null,
      });
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message:
            err?.response?.data?.message ||
            err?.response?.data?.errors?.[0] ||
            "Tạo nhà cung cấp thất bại",
        },
      };
    }
  },

  async getGoodsReceipts(pageIndex = 1) {
    try {
      const res = await httpClient.get(`/api/goods-receipts?pageIndex=${pageIndex}&pageSize=20`);
      if (!Array.isArray(res.data) && !Array.isArray(res.data?.items)) {
        throw new Error("Dữ liệu danh sách phiếu nhập không đúng định dạng.");
      }
      const raw = Array.isArray(res.data) ? res.data : Array.isArray(res.data?.items) ? res.data.items : [];
      return { data: raw, totalPages: Number(res.data?.totalPages) || 1 };
    } catch (err: any) {
      return {
        error: {
          message: err?.response?.data?.message || err?.message || "Không thể tải danh sách phiếu nhập kho.",
        },
      };
    }
  },

  // [F1] Truy vấn chi tiết phiếu nhập kho để đối soát trạng thái (Draft / Confirmed)
  async getGoodsReceiptById(id: string) {
    try {
      const res = await httpClient.get(`/api/goods-receipts/${id}`);
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          code: err?.response?.status === 404 ? "NOT_FOUND" : "API_ERROR",
          status: err?.response?.status,
          message:
            err?.response?.data?.message ||
            err?.response?.data?.errors?.[0] ||
            "Không thể tải thông tin chi tiết phiếu nhập kho.",
        },
      };
    }
  },

  async createGoodsReceipt(payload: {
    supplierId?: string;
    branchId: string;
    warehouseId: string;
    items: Array<{
      skuId: string;
      quantity: number;
      unitPrice: number;
    }>;
    notes?: string;
    note?: string;
  }) {
    try {
      const res = await httpClient.post("/api/goods-receipts", {
        branchId: payload.branchId,
        warehouseId: payload.warehouseId,
        supplierId: payload.supplierId,
        note: payload.notes || payload.note || null,
        items: payload.items.map((item) => ({
          skuId: item.skuId,
          quantity: Number(item.quantity) || 1,
          unitPrice: Number(item.unitPrice) || 0,
        })),
      });
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message:
            err?.response?.data?.message ||
            err?.response?.data?.errors?.[0] ||
            "Lỗi tạo phiếu nhập kho",
        },
      };
    }
  },

  async confirmGoodsReceipt(receiptId: string) {
    try {
      const res = await httpClient.post(`/api/goods-receipts/${receiptId}/confirm`);
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message:
            err?.response?.data?.message ||
            err?.response?.data?.errors?.[0] ||
            "Lỗi xác nhận phiếu nhập",
        },
      };
    }
  },

  // --- BRANCHES & WAREHOUSES ---
  async getBranches() {
    try {
      const res = await httpClient.get("/api/branches");
      return { data: Array.isArray(res.data) ? res.data : [] };
    } catch (err: any) {
      return {
        error: {
          message: err?.response?.data?.message || "Không thể tải danh sách chi nhánh từ máy chủ.",
        },
      };
    }
  },

  async createBranch(payload: { name: string; address?: string; phone?: string }) {
    try {
      const res = await httpClient.post("/api/branches", payload);
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message: err?.response?.data?.message || err?.response?.data?.errors?.[0] || "Tạo chi nhánh thất bại",
        },
      };
    }
  },

  async updateBranch(id: string, payload: { name: string; address?: string; phone?: string; isActive: boolean }) {
    try {
      const res = await httpClient.put(`/api/branches/${id}`, payload);
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message: err?.response?.data?.message || err?.response?.data?.errors?.[0] || "Cập nhật chi nhánh thất bại",
        },
      };
    }
  },

  async toggleBranchStatus(id: string) {
    try {
      const res = await httpClient.put(`/api/branches/${id}/toggle-status`);
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message: err?.response?.data?.message || "Không thể đổi trạng thái chi nhánh",
        },
      };
    }
  },

  async getWarehouses(branchId?: string) {
    try {
      const url = branchId ? `/api/warehouses?branchId=${branchId}` : "/api/warehouses";
      const res = await httpClient.get(url);
      return { data: Array.isArray(res.data) ? res.data : [] };
    } catch (err: any) {
      return {
        error: {
          message: err?.response?.data?.message || "Không thể tải danh sách kho hàng từ máy chủ.",
        },
      };
    }
  },

  async createWarehouse(payload: {
    branchId: string;
    name: string;
    code?: string;
    address?: string;
    type?: string;
  }) {
    try {
      const res = await httpClient.post("/api/warehouses", {
        branchId: payload.branchId,
        name: payload.name.trim(),
      });
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message: err?.response?.data?.message || err?.response?.data?.errors?.[0] || "Tạo kho hàng thất bại",
        },
      };
    }
  },

  async toggleWarehouseStatus(id: string) {
    try {
      const res = await httpClient.put(`/api/warehouses/${id}/toggle-status`);
      return { data: res.data };
    } catch (err: any) {
      return {
        error: {
          message: err?.response?.data?.message || "Không thể đổi trạng thái kho hàng",
        },
      };
    }
  },

  // --- ORDERS & RESERVATIONS ---
  // [A01] Đọc/lưu đơn hàng gắn chặt theo khóa có tiền tố tenantId: oism_orders_${tenantId}
  async getOrders(tenantId?: string): Promise<ApiResult<Order[]>> {
    sessionStorage.removeItem("oism_session_orders");
    const activeTenant = tenantId || getActiveTenantId();
    if (!activeTenant || activeTenant === "guest") {
      return { data: [] };
    }
    const storageKey = getTenantOrdersStorageKey(activeTenant);
    const savedRaw = sessionStorage.getItem(storageKey);
    const savedOrders: Order[] = savedRaw
      ? (JSON.parse(savedRaw) as Order[]).filter((o) => o.tenantId === activeTenant)
      : [];

    try {
      const res = await httpClient.get("/api/orders");
      const apiOrders = Array.isArray(res.data) ? res.data : [];
      return { data: [...savedOrders, ...apiOrders] };
    } catch {
      return { data: savedOrders };
    }
  },

  saveSessionOrders(orders: Order[], tenantId?: string) {
    sessionStorage.removeItem("oism_session_orders");
    const activeTenant = tenantId || getActiveTenantId();
    if (!activeTenant || activeTenant === "guest") return;
    const storageKey = getTenantOrdersStorageKey(activeTenant);
    const tagged = orders.map((o) => ({ ...o, tenantId: activeTenant }));
    sessionStorage.setItem(storageKey, JSON.stringify(tagged));
  },

  // [A08 & A10 & F2] Hỗ trợ chọn rõ Chi nhánh & Kho cụ thể khi giữ chỗ (Reserve), idempotencyKey chống giữ trùng và chặn SKU ngừng hoạt động
  async checkout(
    sku: Sku,
    quantity: number,
    channel: Order["channel"] = "POS",
    customerName = "Khách lẻ tại quầy",
    options?: {
      branchId?: string;
      branchName?: string;
      warehouseId?: string;
      warehouseName?: string;
      tenantId?: string;
      idempotencyKey?: string;
      isRetry?: boolean;
    }
  ): Promise<ApiResult<{ orderCode: string }>> {
    if (sku.isActive === false) {
      return {
        error: {
          code: "INVALID_STATE",
          message: `Mã SKU ${sku.code} hiện đang ở trạng thái [Ngừng hoạt động] và không thể đưa vào đơn bán.`,
        },
      };
    }

    try {
      let branchId = options?.branchId || sku.branchId;
      let warehouseId = options?.warehouseId || sku.warehouseId;
      let branchName = options?.branchName || sku.branchName;
      let warehouseName = options?.warehouseName || sku.warehouseName;

      if (!branchId || !warehouseId) {
        const [brRes, whRes] = await Promise.all([this.getBranches(), this.getWarehouses()]);
        if (brRes.error || whRes.error) {
          return {
            error: {
              code: "NETWORK_ERROR",
              message: brRes.error?.message || whRes.error?.message || "Không thể tải thông tin kho để giữ chỗ.",
            },
          };
        }
        const preferredBranch = sku.branchBalances?.find((b) => b.available >= quantity)?.branchId || brRes.data?.[0]?.id;
        branchId = branchId || preferredBranch;
        const matchedBranchObj = brRes.data?.find((b: any) => b.id === branchId);
        branchName = branchName || matchedBranchObj?.name || "Chi nhánh chính";

        const matchedWh = whRes.data?.find((w: any) => w.branchId === branchId) || whRes.data?.[0];
        warehouseId = warehouseId || matchedWh?.id;
        warehouseName = warehouseName || matchedWh?.name || "Kho chính";
      }

      if (branchId && warehouseId) {
        // [F2] Kiểm tra tồn khả dụng thực tế tại đúng Kho được chọn trước khi gọi Reserve
        // CHÚ Ý: Nếu đây là thao tác RETRY (options?.isRetry), không dùng whAvailable phía client để chặn,
        // vì lần gọi trước có thể backend đã trừ khả dụng và tạo reservation thành công rồi!
        if (!options?.isRetry) {
          const whBalRes = await this.getBalances({ branchId, warehouseId });
          if (whBalRes.data) {
            const whSkuBal = whBalRes.data.find((item) => item.id === sku.id);
            const whAvailable = whSkuBal ? whSkuBal.onHand - whSkuBal.reserved : 0;
            if (whAvailable < quantity) {
              return {
                error: {
                  code: "INSUFFICIENT_STOCK",
                  message: `Kho "${warehouseName || warehouseId}" (${branchName || branchId}) chỉ còn khả dụng ${whAvailable} sản phẩm cho mã ${sku.code} (yêu cầu: ${quantity}). Vui lòng chọn kho khác hoặc nhập thêm hàng.`,
                },
              };
            }
          }
        }

        const res = await this.reserveStock({
          branchId,
          warehouseId,
          skuId: sku.id,
          quantity,
          durationMinutes: 15,
          idempotencyKey: options?.idempotencyKey,
        });

        if (res.error) {
          return {
            error: {
              code: "INSUFFICIENT_STOCK",
              message: res.error.message,
            },
          };
        }

        // Lấy giá vốn WAC của đúng chi nhánh xuất hàng
        const branchWac =
          sku.branchBalances?.find((b) => b.branchId === branchId)?.averageCost ?? sku.averageCost ?? 0;

        const activeTenant = options?.tenantId || getActiveTenantId();
        const orderCode = `ORD-${String(res.data?.orderId || Date.now()).slice(0, 8).toUpperCase()}`;
        const newOrder: Order = {
          id: res.data?.orderId || `ord-${Date.now()}`,
          code: orderCode,
          tenantId: activeTenant,
          channel,
          branchId,
          branch: `${branchName || "Chi nhánh"}${warehouseName ? ` / ${warehouseName}` : ""}`,
          warehouseId,
          warehouse: warehouseName,
          customer: customerName,
          status: "Reserved",
          itemCount: quantity,
          amount: sku.sellingPrice * quantity,
          cogs: branchWac * quantity,
          createdAt: new Date().toISOString(),
          expiresIn: "15:00",
          items: [
            {
              skuCode: sku.code,
              name: `${sku.name}${sku.variant ? ` (${sku.variant})` : ""}`,
              quantity,
              price: sku.sellingPrice,
              costSnapshot: branchWac,
            },
          ],
        };

        sessionStorage.removeItem("oism_session_orders");
        const storageKey = getTenantOrdersStorageKey(activeTenant);
        const savedRaw = sessionStorage.getItem(storageKey);
        const savedOrders: Order[] = savedRaw
          ? (JSON.parse(savedRaw) as Order[]).filter((o) => o.tenantId === activeTenant)
          : [];
        sessionStorage.setItem(storageKey, JSON.stringify([newOrder, ...savedOrders]));

        return { data: { orderCode } };
      }
    } catch (err: any) {
      return {
        error: {
          code: "INVALID_STATE",
          message: err?.message || "Lỗi kết nối khi thực hiện giữ chỗ tồn kho.",
        },
      };
    }

    return {
      error: {
        code: "INVALID_STATE",
        message: "Chưa tìm thấy Chi nhánh/Kho có số dư tồn kho cho SKU này. Vui lòng lập Phiếu nhập kho trước khi bán.",
      },
    };
  },
};
