import { apiClient } from './client';
import type {
  BranchResponse,
  CreateBranchRequest,
  CreateSupplierRequest,
  CreateWarehouseRequest,
  SupplierResponse,
  WarehouseResponse,
} from '../types/organization.types';

export const organizationApi = {
  // Branches (/api/branches)
  async getBranches(): Promise<BranchResponse[]> {
    const response = await apiClient.get<BranchResponse[]>('/branches');
    return response.data;
  },

  async createBranch(payload: CreateBranchRequest): Promise<BranchResponse> {
    const response = await apiClient.post<BranchResponse>('/branches', payload);
    return response.data;
  },

  async toggleBranchStatus(id: string): Promise<{ message: string; isActive: boolean }> {
    const response = await apiClient.put<{ message: string; isActive: boolean }>(
      `/branches/${id}/toggle-status`
    );
    return response.data;
  },

  // Warehouses (/api/warehouses)
  async getWarehouses(branchId?: string): Promise<WarehouseResponse[]> {
    const params = branchId ? { branchId } : undefined;
    const response = await apiClient.get<WarehouseResponse[]>('/warehouses', { params });
    return response.data;
  },

  async createWarehouse(payload: CreateWarehouseRequest): Promise<WarehouseResponse> {
    const response = await apiClient.post<WarehouseResponse>('/warehouses', payload);
    return response.data;
  },

  async toggleWarehouseStatus(id: string): Promise<{ message: string; id: string; isActive: boolean }> {
    const response = await apiClient.put<{ message: string; id: string; isActive: boolean }>(
      `/warehouses/${id}/toggle-status`
    );
    return response.data;
  },

  // Suppliers (/api/suppliers)
  async getSuppliers(search?: string): Promise<SupplierResponse[]> {
    const params = search?.trim() ? { search: search.trim() } : undefined;
    const response = await apiClient.get<SupplierResponse[]>('/suppliers', { params });
    return response.data;
  },

  async createSupplier(payload: CreateSupplierRequest): Promise<SupplierResponse> {
    const response = await apiClient.post<SupplierResponse>('/suppliers', payload);
    return response.data;
  },
};
