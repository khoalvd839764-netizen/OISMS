export interface ApiError {
  code?: string;
  message: string;
  details?: Record<string, string[]>;
  status?: number;
}

export interface ApiResponse<T> {
  data?: T;
  message?: string;
  success?: boolean;
  error?: ApiError;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage?: boolean;
  hasNextPage?: boolean;
}
