import React, { useState, useEffect, useTransition, useRef } from 'react';
import { Search, Barcode, Loader2, Plus, AlertCircle, CheckCircle2, RefreshCw } from 'lucide-react';
import type { PosProductSearchResponse } from '../types/pos';
import { searchPosProducts, scanPosBarcode } from '../services/api';
import {
  subscribeBranchStock,
  setAuthoritativeStock,
  getCachedStock,
  registerSkuIdentity,
  invalidateAndRefetchStock,
  issueStockReadTicket,
  type StockItemState,
} from '../services/stockService';

interface ProductSearchProps {
  branchId: string;
  onAddToCart: (product: PosProductSearchResponse) => void;
  searchInputRef: React.RefObject<HTMLInputElement | null>;
  onLookupLatency?: (latencyMs: number) => void;
}

export const ProductSearch: React.FC<ProductSearchProps> = ({
  branchId,
  onAddToCart,
  searchInputRef,
  onLookupLatency,
}) => {
  const [searchTerm, setSearchTerm] = useState('');
  const [results, setResults] = useState<PosProductSearchResponse[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [, startTransition] = useTransition();
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successToast, setSuccessToast] = useState<string | null>(null);
  const [showDropdown, setShowDropdown] = useState(false);
  const [stockMap, setStockMap] = useState<Record<string, StockItemState>>({});
  const [retryingKeys, setRetryingKeys] = useState<Record<string, boolean>>({});

  // Race condition protection: Theo dõi sequential requestId và abort controller
  const latestRequestIdRef = useRef<number>(0);
  const currentAbortControllerRef = useRef<AbortController | null>(null);
  const currentBranchRef = useRef<string>(branchId);
  currentBranchRef.current = branchId;

  // Dọn dẹp request khi unmount
  useEffect(() => {
    return () => {
      latestRequestIdRef.current++;
      if (currentAbortControllerRef.current) {
        currentAbortControllerRef.current.abort();
      }
    };
  }, []);

  // Đăng ký nhận cập nhật tồn kho từ stockService cho chi nhánh hiện tại (POS-STOCK-F01 & T03-AC-06)
  useEffect(() => {
    if (!branchId) {
      setStockMap({});
      return;
    }

    const unsubscribe = subscribeBranchStock(branchId, (latestStock) => {
      setStockMap(latestStock);

      // Cập nhật ngay lập tức vào danh sách results đang hiển thị nếu có mặt hàng khớp
      setResults((prevResults) => {
        let changed = false;
        const next = prevResults.map((item) => {
          const entry = latestStock[item.variantId] || latestStock[item.sku];
          if (entry && entry.available !== item.available) {
            changed = true;
            return {
              ...item,
              available: entry.available,
              onHand: entry.onHand ?? item.onHand,
              reserved: entry.reserved ?? item.reserved,
            };
          }
          return item;
        });
        return changed ? next : prevResults;
      });
    });

    return () => {
      unsubscribe();
    };
  }, [branchId]);

  // Khi đổi chi nhánh: Vô hiệu hóa ngay lập tức mọi request đang bay, xóa trắng kết quả tìm kiếm và từ khóa cũ (T05-AC-06 & B02-F05)
  useEffect(() => {
    latestRequestIdRef.current++;
    setSearchTerm('');
    setResults([]);
    setShowDropdown(false);
    setErrorMessage(null);
    if (currentAbortControllerRef.current) {
      currentAbortControllerRef.current.abort();
    }
  }, [branchId]);

  // Debounced search với chống Race Condition (T05-AC-06: Gõ nhanh hoặc response đảo thứ tự không ghi đè kết quả mới)
  useEffect(() => {
    // B02-F05: Vô hiệu hóa ngay lập tức callback cũ khi query thay đổi hoặc bị xóa
    const requestId = ++latestRequestIdRef.current;
    if (currentAbortControllerRef.current) {
      currentAbortControllerRef.current.abort();
    }

    const trimmed = searchTerm.trim();
    if (!trimmed) {
      setResults([]);
      setShowDropdown(false);
      setIsSearching(false);
      return;
    }

    const abortController = new AbortController();
    currentAbortControllerRef.current = abortController;

    const timer = setTimeout(async () => {
      // Nếu trong thời gian debounce (200ms) query đã bị đổi hoặc hủy -> thoát ngay
      if (
        requestId !== latestRequestIdRef.current ||
        abortController.signal.aborted ||
        branchId !== currentBranchRef.current
      ) {
        return;
      }

      const requestBranchId = branchId;
      const searchTicket = issueStockReadTicket();
      setIsSearching(true);
      setErrorMessage(null);

      const startTime = performance.now();

      try {
        const data = await searchPosProducts(trimmed, requestBranchId);
        const duration = performance.now() - startTime;
        onLookupLatency?.(duration);

        // Kiểm tra xem request này có còn là request mới nhất và đúng chi nhánh không
        if (
          requestId !== latestRequestIdRef.current ||
          requestBranchId !== currentBranchRef.current ||
          abortController.signal.aborted
        ) {
          return; // Hủy kết quả cũ hoặc lạc hậu
        }

        // Đăng ký danh tính và cập nhật số dư vào stockService với searchTicket
        for (const item of data) {
          registerSkuIdentity(item.variantId, item.sku, item.barcode || undefined);
          if (item.available !== undefined && item.available !== null) {
            setAuthoritativeStock(requestBranchId, item.variantId, item.available, {
              onHand: item.onHand,
              reserved: item.reserved,
              skuCode: item.sku,
              barcode: item.barcode || undefined,
              skuId: item.variantId,
            }, searchTicket);
            if (item.sku) {
              setAuthoritativeStock(requestBranchId, item.sku, item.available, {
                onHand: item.onHand,
                reserved: item.reserved,
                skuCode: item.sku,
                barcode: item.barcode || undefined,
                skuId: item.variantId,
              }, searchTicket);
            }
          }
        }

        startTransition(() => {
          setResults(data);
          setShowDropdown(true);
        });
      } catch (err: any) {
        if (
          requestId === latestRequestIdRef.current &&
          requestBranchId === currentBranchRef.current &&
          !abortController.signal.aborted
        ) {
          setErrorMessage(err.message || 'Lỗi tra cứu sản phẩm');
        }
      } finally {
        if (requestId === latestRequestIdRef.current) {
          setIsSearching(false);
        }
      }
    }, 200);

    return () => {
      clearTimeout(timer);
      abortController.abort();
    };
  }, [searchTerm, branchId, onLookupLatency]);

  // Handle enter key (Barcode or exact match)
  const handleKeyDown = async (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      const code = searchTerm.trim();
      if (!code) return;

      const requestId = ++latestRequestIdRef.current;
      const requestBranchId = branchId;
      const barcodeTicket = issueStockReadTicket();

      setIsSearching(true);
      setErrorMessage(null);

      const startTime = performance.now();

      // Tra cứu barcode chính xác từ API
      try {
        const item = await scanPosBarcode(code, requestBranchId);
        const duration = performance.now() - startTime;
        onLookupLatency?.(duration);

        // B02-F05: Guard cả requestId và currentBranchRef khi branch đổi giữa lúc await
        if (
          requestId !== latestRequestIdRef.current ||
          requestBranchId !== currentBranchRef.current
        ) {
          return;
        }

        // B02-F02 & B02-F04: Phản hồi API barcode mới nhất cập nhật theo barcodeTicket phát hành khi bắt đầu read
        const freshAvailable = item.available ?? 0;

        // Cập nhật stock store và danh tính SKU với ticket
        const committedVariant = setAuthoritativeStock(requestBranchId, item.variantId, freshAvailable, {
          onHand: item.onHand,
          reserved: item.reserved,
          skuId: item.variantId,
          skuCode: item.sku,
          barcode: item.barcode || undefined,
        }, barcodeTicket);
        if (item.sku) {
          setAuthoritativeStock(requestBranchId, item.sku, freshAvailable, {
            onHand: item.onHand,
            reserved: item.reserved,
            skuId: item.variantId,
            skuCode: item.sku,
            barcode: item.barcode || undefined,
          }, barcodeTicket);
        }

        // B02-F04: Nếu response barcode trả về sau một refetch/write mới hơn -> dùng authoritative stock mới nhất trong cache
        let effectiveAvailable = freshAvailable;
        if (!committedVariant) {
          const latestCached = getCachedStock(requestBranchId, item.variantId) || (item.sku ? getCachedStock(requestBranchId, item.sku) : null);
          if (latestCached?.isStale) {
            setErrorMessage(latestCached.error || 'Dữ liệu tồn chưa đồng bộ do lỗi kết nối. Bấm để thử lại.');
            return;
          }
          effectiveAvailable = latestCached ? latestCached.available : freshAvailable;
        }

        if (effectiveAvailable <= 0) {
          setErrorMessage(`Sản phẩm "${item.productName}" (${item.sku}) đã hết hàng trong kho chi nhánh (tồn khả dụng = 0).`);
          return;
        }

        onAddToCart({
          ...item,
          available: effectiveAvailable,
        });
        triggerSuccess(`Đã thêm: ${item.productName} (${item.sku})`);
        setSearchTerm('');
        setShowDropdown(false);
      } catch (err: any) {
        if (
          requestId === latestRequestIdRef.current &&
          requestBranchId === currentBranchRef.current
        ) {
          // Tuyệt đối không tự động thêm results[0] từ lần tìm kiếm trước khi quét barcode thất bại
          setErrorMessage(err.message || `Không tìm thấy sản phẩm nào khớp với mã "${code}".`);
        }
      } finally {
        if (requestId === latestRequestIdRef.current) {
          setIsSearching(false);
        }
      }
    } else if (e.key === 'Escape') {
      setShowDropdown(false);
    }
  };

  const triggerSuccess = (msg: string) => {
    setSuccessToast(msg);
    setTimeout(() => setSuccessToast(null), 2500);
  };

  const handleSelectProduct = (product: PosProductSearchResponse) => {
    const currentStockEntry = stockMap[product.variantId] || stockMap[product.sku];
    const effectiveAvailable = currentStockEntry ? currentStockEntry.available : (product.available ?? 0);

    if (effectiveAvailable <= 0) {
      setErrorMessage(`Sản phẩm "${product.productName}" (${product.sku}) đã hết hàng trong kho chi nhánh (tồn khả dụng = 0).`);
      return;
    }

    onAddToCart({
      ...product,
      available: effectiveAvailable,
    });
    triggerSuccess(`Đã thêm: ${product.productName} (${product.sku})`);
    setSearchTerm('');
    setShowDropdown(false);
    if (searchInputRef.current) {
      searchInputRef.current.focus();
    }
  };

  // Thử lại làm mới tồn khi GET refresh gặp lỗi (T03-AC-07)
  const handleRetryStock = async (e: React.MouseEvent, item: PosProductSearchResponse) => {
    e.stopPropagation();
    const lookupKey = item.variantId || item.sku;
    setRetryingKeys((prev) => ({ ...prev, [lookupKey]: true }));
    try {
      await invalidateAndRefetchStock(branchId, [
        { skuId: item.variantId, skuCode: item.sku, barcode: item.barcode },
      ]);
    } finally {
      setRetryingKeys((prev) => ({ ...prev, [lookupKey]: false }));
    }
  };

  return (
    <div className="relative w-full">
      {/* Search Bar Input */}
      <div className="relative flex items-center">
        <div className="absolute left-3.5 text-slate-400 pointer-events-none flex items-center gap-1.5">
          <Search className="w-5 h-5" />
          <Barcode className="w-4 h-4 text-indigo-400" />
        </div>
        <input
          ref={searchInputRef}
          type="text"
          value={searchTerm}
          onChange={(e) => {
            setSearchTerm(e.target.value);
            if (!e.target.value.trim()) {
              setResults([]);
              setShowDropdown(false);
            }
          }}
          onKeyDown={handleKeyDown}
          onFocus={() => results.length > 0 && setShowDropdown(true)}
          placeholder="Tìm tên sản phẩm, mã SKU hoặc quét mã vạch Barcode (Phím tắt: F2)..."
          className="w-full pl-16 pr-12 py-3 bg-white border-2 border-slate-200 rounded-xl text-slate-800 placeholder-slate-400 font-medium text-base shadow-sm focus:outline-none focus:border-indigo-600 focus:ring-4 focus:ring-indigo-100 transition-all"
        />
        <div className="absolute right-3.5 flex items-center gap-2">
          {isSearching && <Loader2 className="w-5 h-5 text-indigo-600 animate-spin" />}
          <kbd className="hidden sm:inline-block px-2 py-0.5 text-[11px] font-semibold text-slate-500 bg-slate-100 border border-slate-300 rounded shadow-xs">
            F2
          </kbd>
        </div>
      </div>

      {/* Floating Success / Error feedback */}
      {successToast && (
        <div className="absolute top-full left-0 right-0 mt-1 z-30 flex items-center gap-2 px-3 py-2 bg-emerald-50 text-emerald-800 border border-emerald-200 rounded-lg text-xs font-semibold shadow-md animate-fade-in">
          <CheckCircle2 className="w-4 h-4 text-emerald-600 shrink-0" />
          <span>{successToast}</span>
        </div>
      )}

      {errorMessage && (
        <div className="absolute top-full left-0 right-0 mt-1 z-30 flex items-center gap-2 px-3 py-2 bg-rose-50 text-rose-800 border border-rose-200 rounded-lg text-xs font-semibold shadow-md animate-fade-in">
          <AlertCircle className="w-4 h-4 text-rose-600 shrink-0" />
          <span>{errorMessage}</span>
        </div>
      )}

      {/* Dropdown Results */}
      {showDropdown && results.length > 0 && (
        <div className="absolute top-full left-0 right-0 mt-2 bg-white rounded-xl border border-slate-200 shadow-2xl z-40 max-h-96 overflow-y-auto divide-y divide-slate-100">
          <div className="px-3 py-2 bg-slate-50 text-xs font-semibold text-slate-500 flex justify-between items-center sticky top-0 border-b border-slate-100">
            <span>Tìm thấy {results.length} sản phẩm từ API</span>
            <span className="text-[11px] text-slate-400">Chọn sản phẩm còn tồn để thêm vào giỏ</span>
          </div>
          {results.map((item) => {
            const stockEntry = stockMap[item.variantId] || stockMap[item.sku];
            const currentAvailable = stockEntry ? stockEntry.available : (item.available ?? 0);
            const isOutOfStock = currentAvailable <= 0;
            const isStale = stockEntry?.isStale;
            const lookupKey = item.variantId || item.sku;
            const isRetrying = retryingKeys[lookupKey];

            return (
              <div
                key={item.variantId}
                onClick={() => !isOutOfStock && handleSelectProduct(item)}
                className={`px-4 py-3 flex items-center justify-between transition-colors ${
                  isOutOfStock
                    ? 'opacity-60 bg-slate-50/50 cursor-not-allowed'
                    : 'hover:bg-indigo-50/70 cursor-pointer group'
                }`}
              >
                <div className="flex-1 pr-4">
                  <div className="flex items-center gap-2">
                    <span className={`font-bold text-sm transition-colors ${isOutOfStock ? 'text-slate-500' : 'text-slate-800 group-hover:text-indigo-600'}`}>
                      {item.productName}
                    </span>
                    {item.variantName && (
                      <span className="text-xs px-2 py-0.5 bg-slate-100 text-slate-600 rounded font-medium">
                        {item.variantName}
                      </span>
                    )}
                    {isStale && (
                      <span className="text-[10px] px-1.5 py-0.5 bg-amber-100 text-amber-800 rounded font-bold border border-amber-300">
                        Chưa đồng bộ
                      </span>
                    )}
                  </div>
                  <div className="flex flex-wrap items-center gap-3 text-xs text-slate-500 mt-1">
                    <span className="font-mono font-semibold text-slate-600">SKU: {item.sku}</span>
                    {item.barcode && <span>Mã vạch: {item.barcode}</span>}
                    {item.color && <span>Màu: {item.color}</span>}
                    {item.size && <span>Size: {item.size}</span>}
                  </div>
                </div>

                <div className="text-right flex items-center gap-4">
                  <div>
                    <div className={`text-base font-bold ${isOutOfStock ? 'text-slate-400' : 'text-indigo-600'}`}>
                      {new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(item.retailPrice)}
                    </div>
                    <div className="text-xs mt-0.5 flex items-center justify-end gap-1.5">
                      {isOutOfStock ? (
                        <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[11px] font-semibold bg-rose-100 text-rose-700">
                          Hết tồn kho (0)
                        </span>
                      ) : (
                        <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[11px] font-semibold bg-emerald-100 text-emerald-700">
                          Khả dụng: {currentAvailable}
                        </span>
                      )}
                      {isStale && (
                        <button
                          type="button"
                          onClick={(e) => handleRetryStock(e, item)}
                          className="px-1.5 py-0.5 text-[10px] font-bold bg-amber-500 hover:bg-amber-600 text-white rounded transition-colors inline-flex items-center gap-0.5 shadow-xs"
                          title="Làm mới tồn kho"
                        >
                          <RefreshCw className={`w-2.5 h-2.5 ${isRetrying ? 'animate-spin' : ''}`} />
                          Thử lại
                        </button>
                      )}
                    </div>
                  </div>

                  <button
                    type="button"
                    disabled={isOutOfStock}
                    className={`p-2 rounded-lg transition-colors shadow-xs ${
                      isOutOfStock
                        ? 'bg-slate-100 text-slate-400 cursor-not-allowed'
                        : 'bg-indigo-50 text-indigo-600 group-hover:bg-indigo-600 group-hover:text-white'
                    }`}
                    title={isOutOfStock ? 'Hết tồn kho' : 'Thêm vào giỏ'}
                  >
                    <Plus className="w-4 h-4" />
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
