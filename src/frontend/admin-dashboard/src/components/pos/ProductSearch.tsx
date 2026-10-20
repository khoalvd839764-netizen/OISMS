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
      } catch (err: any) {
        // Tuyệt đối không tự động thêm results[0] từ lần tìm kiếm trước khi quét barcode thất bại
        setErrorMessage(err.message || `Không tìm thấy sản phẩm nào khớp với mã "${code}".`);
      } finally {
        setIsSearching(false);
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
    if ((product.available ?? 0) <= 0) {
      setErrorMessage(`Sản phẩm "${product.productName}" (${product.sku}) đã hết hàng trong kho chi nhánh (tồn khả dụng = 0).`);
      return;
    }
    onAddToCart(product);
    triggerSuccess(`Đã thêm: ${product.productName} (${product.sku})`);
    setSearchTerm('');
    setShowDropdown(false);
    if (searchInputRef.current) {
      searchInputRef.current.focus();
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
            const isOutOfStock = (item.available ?? 0) <= 0;
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
                    <div className="text-xs mt-0.5">
                      {isOutOfStock ? (
                        <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[11px] font-semibold bg-rose-100 text-rose-700">
                          Hết tồn kho (0)
                        </span>
                      ) : (
                        <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[11px] font-semibold bg-emerald-100 text-emerald-700">
                          Khả dụng: {item.available}
                        </span>
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
