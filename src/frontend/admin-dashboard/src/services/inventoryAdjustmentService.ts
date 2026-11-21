import { httpClient } from './httpClient';
import type { InventoryAdjustment } from '../types/inventoryAdjustment';

export const inventoryAdjustmentService = {
  async getById(id: string): Promise<InventoryAdjustment> {
    const res = await httpClient.get<InventoryAdjustment>(`/api/inventory/adjustments/${id}`);
    return res.data;
  },
};
