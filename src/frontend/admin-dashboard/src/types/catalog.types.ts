export interface CategoryResponse {
  id: string;
  name: string;
  description?: string | null;
  parentId?: string | null;
  parentName?: string | null;
  isActive: boolean;
  subCategories?: CategoryResponse[];
}

export interface CreateCategoryRequest {
  name: string;
  description?: string | null;
  parentId?: string | null;
}

export interface UpdateCategoryRequest {
  name: string;
  description?: string | null;
  parentId?: string | null;
  isActive: boolean;
}

export interface BrandResponse {
  id: string;
  name: string;
  description?: string | null;
  isActive: boolean;
}

export interface CreateBrandRequest {
  name: string;
  description?: string | null;
}

export interface UpdateBrandRequest {
  name: string;
  description?: string | null;
  isActive: boolean;
}

export interface ProductVariantResponse {
  id: string;
  productId: string;
  sku: string;
  variantName: string;
  barcode?: string | null;
  retailPrice: number;
  wholesalePrice: number;
  costPrice: number;
  color?: string | null;
  size?: string | null;
  isActive: boolean;
}

export interface ProductResponse {
  id: string;
  name: string;
  description?: string | null;
  categoryId?: string | null;
  categoryName?: string | null;
  brandId?: string | null;
  brandName?: string | null;
  isActive: boolean;
  variants: ProductVariantResponse[];
}

export interface CreateVariantRequest {
  sku: string;
  variantName: string;
  barcode?: string | null;
  retailPrice: number;
  wholesalePrice: number;
  color?: string | null;
  size?: string | null;
}

export type CreateProductVariantRequest = CreateVariantRequest;

export interface CreateProductRequest {
  name: string;
  description?: string | null;
  categoryId?: string | null;
  brandId?: string | null;
  variants: CreateVariantRequest[];
}

export interface UpdateProductRequest {
  name: string;
  description?: string | null;
  categoryId?: string | null;
  brandId?: string | null;
  isActive: boolean;
}
