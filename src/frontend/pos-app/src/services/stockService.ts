/**
 * Unified Stock Service - Delegates directly to authoritative stock store in api.ts
 * B02-F02 & B02-F03: App, ProductSearch, Cart, Realtime, and Reconnect all share the EXACT SAME authoritative store.
 */
import {
  type StockItemState,
  type StockRefreshResult,
  subscribeBranchStock as apiSubscribeBranchStock,
  setAuthoritativeStock as apiSetAuthoritativeStock,
  getCachedStock as apiGetCachedStock,
  registerSkuIdentity as apiRegisterSkuIdentity,
  markStockStale as apiMarkStockStale,
  invalidateAndRefetchStock as apiInvalidateAndRefetchStock,
  _resetStockState as apiResetStockState,
} from './api';

export type { StockItemState, StockRefreshResult };

export const subscribeBranchStock = apiSubscribeBranchStock;
export const setAuthoritativeStock = apiSetAuthoritativeStock;
export const getCachedStock = apiGetCachedStock;
export const registerSkuIdentity = apiRegisterSkuIdentity;
export const markStockStale = apiMarkStockStale;
export const invalidateAndRefetchStock = apiInvalidateAndRefetchStock;
export const _resetStockState = apiResetStockState;
