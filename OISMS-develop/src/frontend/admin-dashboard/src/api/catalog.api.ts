import { apiClient } from './client';
import type {
  BrandResponse,
  CategoryResponse,
  CreateBrandRequest,
  CreateCategoryRequest,
  CreateProductRequest,
  CreateVariantRequest,
  ProductResponse,
  ProductVariantResponse,
  UpdateBrandRequest,
  UpdateCategoryRequest,
  UpdateProductRequest,
} from '../types/catalog.types';

export const catalogApi = {
  // Categories (/api/categories)
  async getCategories(): Promise<CategoryResponse[]> {
    const response = await apiClient.get<CategoryResponse[]>('/categories');
    return response.data;
  },

  async createCategory(payload: CreateCategoryRequest): Promise<CategoryResponse> {
    const response = await apiClient.post<CategoryResponse>('/categories', payload);
    return response.data;
  },

  async updateCategory(id: string, payload: UpdateCategoryRequest): Promise<CategoryResponse> {
    const response = await apiClient.put<CategoryResponse>(`/categories/${id}`, payload);
    return response.data;
  },

  async deleteCategory(id: string): Promise<{ message: string }> {
    const response = await apiClient.delete<{ message: string }>(`/categories/${id}`);
    return response.data;
  },

  // Brands (/api/brands)
  async getBrands(): Promise<BrandResponse[]> {
    const response = await apiClient.get<BrandResponse[]>('/brands');
    return response.data;
  },

  async createBrand(payload: CreateBrandRequest): Promise<BrandResponse> {
    const response = await apiClient.post<BrandResponse>('/brands', payload);
    return response.data;
  },

  async updateBrand(id: string, payload: UpdateBrandRequest): Promise<BrandResponse> {
    const response = await apiClient.put<BrandResponse>(`/brands/${id}`, payload);
    return response.data;
  },

  async deleteBrand(id: string): Promise<{ message: string }> {
    const response = await apiClient.delete<{ message: string }>(`/brands/${id}`);
    return response.data;
  },

  // Products & Variants (/api/products)
  async getProducts(params?: {
    search?: string;
    categoryId?: string;
    brandId?: string;
  }): Promise<ProductResponse[]> {
    const cleanParams: Record<string, string> = {};
    if (params?.search?.trim()) cleanParams.search = params.search.trim();
    if (params?.categoryId) cleanParams.categoryId = params.categoryId;
    if (params?.brandId) cleanParams.brandId = params.brandId;

    const response = await apiClient.get<ProductResponse[]>('/products', { params: cleanParams });
    return response.data;
  },

  async getProductById(id: string): Promise<ProductResponse> {
    const response = await apiClient.get<ProductResponse>(`/products/${id}`);
    return response.data;
  },

  async createProduct(payload: CreateProductRequest): Promise<ProductResponse> {
    const response = await apiClient.post<ProductResponse>('/products', payload);
    return response.data;
  },

  async updateProduct(id: string, payload: UpdateProductRequest): Promise<{ message: string }> {
    const response = await apiClient.put<{ message: string }>(`/products/${id}`, payload);
    return response.data;
  },

  async deleteProduct(id: string): Promise<{ message: string }> {
    const response = await apiClient.delete<{ message: string }>(`/products/${id}`);
    return response.data;
  },

  async createVariant(productId: string, payload: CreateVariantRequest): Promise<ProductVariantResponse> {
    const response = await apiClient.post<ProductVariantResponse>(
      `/products/${productId}/variants`,
      payload
    );
    return response.data;
  },

  async addVariant(productId: string, payload: CreateVariantRequest): Promise<ProductVariantResponse> {
    const response = await apiClient.post<ProductVariantResponse>(
      `/products/${productId}/variants`,
      payload
    );
    return response.data;
  },
};

/**
 * Sinh mã vạch chuẩn GS1 EAN-13 (mã quốc gia 893 Việt Nam + 9 chữ số ngẫu nhiên + 1 số kiểm tra Check Digit).
 * Đồng bộ 100% thuật toán với OISM.Application.Common.Helpers.BarcodeHelper.
 */
export function generateEan13Barcode(): string {
  let digits12 = '893';
  for (let i = 0; i < 9; i++) {
    digits12 += Math.floor(Math.random() * 10).toString();
  }
  const checkDigit = calculateEan13CheckDigit(digits12);
  return `${digits12}${checkDigit}`;
}

export function calculateEan13CheckDigit(digits12: string): number {
  let sum = 0;
  for (let i = 0; i < 12; i++) {
    const digit = parseInt(digits12[i], 10);
    sum += i % 2 === 0 ? digit : digit * 3;
  }
  return (10 - (sum % 10)) % 10;
}

export function isValidEan13(barcode: string): boolean {
  if (!/^\d{13}$/.test(barcode)) return false;
  const expected = calculateEan13CheckDigit(barcode.slice(0, 12));
  return parseInt(barcode[12], 10) === expected;
}
