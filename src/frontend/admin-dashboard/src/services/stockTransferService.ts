import axios from 'axios';
import type {
  StockTransfer,
  CreateStockTransferRequest,
  RejectStockTransferRequest,
} from '../types/stockTransfer';

const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api',
  headers: {
    'Content-Type': 'application/json',
  },
});

export const stockTransferService = {
 
  async getTransfers(): Promise<StockTransfer[]> {
    const response = await apiClient.get('/stock-transfers');
    return response.data;
  },

  
  async getTransferById(id: string): Promise<StockTransfer> {
    const response = await apiClient.get(`/stock-transfers/${id}`);
    return response.data;
  },

  
  async createTransfer(data: CreateStockTransferRequest): Promise<StockTransfer> {
    const response = await apiClient.post('/stock-transfers', data);
    return response.data;
  },

  
  async dispatchTransfer(id: string): Promise<void> {
    await apiClient.post(`/stock-transfers/${id}/dispatch`);
  },

 
  async receiveTransfer(id: string): Promise<void> {
    await apiClient.post(`/stock-transfers/${id}/receive`);
  },

  
  async rejectTransfer(id: string, data: RejectStockTransferRequest): Promise<void> {
    await apiClient.post(`/stock-transfers/${id}/reject`, data);
  },
};