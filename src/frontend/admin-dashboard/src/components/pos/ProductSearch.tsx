import React, { useState, useEffect, useTransition } from 'react';
import { Search, Barcode, Loader2, Plus, AlertCircle, CheckCircle2 } from 'lucide-react';
import type { PosProductSearchResponse } from '../../types/pos';
import { searchPosProducts, scanPosBarcode } from '../../services/posApi';

interface ProductSearchProps {
  branchId: string;
  onAddToCart: (product: PosProductSearchResponse) => void;
  searchInputRef: React.RefObject<HTMLInputElement | null>;
}

export const ProductSearch: React.FC<ProductSearchProps> = ({
  branchId,
  onAddToCart,
  searchInputRef,
}) => {
  const [searchTerm, setSearchTerm] = useState('');
  const [results, setResults] = useState<PosProductSearchResponse[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [, startTransition] = useTransition();
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successToast, setSuccessToast] = useState<string | null>(null);
  const [showDropdown, setShowDropdown] = useState(false);

  // Debounced search
  useEffect(() => {
    if (!searchTerm.trim()) {
      return;
    }

    const timer = setTimeout(async () => {
      setIsSearching(true);
      setErrorMessage(null);
      try {
        const data = await searchPosProducts(searchTerm, branchId);
        startTransition(() => {
          setResults(data);
          setShowDropdown(true);
        });
      } catch (err: any) {
        setErrorMessage(err.message || 'Lỗi tra cứu sản phẩm');
      } finally {
        setIsSearching(false);
      }
    }, 250);

    return () => clearTimeout(timer);
  }, [searchTerm, branchId]);

  // Handle enter key (Barcode or exact match)
  const handleKeyDown = async (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      const code = searchTerm.trim();
      if (!code) return;

      setIsSearching(true);
      setErrorMessage(null);

      // Tra cứu barcode chính xác từ API
      try {
        const item = await scanPosBarcode(code, branchId);
        if ((item.available ?? 0) <= 0) {
          setErrorMessage(`Sản phẩm "${item.productName}" (${item.sku}) đã hết hàng trong kho chi nhánh (tồn khả dụng = 0).`);
          return;
        }
        onAddToCart(item);
        triggerSuccess(`Đã thêm: ${item.productName} (${item.sku})`);
        setSearchTerm('');
        setShowDropdown(false);
      } catch (_barcodeErr) {
        // Fallback: nếu barcode không tìm thấy, xem có kết quả từ search dropdown không
        if (results.length > 0) {
          const first = results[0];
          if ((first.available ?? 0) <= 0) {
            setErrorMessage(`Sản phẩm "${first.productName}" (${first.sku}) đã hết hàng (tồn khả dụng = 0).`);
            return;
          }
          onAddToCart(first);
          triggerSuccess(`Đã thêm: ${first.productName} (${first.sku})`);
          setSearchTerm('');
          setShowDropdown(false);
        } else {
          setErrorMessage(`Không tìm thấy sản phẩm nào khớp với mã: "${code}"`);
        }
      } finally {
        setIsSearching(false);
      }
    }
  };

  const handleSelectItem = (item: PosProductSearchResponse) => {
    if ((item.available ?? 0) <= 0) {
      setErrorMessage(`Sản phẩm "${item.productName}" (${item.sku}) đã hết hàng trong kho chi nhánh (tồn khả dụng = 0).`);
      return;
    }
    onAddToCart(item);
    triggerSuccess(`Đã thêm: ${item.productName} (${item.sku})`);
    setSearchTerm('');
    setShowDropdown(false);
    searchInputRef.current?.focus();
  };

  const triggerSuccess = (msg: string) => {
    setSuccessToast(msg);
    setTimeout(() => {
      setSuccessToast(null);
    }, 2000);
  };

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
  };

  return (
    <div className="relative w-full">
      {/* Input Group */}
      <div className="relative flex items-center">
        <div className="absolute left-3.5 flex items-center pointer-events-none text-slate-400">
          <Barcode className="w-5 h-5 text-indigo-500 mr-1.5" />
          <Search className="w-4 h-4" />
        </div>

        <input
          ref={searchInputRef}
          type="text"
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          onKeyDown={handleKeyDown}
          onFocus={() => {
            if (results.length > 0) setShowDropdown(true);
          }}
          placeholder="Quét mã vạch Barcode hoặc tìm kiếm Tên, SKU sản phẩm (Phím F2)..."
          className="w-full pl-16 pr-12 py-3 bg-slate-50 hover:bg-slate-100/80 focus:bg-white text-slate-800 placeholder-slate-400 text-sm font-medium rounded-xl border border-slate-200 focus:border-indigo-500 focus:ring-3 focus:ring-indigo-500/10 transition-all outline-none"
        />

        <div className="absolute right-3 flex items-center gap-2">
          {isSearching && <Loader2 className="w-4 h-4 text-indigo-500 animate-spin" />}
          <kbd className="hidden sm:inline-block px-2 py-0.5 text-[11px] font-semibold text-slate-400 bg-slate-200/60 rounded border border-slate-300/80">
            F2
          </kbd>
        </div>
      </div>

      {/* Notifications / Error Banner */}
      {errorMessage && (
        <div className="mt-2 p-2.5 bg-rose-50 border border-rose-200 text-rose-700 text-xs rounded-lg flex items-center justify-between animate-fade-in">
          <div className="flex items-center gap-2">
            <AlertCircle className="w-4 h-4 shrink-0 text-rose-500" />
            <span>{errorMessage}</span>
          </div>
          <button
            type="button"
            onClick={() => setErrorMessage(null)}
            className="text-rose-500 hover:text-rose-700 text-xs font-semibold px-2"
          >
            Đóng
          </button>
        </div>
      )}

      {successToast && (
        <div className="mt-2 p-2 bg-emerald-50 border border-emerald-200 text-emerald-700 text-xs rounded-lg flex items-center gap-2 animate-fade-in">
          <CheckCircle2 className="w-4 h-4 shrink-0 text-emerald-500" />
          <span className="font-medium">{successToast}</span>
        </div>
      )}

      {/* Autocomplete Dropdown List */}
      {showDropdown && results.length > 0 && (
        <>
          <div
            className="fixed inset-0 z-20"
            onClick={() => setShowDropdown(false)}
          />
          <div className="absolute left-0 right-0 top-full mt-1.5 bg-white rounded-xl shadow-xl border border-slate-200/80 max-h-80 overflow-y-auto z-30 divide-y divide-slate-100">
            {results.map((item) => {
              const isAvailable = (item.available ?? 0) > 0;
              return (
                <div
                  key={item.variantId}
                  onClick={() => handleSelectItem(item)}
                  className={`p-3 hover:bg-indigo-50/60 transition-colors cursor-pointer flex items-center justify-between gap-3 ${
                    !isAvailable ? 'opacity-60 bg-slate-50/50' : ''
                  }`}
                >
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="font-semibold text-sm text-slate-800 truncate">
                        {item.productName}
                      </span>
                      {item.variantName && item.variantName !== item.productName && (
                        <span className="text-xs px-2 py-0.5 rounded bg-slate-100 text-slate-600 font-medium">
                          {item.variantName}
                        </span>
                      )}
                    </div>
                    <div className="flex items-center gap-3 text-xs text-slate-400 mt-1">
                      <span className="font-mono text-slate-500 font-semibold">{item.sku}</span>
                      {item.barcode && <span>Mã vạch: {item.barcode}</span>}
                    </div>
                  </div>

                  <div className="text-right shrink-0">
                    <div className="text-sm font-bold text-indigo-600">
                      {formatCurrency(item.retailPrice)}
                    </div>
                    <div className="text-xs mt-0.5">
                      {isAvailable ? (
                        <span className="text-emerald-700 font-semibold bg-emerald-50 px-2 py-0.5 rounded-full border border-emerald-200/60">
                          Khả dụng: {item.available}
                        </span>
                      ) : (
                        <span className="text-rose-600 font-semibold bg-rose-50 px-2 py-0.5 rounded-full border border-rose-200/60">
                          Hết tồn kho (0)
                        </span>
                      )}
                    </div>
                  </div>

                  <button
                    type="button"
                    disabled={!isAvailable}
                    className="p-1.5 rounded-lg bg-indigo-50 text-indigo-600 hover:bg-indigo-600 hover:text-white disabled:opacity-30 disabled:hover:bg-indigo-50 disabled:hover:text-indigo-600 transition-colors"
                  >
                    <Plus className="w-4 h-4" />
                  </button>
                </div>
              );
            })}
          </div>
        </>
      )}
    </div>
  );
};
