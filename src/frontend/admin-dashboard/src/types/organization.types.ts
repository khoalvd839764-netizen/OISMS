export interface BranchResponse {
  id: string;
  name: string;
  address?: string | null;
  phone?: string | null;
  isActive: boolean;
  warehouses?: WarehouseResponse[];
}

export interface CreateBranchRequest {
  name: string;
  address?: string | null;
  phone?: string | null;
}

export interface WarehouseResponse {
  id: string;
  branchId: string;
  branchName: string;
  name: string;
  isActive: boolean;
  createdAt?: string;
}

export interface CreateWarehouseRequest {
  branchId: string;
  name: string;
}

export interface SupplierResponse {
  id: string;
  name: string;
  code: string;
  contactName?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
  isActive: boolean;
}

export interface CreateSupplierRequest {
  name: string;
  code?: string | null;
  contactName?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
}
