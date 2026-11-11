import { useEffect, useRef, useState, type RefObject } from 'react';
import { Search, RefreshCw, Package, ScanBarcode } from 'lucide-react';
import type { PosProductSearchResponse, CartItem } from '../../types/pos';
import { scanPosBarcode } from '../../services/posApi';
import { baseQuantityInCart } from '../../services/posCartUnits';

export function ProductCatalog({ branchId, cart, onAdd, searchRef, data, loading, error, onReload }: {
  branchId: string; cart: CartItem[]; onAdd: (item: PosProductSearchResponse) => void;
  searchRef: RefObject<HTMLInputElement | null>; data: PosProductSearchResponse[]; loading: boolean; error: string | null; onReload: () => void;
}) {
  const [query, setQuery] = useState('');
  const [category, setCategory] = useState('all');
  const [scanning, setScanning] = useState(false);
  const [scanError, setScanError] = useState<string | null>(null);
  const generation = useRef(0);
  useEffect(() => {
    generation.current++;
    return () => { generation.current++; };
  }, [branchId]);
  const categories = [...new Map(data.filter((item) => item.categoryId && item.categoryName)
    .map((item) => [item.categoryId!, item.categoryName!])).entries()];
  const term = query.trim().toLocaleLowerCase('vi-VN');
  const visible = data.filter((item) => (category === 'all' || item.categoryId === category) &&
    (!term || [item.productName, item.sku, item.barcode, item.variantName].some((value) => value?.toLocaleLowerCase('vi-VN').includes(term))));
  async function scan() {
    if (!query.trim() || scanning || !branchId || loading) return;
    const exact = data.find((value) => value.sku.toLowerCase() === term || value.barcode?.toLowerCase() === term);
    if (!exact && visible.length > 1) return;
    const seq = generation.current;
    setScanning(true); setScanError(null);
    try {
      const item = exact ?? (visible.length === 1 ? visible[0] : await scanPosBarcode(query.trim(), branchId));
      if (seq !== generation.current) return;
      if ((item.available ?? 0) <= 0) throw new Error('Sản phẩm đã hết tồn khả dụng tại chi nhánh này.');
      onAdd(item); setQuery(''); searchRef.current?.focus();
    } catch (err) { if (seq === generation.current) setScanError(err instanceof Error ? err.message : 'Không thể quét mã.'); }
    finally { if (seq === generation.current) setScanning(false); }
  }
  return <section className="pos-catalog-view" aria-label="Danh mục sản phẩm POS">
    <div className="pos-catalog-search">
      <Search size={20} />
      <input ref={searchRef} aria-label="Tìm sản phẩm hoặc quét barcode" value={query} onChange={(event) => setQuery(event.target.value)}
        onKeyDown={(event) => { if (event.key === 'Enter') { event.preventDefault(); void scan(); } }}
        placeholder="Quét barcode hoặc nhập tên, SKU rồi nhấn Enter..." />
      <kbd>F2</kbd>
      <button type="button" aria-label="Tải lại sản phẩm" onClick={onReload} disabled={loading || scanning}><RefreshCw size={17} /></button>
    </div>
    <div className="pos-category-tabs" aria-label="Lọc danh mục">
      <button type="button" aria-pressed={category === 'all'} onClick={() => setCategory('all')}>Tất cả</button>
      {categories.map(([id, name]) => <button type="button" key={id} aria-pressed={category === id} onClick={() => setCategory(id)}>{name}</button>)}
      <span>{visible.length} sản phẩm</span>
    </div>
    {(error || scanError) && <p className="pos-error" role="alert">{error || scanError}</p>}
    {loading ? <div className="pos-empty" role="status"><Package size={32} /><p>Đang tải sản phẩm...</p></div> : visible.length === 0 ?
      <div className="pos-empty"><Search size={32} /><p>{error ? 'Chưa tải được danh mục. Vui lòng thử lại.' : 'Không có sản phẩm phù hợp.'}</p></div> :
      <div className="pos-product-grid">{visible.map((item) => {
        const remaining = Math.max(0, (item.available ?? 0) - baseQuantityInCart(cart, item.variantId));
        return <button className="pos-product-card" type="button" key={item.variantId} disabled={remaining <= 0 || scanning}
          aria-label={`Thêm ${item.productName} ${item.sku}`} onClick={() => onAdd(item)}>
          <div className="pos-product-placeholder" aria-hidden="true"><Package size={30} strokeWidth={1} /></div>
          <div className="pos-product-info"><strong>{item.productName}</strong><small>{item.sku} · {[item.color, item.size].filter(Boolean).join(' / ') || item.variantName}</small>
            <b>{item.retailPrice.toLocaleString('vi-VN')} ₫</b>
            <span className={`pos-stock-pill ${remaining === 0 ? 'is-empty' : remaining < 5 ? 'is-low' : ''}`}>Có thể bán: {remaining}</span>
          </div>
        </button>;
      })}</div>}
    <p className="pos-catalog-hint"><ScanBarcode size={14} /> Giá và tồn theo chi nhánh đang chọn · F2 để tìm nhanh</p>
  </section>;
}
