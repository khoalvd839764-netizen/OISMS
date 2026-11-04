import { httpClient } from './httpClient';
import type {
  Unit,
  CreateUnitRequest,
  SkuUnit,
  CreateSkuUnitRequest,
  UpdateSkuUnitRequest,
  SkuUnitFactorHistory,
} from '../types/units';

export interface ProblemDetailsError {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errorCode?: string;
  extensions?: Record<string, any>;
}

export function parseUnitApiError(error: any): ProblemDetailsError {
  if (error?.response?.data) {
    const data = error.response.data;
    return {
      type: data.type,
      title: data.title || 'Lỗi xử lý đơn vị tính',
      status: error.response.status,
      detail: data.detail || error.message || 'Lỗi xử lý yêu cầu.',
      errorCode: data.errorCode || data.extensions?.errorCode,
      extensions: data.extensions,
    };
  }
  return {
    title: 'Lỗi kết nối',
    detail: error?.message || 'Không thể kết nối đến máy chủ.',
    status: 0,
  };
}

export const unitService = {
  /**
   * Lấy danh sách toàn bộ đơn vị tính của Tenant
   */
  async getUnits(): Promise<Unit[]> {
    const res = await httpClient.get<Unit[]>('/api/units');
    return res.data;
  },

  /**
   * Lấy chi tiết đơn vị tính theo Id
   */
  async getUnitById(id: string): Promise<Unit> {
    const res = await httpClient.get<Unit>(`/api/units/${id}`);
    return res.data;
  },

  /**
   * Tạo mới một đơn vị tính trong danh mục chung
   */
  async createUnit(request: CreateUnitRequest): Promise<Unit> {
    const res = await httpClient.post<Unit>('/api/units', request);
    return res.data;
  },

  /**
   * Cập nhật thông tin đơn vị tính
   */
  async updateUnit(id: string, request: Partial<CreateUnitRequest & { isActive?: boolean }>): Promise<Unit> {
    const res = await httpClient.put<Unit>(`/api/units/${id}`, request);
    return res.data;
  },

  /**
   * Lấy danh sách cấu hình đơn vị tính (gốc và quy đổi) cho một biến thể SKU
   */
  async getSkuUnits(productId: string, variantId: string): Promise<SkuUnit[]> {
    const res = await httpClient.get<SkuUnit[]>(`/api/products/${productId}/variants/${variantId}/units`);
    return res.data;
  },

  /**
   * Thêm cấu hình đơn vị tính quy đổi cho SKU
   */
  async createSkuUnit(productId: string, variantId: string, request: CreateSkuUnitRequest): Promise<SkuUnit> {
    const res = await httpClient.post<SkuUnit>(
      `/api/products/${productId}/variants/${variantId}/units`,
      request
    );
    return res.data;
  },

  /**
   * Cập nhật cấu hình đơn vị tính cho SKU
   */
  async updateSkuUnit(
    productId: string,
    variantId: string,
    skuUnitId: string,
    request: UpdateSkuUnitRequest
  ): Promise<SkuUnit> {
    const res = await httpClient.put<SkuUnit>(
      `/api/products/${productId}/variants/${variantId}/units/${skuUnitId}`,
      request
    );
    return res.data;
  },

  /**
   * Tắt mềm cấu hình đơn vị tính cho SKU
   */
  async deleteSkuUnit(productId: string, variantId: string, skuUnitId: string): Promise<void> {
    await httpClient.delete(`/api/products/${productId}/variants/${variantId}/units/${skuUnitId}`);
  },

  /**
   * Lấy lịch sử biến động hệ số quy đổi của đơn vị tính SKU
   */
  async getSkuUnitHistory(
    productId: string,
    variantId: string,
    skuUnitId: string
  ): Promise<SkuUnitFactorHistory[]> {
    const res = await httpClient.get<SkuUnitFactorHistory[]>(
      `/api/products/${productId}/variants/${variantId}/units/${skuUnitId}/history`
    );
    return res.data;
  },
};
