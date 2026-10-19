import { useState, useEffect, useRef } from 'react';
import { Trash2, Printer, ShieldCheck, Sparkles } from 'lucide-react';
import type { CartItem, Branch, PosProductSearchResponse, ReceiptData } from './types/pos';
import { fetchBranches, getStoredUser, removeAuthToken } from './services/api';
import { PosHeader } from './components/PosHeader';
import { ProductSearch } from './components/ProductSearch';
import { CartTable } from './components/CartTable';
import { PaymentModal } from './components/PaymentModal';
import { ReceiptPrintModal } from './components/ReceiptPrintModal';
import { LoginModal } from './components/LoginModal';

export function App() {
  // Application State
  const [cart, setCart] = useState<CartItem[]>([]);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [selectedBranchId, setSelectedBranchId] = useState<string>('');
  const [currentUser, setCurrentUser] = useState(getStoredUser());
  const [isOnline, setIsOnline] = useState<boolean>(navigator.onLine);
  const [isFullscreen, setIsFullscreen] = useState<boolean>(false);

  // Modals
  const [isPaymentOpen, setIsPaymentOpen] = useState(false);
  const [isLoginOpen, setIsLoginOpen] = useState(false);
  const [printedReceipt, setPrintedReceipt] = useState<ReceiptData | null>(null);

  // Search input ref for F2 keyboard shortcut
  const searchInputRef = useRef<HTMLInputElement>(null);

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

    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
    };
  }, []);

  // Global Keyboard Shortcuts (F2: Search/Barcode, F4: Pay, Esc: Close)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'F2') {
        e.preventDefault();
        searchInputRef.current?.focus();
      } else if (e.key === 'F4') {
        e.preventDefault();
        if (cart.length > 0 && !isPaymentOpen) {
          setIsPaymentOpen(true);
        }
      } else if (e.key === 'Escape') {
        setIsPaymentOpen(false);
        setIsLoginOpen(false);
        setPrintedReceipt(null);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [cart.length, isPaymentOpen]);

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

  // Cart operations
  const handleAddToCart = (product: PosProductSearchResponse) => {
    setCart((prevCart) => {
      const existingIndex = prevCart.findIndex((i) => i.variantId === product.variantId);
      if (existingIndex > -1) {
        const next = [...prevCart];
        next[existingIndex] = {
          ...next[existingIndex],
          quantity: next[existingIndex].quantity + 1,
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
            available: product.available ?? 0,
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
            return nextQty > 0 ? { ...item, quantity: nextQty } : null;
          }
          return item;
        })
        .filter((item): item is CartItem => item !== null);
    });
  };

  const handleSetQuantity = (variantId: string, quantity: number) => {
    setCart((prevCart) =>
      prevCart.map((item) => (item.variantId === variantId ? { ...item, quantity } : item))
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

  const handleLogout = () => {
    removeAuthToken();
    setCurrentUser(null);
  };

  // Calculations
  const totalQuantity = cart.reduce((sum, item) => sum + item.quantity, 0);
  const totalAmount = cart.reduce((sum, item) => sum + item.retailPrice * item.quantity, 0);

  const selectedBranch = branches.find((b) => b.id === selectedBranchId);

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
  };

  return (
    <div className="min-h-screen flex flex-col bg-slate-100 font-sans text-slate-800">
      {/* Header Bar */}
      <PosHeader
        branches={branches}
        selectedBranchId={selectedBranchId}
        onSelectBranch={setSelectedBranchId}
        currentUser={currentUser}
        onOpenLogin={() => setIsLoginOpen(true)}
        onLogout={handleLogout}
        isOnline={isOnline}
        isFullscreen={isFullscreen}
        onToggleFullscreen={toggleFullscreen}
      />

      {/* Main POS Workspace */}
      <main className="no-print flex-1 p-4 lg:p-6 flex flex-col lg:flex-row gap-5 overflow-hidden max-w-[1920px] mx-auto w-full">
        {/* Left Side: Product Search & Cart Table */}
        <section className="flex-1 flex flex-col gap-4 min-w-0">
          {/* Barcode & Search Component */}
          <div className="bg-white p-3.5 rounded-2xl border border-slate-200/80 shadow-xs">
            <ProductSearch
              branchId={selectedBranchId}
              onAddToCart={handleAddToCart}
              searchInputRef={searchInputRef}
            />
          </div>

          {/* Cart Table with Multi-Line Support */}
          <CartTable
            items={cart}
            onUpdateQuantity={handleUpdateQuantity}
            onSetQuantity={handleSetQuantity}
            onRemoveItem={handleRemoveItem}
            onClearCart={handleClearCart}
            onFocusSearch={() => searchInputRef.current?.focus()}
          />
        </section>

        {/* Right Side: Order Summary & Checkout Action Panel */}
        <aside className="w-full lg:w-[400px] xl:w-[440px] shrink-0 flex flex-col gap-4">
          <div className="bg-white rounded-3xl border border-slate-200/80 shadow-sm p-6 flex flex-col justify-between h-full">
            <div className="space-y-6">
              {/* Order Info Header */}
              <div className="flex items-center justify-between pb-4 border-b border-slate-100">
                <div>
                  <h2 className="text-base font-extrabold text-slate-800 tracking-tight m-0">
                    Hóa Đơn Bán Hàng
                  </h2>
                  <p className="text-xs text-slate-400 m-0">
                    Chi nhánh: <span className="font-semibold text-slate-700">{selectedBranch?.name || 'Mặc định'}</span>
                  </p>
                </div>
                <span className="px-3 py-1 rounded-full text-xs font-bold bg-indigo-50 text-indigo-700 border border-indigo-200/60">
                  {totalQuantity} món
                </span>
              </div>

              {/* Price Breakdown */}
              <div className="space-y-3 text-sm">
                <div className="flex justify-between text-slate-500">
                  <span>Tạm tính ({cart.length} dòng):</span>
                  <span className="font-semibold text-slate-800">{formatCurrency(totalAmount)}</span>
                </div>
                <div className="flex justify-between text-slate-500">
                  <span>Giảm giá:</span>
                  <span className="font-semibold text-emerald-600">0 đ</span>
                </div>
                <div className="flex justify-between text-slate-500">
                  <span>Thuế VAT (0%):</span>
                  <span className="font-semibold text-slate-800">0 đ</span>
                </div>

                <div className="pt-3 border-t-2 border-dashed border-slate-200 flex justify-between items-baseline">
                  <span className="text-base font-extrabold text-slate-900">TỔNG THANH TOÁN:</span>
                  <span className="text-2xl font-black text-indigo-600 tracking-tight">
                    {formatCurrency(totalAmount)}
                  </span>
                </div>
              </div>

              {/* Notice / Status Indicator */}
              <div className="p-3 bg-slate-50 rounded-2xl border border-slate-200 text-xs text-slate-500 flex items-start gap-2">
                <Sparkles className="w-4 h-4 text-indigo-500 shrink-0 mt-0.5" />
                <div>
                  <span className="font-semibold text-slate-700">Dữ liệu thật 100%: </span>
                  Tra cứu tồn khả dụng và giá bán trực tiếp từ PostgreSQL thông qua API Backend.
                </div>
              </div>
            </div>

            {/* Bottom Actions */}
            <div className="space-y-3 pt-6 border-t border-slate-100">
              <button
                type="button"
                disabled={cart.length === 0}
                onClick={() => setIsPaymentOpen(true)}
                className={`w-full py-4 px-6 rounded-2xl font-bold text-base flex items-center justify-between transition-all shadow-lg ${
                  cart.length === 0
                    ? 'bg-slate-200 text-slate-400 cursor-not-allowed shadow-none'
                    : 'bg-gradient-to-r from-indigo-600 to-indigo-700 hover:from-indigo-500 hover:to-indigo-600 text-white shadow-indigo-600/30'
                }`}
              >
                <div className="flex items-center gap-2">
                  <ShieldCheck className="w-5 h-5" />
                  <span>THANH TOÁN (F4)</span>
                </div>
                <span className="font-black font-mono text-lg">{formatCurrency(totalAmount)}</span>
              </button>

              <div className="flex gap-2 text-xs">
                <button
                  type="button"
                  disabled={cart.length === 0}
                  onClick={() => {
                    const tempReceipt: ReceiptData = {
                      receiptNumber: `TMP-${Date.now().toString().slice(-6)}`,
                      createdAt: new Date().toLocaleString('vi-VN'),
                      tenantName: currentUser?.tenantName || 'CỬA HÀNG OISMS',
                      branchName: selectedBranch?.name || 'Chi nhánh mặc định',
                      branchAddress: selectedBranch?.address || 'Chi nhánh bán lẻ POS',
                      branchPhone: selectedBranch?.phone || '1900 6868',
                      cashierName: currentUser?.fullName || 'Thu ngân',
                      items: cart.map((i) => ({
                        name: i.productName + (i.variantName ? ` - ${i.variantName}` : ''),
                        sku: i.sku,
                        quantity: i.quantity,
                        unitPrice: i.retailPrice,
                        subtotal: i.retailPrice * i.quantity,
                      })),
                      totalAmount,
                      paymentMethod: 'CASH',
                      amountGiven: totalAmount,
                      changeDue: 0,
                    };
                    setPrintedReceipt(tempReceipt);
                  }}
                  className="flex-1 py-2.5 px-3 bg-slate-100 hover:bg-slate-200 disabled:opacity-50 text-slate-700 font-semibold rounded-xl transition-colors flex items-center justify-center gap-1.5"
                >
                  <Printer className="w-3.5 h-3.5" />
                  <span>In phiếu tạm tính</span>
                </button>

                <button
                  type="button"
                  disabled={cart.length === 0}
                  onClick={handleClearCart}
                  className="py-2.5 px-3 bg-rose-50 hover:bg-rose-100 disabled:opacity-50 text-rose-700 font-semibold rounded-xl transition-colors flex items-center justify-center gap-1.5"
                  title="Hủy đơn"
                >
                  <Trash2 className="w-3.5 h-3.5" />
                  <span>Hủy</span>
                </button>
              </div>
            </div>
          </div>
        </aside>
      </main>

      {/* Payment Modal */}
      <PaymentModal
        isOpen={isPaymentOpen}
        onClose={() => setIsPaymentOpen(false)}
        items={cart}
        branchId={selectedBranchId}
        branchName={selectedBranch?.name || 'Chi nhánh mặc định'}
        tenantName={currentUser?.tenantName || 'CỬA HÀNG OISMS'}
        cashierName={currentUser?.fullName || 'Thu ngân'}
        onSuccessAndPrint={(receipt) => {
          setPrintedReceipt(receipt);
          setCart([]); // Clear cart after successful transaction
        }}
      />

      {/* Print Thermal Receipt Modal */}
      <ReceiptPrintModal
        receipt={printedReceipt}
        onClose={() => setPrintedReceipt(null)}
      />

      {/* Login Modal */}
      <LoginModal
        isOpen={isLoginOpen}
        onClose={() => setIsLoginOpen(false)}
        onLoginSuccess={(user) => {
          setCurrentUser(user);
          fetchBranches().then((data) => {
            setBranches(data);
            if (data.length > 0) setSelectedBranchId(data[0].id);
          });
        }}
      />
    </div>
  );
}

export default App;
