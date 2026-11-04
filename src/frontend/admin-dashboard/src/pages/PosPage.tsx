import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { Trash2, ShoppingBag, Minus, Plus, Package } from 'lucide-react';
import type { CartItem, Branch, PosProductSearchResponse, ReceiptData } from '../types/pos';
import { fetchBranches, searchPosProducts } from '../services/posApi';
import { applyCurrentPrices, baseQuantityInCart, lineDecimalPlaces, maxQuantityForLine, normalizeQuantity, resolveCartUnit, type CurrentPriceItem } from '../services/posCartUnits';
import { QuantityInput } from '../components/pos/QuantityInput';
import { PosHeader } from '../components/pos/PosHeader';
import { ProductCatalog } from '../components/pos/ProductCatalog';
import './PosPage.css';
import { PaymentModal } from '../components/pos/PaymentModal';
import { ReceiptPrintModal } from '../components/pos/ReceiptPrintModal';
import { useAuth } from '../context/AuthContext';

export function PosPage() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  // User State từ AuthContext tập trung
  const currentUser = user
    ? {
        fullName: user.name,
        email: user.email,
        tenantName: user.tenantName,
        role: user.role,
      }
    : null;

  // Application State
  const [cart, setCart] = useState<CartItem[]>([]);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [selectedBranchId, setSelectedBranchId] = useState<string>('');
  const [isOnline, setIsOnline] = useState<boolean>(navigator.onLine);
  const [isFullscreen, setIsFullscreen] = useState<boolean>(false);

  // Modals
  const [checkoutBusy, setCheckoutBusy] = useState(false);
  const [catalogRefresh, setCatalogRefresh] = useState(0);
  const paymentButtonRef = useRef<HTMLButtonElement>(null);
  const [printedReceipt, setPrintedReceipt] = useState<ReceiptData | null>(null);

  // Search input ref for F2 keyboard shortcut
  const searchInputRef = useRef<HTMLInputElement>(null);

  // Cập nhật tiêu đề thẻ trình duyệt cho giao diện POS
  useEffect(() => {
    const storeSuffix = user?.tenantName ? ` - ${user.tenantName}` : '';
    document.title = `OISMS POS · Quầy Bán Hàng${storeSuffix}`;
  }, [user?.tenantName]);

  // Load initial branches and online listener
  useEffect(() => {
    const handleOnline = () => setIsOnline(true);
    const handleOffline = () => setIsOnline(false);

    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);

    // Fetch branches from API
    fetchBranches().then((data) => {
      setBranches(data);
      if (data.length > 0) {
        setSelectedBranchId((prev) => (prev ? prev : data[0].id));
      }
    });

    // Lắng nghe sự kiện phiên đăng nhập hết hạn từ API client
    const handleAuthExpired = async () => {
      setCart([]);
      setPrintedReceipt(null);
      await logout();
      navigate('/login');
    };
    window.addEventListener('pos-auth-expired', handleAuthExpired);

    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
      window.removeEventListener('pos-auth-expired', handleAuthExpired);
    };
  }, [logout, navigate]);

  // Global Keyboard Shortcuts (F2: Search/Barcode, F4: Pay, Esc: Close)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'F2') {
        e.preventDefault();
        searchInputRef.current?.focus();
      } else if (e.key === 'F4') {
        e.preventDefault();
        if (cart.length > 0 && !checkoutBusy) paymentButtonRef.current?.focus();
      } else if (e.key === 'Escape') {
        setPrintedReceipt(null);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [cart.length, checkoutBusy]);

  // Fullscreen toggle
  const toggleFullscreen = () => {
    if (!document.fullscreenElement) {
      document.documentElement.requestFullscreen().then(() => setIsFullscreen(true)).catch(() => {});
    } else {
      if (document.exitFullscreen) {
        document.exitFullscreen().then(() => setIsFullscreen(false)).catch(() => {});
      }
    }
  };

  // Cart operations với kiểm tra tồn khả dụng nghiêm ngặt
  const handleAddToCart = (product: PosProductSearchResponse) => {
    const availableStock = product.available ?? 0;
    if (availableStock <= 0) {
      alert(`Sản phẩm "${product.productName}" đã hết hàng trong kho chi nhánh (Tồn khả dụng = 0).`);
      return;
    }

    // U05: chọn đơn vị theo mã vạch quy cách hoặc đơn vị bán mặc định; giá phải do server trả về
    const resolved = resolveCartUnit(product);
    if (!resolved) {
      alert(`Đơn vị bán của "${product.productName}" chưa có giá. Vui lòng cấu hình giá trước khi bán.`);
      return;
    }
    const selectedUnit = resolved.unit;
    const unitId = selectedUnit?.unitId || '';
    const lineKey = `${product.variantId}:${unitId}`;
    if (baseQuantityInCart(cart, product.variantId) + (selectedUnit?.factor || 1) > availableStock) {
      alert(`Không thể thêm. Tồn khả dụng của chi nhánh (${availableStock}) không đủ cho thêm 1 ${selectedUnit?.unitName || 'sản phẩm'}.`);
      return;
    }

    setCart((prevCart) => {
      // Khóa dòng theo variantId + unitId: khác đơn vị thì không gộp dòng
      const existingIndex = prevCart.findIndex((i) =>
        i.lineKey ? i.lineKey === lineKey : (i.variantId === product.variantId && (i.unitId || '') === unitId)
      );
      if (existingIndex > -1) {
        const currentQty = prevCart[existingIndex].quantity;
        if (currentQty + 1 > availableStock) {
          alert(`Không thể thêm. Số lượng trong giỏ (${currentQty}) đã đạt mức tồn khả dụng tối đa (${availableStock}) của chi nhánh.`);
          return prevCart;
        }
        const next = [...prevCart];
        next[existingIndex] = {
          ...next[existingIndex],
          quantity: currentQty + 1,
        };
        return next;
      } else {
        return [
          ...prevCart,
          {
            variantId: product.variantId,
            productId: product.productId,
            sku: product.sku,
            productName: product.productName,
            variantName: product.variantName,
            barcode: selectedUnit?.barcode || product.barcode,
            retailPrice: resolved.price,
            quantity: 1,
            available: availableStock,
            color: product.color,
            size: product.size,
            unitId,
            unitName: selectedUnit?.unitName || '',
            unitCode: selectedUnit?.unitCode || '',
            factorToBase: selectedUnit?.factor || 1,
            isBaseUnit: selectedUnit?.isBase ?? true,
            units: product.units,
            lineKey,
          },
        ];
      }
    });
  };

  // Danh mục không có giá theo đơn vị, nên lấy đơn vị và giá hiện hành từ API tìm kiếm của POS trước khi thêm vào giỏ
  const handleCatalogAdd = async (product: PosProductSearchResponse) => {
    // Kết quả từ API tìm kiếm hoặc quét mã đã có đơn vị và giá, kể cả đơn vị khớp mã vạch
    if (product.units) {
      handleAddToCart(product);
      return;
    }
    try {
      const results = await searchPosProducts(product.sku, selectedBranchId);
      const match = results.find((item) => item.variantId === product.variantId);
      if (!match) {
        alert(`Không tải được giá hiện hành của "${product.productName}". Vui lòng thử lại.`);
        return;
      }
      handleAddToCart(match);
    } catch (err: any) {
      alert(err?.message || `Không tải được giá hiện hành của "${product.productName}".`);
    }
  };

  const handleUpdateQuantity = (key: string, delta: number) => {
    setCart((prevCart) => {
      return prevCart
        .map((item) => {
          if (item.lineKey === key || item.variantId === key) {
            const nextQty = normalizeQuantity(item.quantity + delta, lineDecimalPlaces(item));
            const maxQty = maxQuantityForLine(prevCart, item);
            if (delta > 0 && nextQty > maxQty) {
              alert(`Số lượng không được vượt quá tồn khả dụng (tối đa ${maxQty} ${item.unitName || 'sản phẩm'}).`);
              return item;
            }
            return nextQty > 0 ? { ...item, quantity: nextQty } : null;
          }
          return item;
        })
        .filter((item): item is CartItem => item !== null);
    });
  };

  const handleSetQuantity = (key: string, quantity: number) => {
    setCart((prevCart) =>
      prevCart.map((item) => {
        if (item.lineKey === key || item.variantId === key) {
          const decimalPlaces = lineDecimalPlaces(item);
          const minQty = decimalPlaces > 0 ? 1 / 10 ** decimalPlaces : 1;
          const maxQty = Math.max(minQty, maxQuantityForLine(prevCart, item));
          const wanted = Number.isFinite(quantity) ? normalizeQuantity(quantity, decimalPlaces) : minQty;
          return { ...item, quantity: Math.min(Math.max(minQty, wanted), maxQty) };
        }
        return item;
      })
    );
  };

  const handleRemoveItem = (key: string) => {
    setCart((prevCart) => prevCart.filter((item) => item.lineKey !== key && item.variantId !== key));
  };

  const handleUpdateUnit = (lineKey: string, newUnitId: string) => {
    const lineItem = cart.find((i) => i.lineKey === lineKey || i.variantId === lineKey);
    const nextUnit = lineItem?.units?.find((u) => u.unitId === newUnitId);
    if (nextUnit?.salePrice == null) {
      alert('Đơn vị này chưa có giá. Vui lòng cấu hình giá trước khi bán.');
      return;
    }
    if (lineItem) {
      const usedByOthers = baseQuantityInCart(cart, lineItem.variantId, lineItem.lineKey || `${lineItem.variantId}:${lineItem.unitId || ''}`);
      if (usedByOthers + lineItem.quantity * nextUnit.factor > lineItem.available) {
        alert(`Tồn khả dụng (${lineItem.available}) không đủ cho số lượng này theo đơn vị ${nextUnit.unitName}.`);
        return;
      }
    }
    setCart((prevCart) => {
      const targetIndex = prevCart.findIndex((i) => i.lineKey === lineKey || i.variantId === lineKey);
      if (targetIndex === -1) return prevCart;
      const currentItem = prevCart[targetIndex];
      const targetUnit = currentItem.units?.find((u) => u.unitId === newUnitId);
      if (!targetUnit) return prevCart;

      const newUnitPrice = targetUnit.salePrice as number;
      const newLineKey = `${currentItem.variantId}:${newUnitId}`;

      const mergeIndex = prevCart.findIndex(
        (i, idx) => idx !== targetIndex && (i.lineKey === newLineKey || (i.variantId === currentItem.variantId && i.unitId === newUnitId))
      );
      if (mergeIndex > -1) {
        const next = [...prevCart];
        next[mergeIndex] = { ...next[mergeIndex], quantity: next[mergeIndex].quantity + currentItem.quantity };
        return next.filter((_, idx) => idx !== targetIndex);
      } else {
        const next = [...prevCart];
        next[targetIndex] = {
          ...currentItem,
          unitId: targetUnit.unitId,
          unitName: targetUnit.unitName,
          unitCode: targetUnit.unitCode,
          factorToBase: targetUnit.factor,
          isBaseUnit: targetUnit.isBase,
          retailPrice: newUnitPrice,
          lineKey: newLineKey,
        };
        return next;
      }
    });
  };

  const handleClearCart = () => {
    if (cart.length === 0) return;
    if (window.confirm('Bạn có chắc muốn xóa toàn bộ giỏ hàng hiện tại?')) {
      setCart([]);
    }
  };

  // Đổi chi nhánh: Xóa giỏ hàng để tránh bán vượt tồn hoặc sai kho của chi nhánh khác
  const handleSelectBranch = (branchId: string) => {
    if (checkoutBusy || branchId === selectedBranchId) return;
    if (cart.length > 0) {
      if (window.confirm('Đổi chi nhánh sẽ làm mới lại giỏ hàng vì tồn kho các chi nhánh là khác nhau. Bạn có muốn tiếp tục?')) {
        setCart([]);
        setSelectedBranchId(branchId);
      }
    } else {
      setSelectedBranchId(branchId);
    }
  };

  // Đăng xuất: Xóa toàn bộ giỏ, hóa đơn và đăng xuất qua AuthContext
  const handleLogout = async () => {
    setCart([]);
    setPrintedReceipt(null);
    await logout();
    navigate('/login');
  };

  // Calculations
  const totalQuantity = cart.reduce((sum, item) => sum + item.quantity, 0);
  const totalAmount = cart.reduce((sum, item) => sum + item.retailPrice * item.quantity, 0);

  const selectedBranch = branches.find((b) => b.id === selectedBranchId);

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
  };

  return (
    <div className="pos-workstation">
      <div inert={checkoutBusy}>
      {/* Header Bar */}
      <PosHeader
        branches={branches}
        selectedBranchId={selectedBranchId}
        onSelectBranch={handleSelectBranch}
        currentUser={currentUser}
        onOpenLogin={() => navigate('/login')}
        onLogout={handleLogout}
        isOnline={isOnline}
        isFullscreen={isFullscreen}
        onToggleFullscreen={toggleFullscreen}
      />

      </div>
      <main className="pos-workspace no-print">
        <fieldset className="pos-catalog-fieldset" disabled={checkoutBusy}>
          <ProductCatalog key={`${user?.tenantId}:${user?.id}:${selectedBranchId}`} branchId={selectedBranchId}
            cart={cart} onAdd={handleCatalogAdd} searchRef={searchInputRef} refresh={catalogRefresh} />
        </fieldset>
        <aside className="pos-order-panel" aria-label="Giỏ hàng và thanh toán">
          <div className="pos-order-heading"><h2><ShoppingBag size={19} /> Đơn hiện tại <span>{totalQuantity}</span></h2>
            <button type="button" onClick={handleClearCart} disabled={!cart.length || checkoutBusy}>Xóa tất cả</button>
          </div>
          <fieldset className="pos-cart-lines" disabled={checkoutBusy}>
            {cart.length === 0 ? <div className="pos-empty"><Package size={38} strokeWidth={1.3} /><strong>Giỏ hàng trống</strong><p>Quét barcode hoặc chọn sản phẩm</p></div> :
              cart.map((item) => <article className="pos-cart-line" key={item.lineKey || item.variantId}>
                <div className="pos-cart-product">
                  <strong>{item.productName}</strong>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '6px', marginTop: '2px' }}>
                    <small>{item.sku} · {item.variantName}</small>
                    {item.units && item.units.length > 1 ? (
                      <select
                        value={item.unitId || ''}
                        onChange={(e) => handleUpdateUnit(item.lineKey || item.variantId, e.target.value)}
                        style={{ fontSize: '11px', padding: '1px 4px', borderRadius: '4px', border: '1px solid #cbd5e1' }}
                      >
                        {item.units.map((u: any) => (
                          <option key={u.unitId} value={u.unitId}>
                            {u.unitName} (x{u.factor}){u.salePrice == null ? ' - chưa có giá' : ''}
                          </option>
                        ))}
                      </select>
                    ) : (
                      item.unitName && <span style={{ fontSize: '11px', background: '#e0e7ff', color: '#3730a3', padding: '1px 4px', borderRadius: '4px' }}>{item.unitName}</span>
                    )}
                  </div>
                </div>
                <button className="pos-remove-line" type="button" aria-label={`Xóa ${item.sku}`} onClick={() => handleRemoveItem(item.lineKey || item.variantId)}><Trash2 size={15} /></button>
                <div className="pos-quantity-control">
                  <button type="button" aria-label={`Giảm ${item.sku}`} onClick={() => handleUpdateQuantity(item.lineKey || item.variantId,-1)}><Minus size={13} /></button>
                  <QuantityInput value={item.quantity} max={maxQuantityForLine(cart, item)} decimalPlaces={lineDecimalPlaces(item)} label={`Số lượng ${item.sku}`}
                    onCommit={(quantity) => handleSetQuantity(item.lineKey || item.variantId, quantity)} />
                  <button type="button" aria-label={`Tăng ${item.sku}`} disabled={item.quantity >= maxQuantityForLine(cart, item)} onClick={() => handleUpdateQuantity(item.lineKey || item.variantId,1)}><Plus size={13} /></button>
                </div>
                <div className="pos-line-price"><strong>{formatCurrency(item.retailPrice*item.quantity)}</strong><small>{formatCurrency(item.retailPrice)} / {item.unitName || 'SP'}</small></div>
              </article>)}
          </fieldset>
          <fieldset className="pos-payment-fieldset" disabled={checkoutBusy}>
            <div className="pos-order-totals"><div><span>Tạm tính</span><b>{formatCurrency(totalAmount)}</b></div>
              <div><span>Giảm giá</span><span>0 ₫</span></div>
              <div className="pos-grand-total"><strong>Tổng thanh toán</strong><strong>{formatCurrency(totalAmount)}</strong></div>
            </div>
            <PaymentModal key={`${user?.tenantId}:${user?.id}:${selectedBranchId}:${catalogRefresh}`} embedded isOpen
              onClose={() => {}} items={cart} onApplyCurrentPrices={(prices: CurrentPriceItem[]) => setCart((prev) => applyCurrentPrices(prev, prices))} branchId={selectedBranchId} branchName={selectedBranch?.name || ''}
              branchAddress={selectedBranch?.address} branchPhone={selectedBranch?.phone}
              tenantName={currentUser?.tenantName || 'CỬA HÀNG OISMS'} cashierName={currentUser?.fullName || 'Thu ngân'}
              onBusyChange={setCheckoutBusy} submitRef={paymentButtonRef}
              onSuccessAndPrint={(receipt) => {setPrintedReceipt(receipt);setCart([]);setCatalogRefresh((value)=>value+1);}}
              onPreviewReceipt={setPrintedReceipt} />
          </fieldset>
        </aside>
      </main>

      {/* Print Thermal Receipt Modal */}
      <ReceiptPrintModal
        receipt={printedReceipt}
        onClose={() => setPrintedReceipt(null)}
      />
    </div>
  );
}
