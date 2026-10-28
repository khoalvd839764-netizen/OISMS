import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { Trash2, ShoppingBag, Minus, Plus, Package } from 'lucide-react';
import type { CartItem, Branch, PosProductSearchResponse, ReceiptData } from '../types/pos';
import { fetchBranches } from '../services/posApi';
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

    setCart((prevCart) => {
      const existingIndex = prevCart.findIndex((i) => i.variantId === product.variantId);
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
            barcode: product.barcode,
            retailPrice: product.retailPrice,
            quantity: 1,
            available: availableStock,
            color: product.color,
            size: product.size,
          },
        ];
      }
    });
  };

  const handleUpdateQuantity = (variantId: string, delta: number) => {
    setCart((prevCart) => {
      return prevCart
        .map((item) => {
          if (item.variantId === variantId) {
            const nextQty = item.quantity + delta;
            if (delta > 0 && nextQty > item.available) {
              alert(`Số lượng không được vượt quá tồn khả dụng (${item.available}) của mặt hàng này.`);
              return item;
            }
            return nextQty > 0 ? { ...item, quantity: nextQty } : null;
          }
          return item;
        })
        .filter((item): item is CartItem => item !== null);
    });
  };

  const handleSetQuantity = (variantId: string, quantity: number) => {
    setCart((prevCart) =>
      prevCart.map((item) => {
        if (item.variantId === variantId) {
          const maxAllowed = item.available > 0 ? item.available : 1;
          const clamped = Math.min(Math.max(1, Number.isFinite(quantity) ? Math.floor(quantity) : 1), maxAllowed);
          return { ...item, quantity: clamped };
        }
        return item;
      })
    );
  };

  const handleRemoveItem = (variantId: string) => {
    setCart((prevCart) => prevCart.filter((item) => item.variantId !== variantId));
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
            cart={cart} onAdd={handleAddToCart} searchRef={searchInputRef} refresh={catalogRefresh} />
        </fieldset>
        <aside className="pos-order-panel" aria-label="Giỏ hàng và thanh toán">
          <div className="pos-order-heading"><h2><ShoppingBag size={19} /> Đơn hiện tại <span>{totalQuantity}</span></h2>
            <button type="button" onClick={handleClearCart} disabled={!cart.length || checkoutBusy}>Xóa tất cả</button>
          </div>
          <fieldset className="pos-cart-lines" disabled={checkoutBusy}>
            {cart.length === 0 ? <div className="pos-empty"><Package size={38} strokeWidth={1.3} /><strong>Giỏ hàng trống</strong><p>Quét barcode hoặc chọn sản phẩm</p></div> :
              cart.map((item) => <article className="pos-cart-line" key={item.variantId}>
                <div className="pos-cart-product"><strong>{item.productName}</strong><small>{item.sku} · {item.variantName}</small></div>
                <button className="pos-remove-line" type="button" aria-label={`Xóa ${item.sku}`} onClick={() => handleRemoveItem(item.variantId)}><Trash2 size={15} /></button>
                <div className="pos-quantity-control">
                  <button type="button" aria-label={`Giảm ${item.sku}`} onClick={() => handleUpdateQuantity(item.variantId,-1)}><Minus size={13} /></button>
                  <input type="number" min="1" max={item.available} aria-label={`Số lượng ${item.sku}`} value={item.quantity}
                    onChange={(event) => handleSetQuantity(item.variantId,Number(event.target.value))} />
                  <button type="button" aria-label={`Tăng ${item.sku}`} disabled={item.quantity >= item.available} onClick={() => handleUpdateQuantity(item.variantId,1)}><Plus size={13} /></button>
                </div>
                <div className="pos-line-price"><strong>{formatCurrency(item.retailPrice*item.quantity)}</strong><small>{formatCurrency(item.retailPrice)} / SP</small></div>
              </article>)}
          </fieldset>
          <fieldset className="pos-payment-fieldset" disabled={checkoutBusy}>
            <div className="pos-order-totals"><div><span>Tạm tính</span><b>{formatCurrency(totalAmount)}</b></div>
              <div><span>Giảm giá</span><span>0 ₫</span></div>
              <div className="pos-grand-total"><strong>Tổng thanh toán</strong><strong>{formatCurrency(totalAmount)}</strong></div>
            </div>
            <PaymentModal key={`${user?.tenantId}:${user?.id}:${selectedBranchId}:${catalogRefresh}`} embedded isOpen
              onClose={() => {}} items={cart} branchId={selectedBranchId} branchName={selectedBranch?.name || ''}
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
