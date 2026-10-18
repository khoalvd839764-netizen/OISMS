import { httpClient } from "./httpClient";
import type { ApiResult, Order, Sku, LedgerEntry } from "../types/domain";

export const apiAdapter = {
  // --- CATALOG & PRODUCTS ---
  async getSkus(): Promise<ApiResult<Sku[]>> {
    try {
      const [prodRes, balRes] = await Promise.all([
        httpClient.get("/api/products"),
        httpClient.get("/api/inventory/balances").catch(() => ({ data: { items: [] } })),
      ]);
      const products = prodRes.data;
      const rawBalances = Array.isArray(balRes.data)
        ? balRes.data
        : Array.isArray(balRes.data?.items)
        ? balRes.data.items
        : [];

      // Tổng hợp số dư theo skuId (nếu có nhiều chi nhánh thì cộng dồn OnHand/Reserved)
      const balanceMap = new Map<string, { onHand: number; reserved: number; averageCost: number }>();
      rawBalances.forEach((b: any) => {
        const prev = balanceMap.get(b.skuId) || { onHand: 0, reserved: 0, averageCost: 0 };
        balanceMap.set(b.skuId, {
          onHand: prev.onHand + (Number(b.onHand) || 0),
          reserved: prev.reserved + (Number(b.reserved) || 0),
          averageCost: Number(b.averageCost) || prev.averageCost,
        });
      });

      const list: Sku[] = [];

      if (Array.isArray(products)) {
        products.forEach((p: any) => {
          if (Array.isArray(p.variants) && p.variants.length > 0) {
            p.variants.forEach((v: any) => {
              const bal = balanceMap.get(v.id);
              list.push({
                id: v.id,
                productId: p.id,
                code: v.sku || v.skuCode || `SKU-${v.id?.slice(0, 8)}`,
                name: p.name,
                variant: v.variantName || "",
                barcode: v.barcode || "",
                sellingPrice: Number(v.retailPrice) || 0,
                wholesalePrice: Number(v.wholesalePrice) || 0,
                averageCost: bal ? bal.averageCost : Number(v.costPrice) || 0,
                onHand: bal ? bal.onHand : 0,
                reserved: bal ? bal.reserved : 0,
                threshold: 5,
                image: "",
                categoryId: p.categoryId || null,
                categoryName: p.categoryName || null,
                brandId: p.brandId || null,
                brandName: p.brandName || null,
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
              categoryId: p.categoryId || null,
              categoryName: p.categoryName || null,
              brandId: p.brandId || null,
              brandName: p.brandName || null,
            });
          }
        });
      }
      return { data: list };
    } catch (err: any) {
      return {
        data: [],
        error: {
          code: "INVALID_STATE",
          message: err?.response?.data?.message || "Không thể tải danh sách sản phẩm từ máy chủ",
        },
      };
    }
  },

  async getCategories() {
    try {
      const res = await httpClient.get("/api/categories");
      const raw = Array.isArray(res.data) ? res.data : [];
      // Làm phẳng cây danh mục đệ quy kèm tiền tố phân cấp
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
        data: [],
        error: { message: err?.response?.data?.message || "Không thể tải danh mục" },
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
        data: [],
        error: { message: err?.response?.data?.message || "Không thể tải thương hiệu" },
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
        variants: payload.variants?.map((v) => ({
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
  async getBalances(): Promise<ApiResult<Sku[]>> {
    try {
      const res = await httpClient.get("/api/inventory/balances");
      const raw = Array.isArray(res.data) ? res.data : (Array.isArray(res.data?.items) ? res.data.items : []);
      const list: Sku[] = [];

      raw.forEach((b: any) => {
        list.push({
          id: b.skuId,
          productId: b.productId || b.skuId,
          code: b.skuCode || "",
          name: b.productName || "",
          variant: b.variantName || "",
          barcode: b.barcode || "",
          sellingPrice: Number(b.retailPrice) || 0,
          wholesalePrice: Number(b.wholesalePrice) || 0,
          averageCost: Number(b.averageCost) || 0,
          onHand: Number(b.onHand) || 0,
          reserved: Number(b.reserved) || 0,
          threshold: 5,
          image: "",
          branchId: b.branchId || undefined,
          branchName: b.branchName || "Chi nhánh chính",
          warehouseId: b.warehouseId || undefined,
          warehouseName: b.warehouseName || "Kho chính",
        });
      });
      return { data: list };
    } catch (err: any) {
      return {
        data: [],
        error: {
          code: "INVALID_STATE",
          message: err?.response?.data?.message || "Lỗi tải tồn kho",
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

  async getLedger(): Promise<ApiResult<LedgerEntry[]>> {
    try {
      const res = await httpClient.get("/api/inventory/ledger");
      const raw = Array.isArray(res.data) ? res.data : (Array.isArray(res.data?.items) ? res.data.items : []);
      const list: LedgerEntry[] = [];

      raw.forEach((l: any) => {
        list.push({
          id: l.id,
          createdAt: l.createdAt
            ? new Date(l.createdAt).toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" })
            : "",
          warehouse: l.warehouseName ? `${l.branchName ? l.branchName + " / " : ""}${l.warehouseName}` : "Kho chính",
          sku: `${l.skuCode} · ${l.productName}`,
          direction: (l.type === "IN" ? "IN" : "OUT") as "IN" | "OUT",
          transaction:
            l.receiptItemId || String(l.documentNumber || "").toUpperCase().startsWith("GR") || l.type === "IN"
              ? "Nhập kho nhà cung cấp"
              : "Xuất kho đơn hàng",
          quantity: Number(l.quantity) || 0,
          balanceAfter: Number(l.balanceAfter) || 0,
          reference: l.documentNumber || l.referenceId || "—",
        });
      });
      return { data: list };
    } catch (err: any) {
      return {
        data: [],
        error: {
          code: "INVALID_STATE",
          message: err?.response?.data?.message || "Lỗi tải sổ kho",
        },
      };
    }
  },

  async reserveStock(payload: {
    branchId: string;
    warehouseId: string;
    skuId: string;
    quantity: number;
    durationMinutes?: number;
  }) {
    try {
      const res = await httpClient.post("/api/inventory/reserve", {
        branchId: payload.branchId,
        warehouseId: payload.warehouseId,
        skuId: payload.skuId,
        quantity: Number(payload.quantity) || 1,
        durationMinutes: payload.durationMinutes || 15,
      });
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
    } catch {
      return { data: [] };
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

  async getGoodsReceipts() {
    try {
      const res = await httpClient.get("/api/goods-receipts");
      const raw = Array.isArray(res.data) ? res.data : (Array.isArray(res.data?.items) ? res.data.items : []);
      return { data: raw };
    } catch {
      return { data: [] };
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
    } catch {
      return { data: [] };
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
    } catch {
      return { data: [] };
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

  // --- POS SEARCH & BARCODE ---
  async posSearch(query: string, branchId?: string) {
    try {
      const params = new URLSearchParams({ query });
      if (branchId) params.set("branchId", branchId);
      const res = await httpClient.get(`/api/pos/search?${params.toString()}`);
      return { data: Array.isArray(res.data) ? res.data : [] };
    } catch (err: any) {
      return {
        data: [],
        error: { message: err?.response?.data?.message || "Lỗi tìm kiếm POS" },
      };
    }
  },

  async posGetByBarcode(barcode: string, branchId?: string) {
    try {
      const url = branchId
        ? `/api/pos/barcode/${encodeURIComponent(barcode)}?branchId=${branchId}`
        : `/api/pos/barcode/${encodeURIComponent(barcode)}`;
      const res = await httpClient.get(url);
      return { data: res.data };
    } catch (err: any) {
      return {
        error: { message: err?.response?.data?.message || "Không tìm thấy sản phẩm theo mã vạch" },
      };
    }
  },

  // --- ORDERS & RESERVATIONS ---
  async getOrders(): Promise<ApiResult<Order[]>> {
    const savedRaw = sessionStorage.getItem("oism_session_orders");
    const savedOrders: Order[] = savedRaw ? JSON.parse(savedRaw) : [];
    try {
      const res = await httpClient.get("/api/orders");
      const apiOrders = Array.isArray(res.data) ? res.data : [];
      return { data: [...savedOrders, ...apiOrders] };
    } catch {
      return { data: savedOrders };
    }
  },

  async checkout(sku: Sku, quantity: number, channel: Order["channel"] = "POS", customerName = "Khách lẻ tại quầy"): Promise<ApiResult<{ orderCode: string }>> {
    if (quantity > sku.onHand - sku.reserved) {
      return {
        error: {
          code: "INSUFFICIENT_STOCK",
          message: "Tồn khả dụng vừa thay đổi. Vui lòng kiểm tra lại giỏ hàng.",
        },
      };
    }

    try {
      let branchId = sku.branchId;
      let warehouseId = sku.warehouseId;

      if (!branchId || !warehouseId) {
        const [brRes, whRes] = await Promise.all([this.getBranches(), this.getWarehouses()]);
        branchId = branchId || brRes.data?.[0]?.id;
        const matchedWh = whRes.data?.find((w: any) => w.branchId === branchId) || whRes.data?.[0];
        warehouseId = warehouseId || matchedWh?.id;
      }

      if (branchId && warehouseId) {
        const res = await this.reserveStock({
          branchId,
          warehouseId,
          skuId: sku.id,
          quantity,
          durationMinutes: 15,
        });

        if (res.error) {
          return {
            error: {
              code: "INSUFFICIENT_STOCK",
              message: res.error.message,
            },
          };
        }

        const orderCode = `ORD-${String(res.data?.orderId || Date.now()).slice(0, 8).toUpperCase()}`;
        const newOrder: Order = {
          id: res.data?.orderId || `ord-${Date.now()}`,
          code: orderCode,
          channel,
          branch: sku.branchName || "Chi nhánh chính",
          customer: customerName,
          status: "Reserved",
          itemCount: quantity,
          amount: sku.sellingPrice * quantity,
          cogs: (sku.averageCost || 0) * quantity,
          createdAt: new Date().toISOString(),
          expiresIn: "15:00",
          items: [
            {
              skuCode: sku.code,
              name: `${sku.name}${sku.variant ? ` (${sku.variant})` : ""}`,
              quantity,
              price: sku.sellingPrice,
              costSnapshot: sku.averageCost || 0,
            },
          ],
        };

        const savedRaw = sessionStorage.getItem("oism_session_orders");
        const savedOrders: Order[] = savedRaw ? JSON.parse(savedRaw) : [];
        sessionStorage.setItem("oism_session_orders", JSON.stringify([newOrder, ...savedOrders]));

        return { data: { orderCode } };
      }
    } catch {
      // Fallback nếu chưa cấu hình kho
    }

    return {
      error: {
        code: "INVALID_STATE",
        message: "Chưa tìm thấy Chi nhánh/Kho có số dư tồn kho cho SKU này. Vui lòng lập Phiếu nhập kho trước khi bán.",
      },
    };
  },
};

