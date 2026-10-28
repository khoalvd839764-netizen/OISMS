import { useState, useEffect, useRef } from 'react';
import { Trash2, Printer, ShieldCheck, Sparkles, RefreshCw, AlertTriangle, Bell } from 'lucide-react';
import type { CartItem, Branch, PosProductSearchResponse, ReceiptData, RealtimeConnectionStatus } from './types/pos';
import {
  fetchBranches,
  getStoredUser,
  removeAuthToken,
  getPendingCheckout,
  getUserPendingCheckout,
  updatePendingCheckoutStatus,
  isSameUserAndTenant,
  invalidateAndRefetchStock,
  isUuid,
  subscribeBranchStock,
  subscribeRealtimeStatus,
  subscribeOrderCreated,
  subscribeLowStockWarning,
  startRealtimeConnection,
  stopRealtimeConnection,
  switchRealtimeBranch,
  isSoundMuted,
  setSoundMuted,
  type StockItemState,
} from './services/api';
import { PosHeader } from './components/PosHeader';
import { ProductSearch } from './components/ProductSearch';
import { CartTable } from './components/CartTable';
import { PaymentModal } from './components/PaymentModal';
import { ReceiptPrintModal } from './components/ReceiptPrintModal';
import { LoginModal } from './components/LoginModal';

export function App() {
  // Lấy người dùng đã xác thực từ storage
  // State index 0: currentUser (Bảo toàn vị trí index phục vụ regression tests)
  const [currentUser, setCurrentUser] = useState(getStoredUser());

  // POS-T01-F03: Khôi phục pending checkout theo owner phiên hiện tại khi reload
  const initialPending = getUserPendingCheckout(currentUser);

  // Application State
  // State index 1: cart (Phục hồi giỏ hàng từ snapshot payload của pending checkout nếu có)
  const [cart, setCart] = useState<CartItem[]>(() => {
    if (initialPending && (initialPending.status === 'unknown' || initialPending.status === 'submitting')) {
      return (initialPending.requestPayload.items || []).map((i) => ({
        productId: i.skuId || i.skuCode,
        variantId: i.skuId || i.skuCode,
        sku: i.skuCode,
        barcode: i.skuCode,
        productName: i.productName,
        variantName: i.variantName || '',
        quantity: i.quantity,
        retailPrice: i.unitPrice,
        available: i.quantity,
      }));
    }
    return [];
  });

  // State index 2: branches
  const [branches, setBranches] = useState<Branch[]>([]);

  // State index 3: selectedBranchId
  const [selectedBranchId, setSelectedBranchId] = useState<string>(() => {
    return initialPending?.owner.branchId || '';
  });

  // State index 4: isOnline
  const [isOnline, setIsOnline] = useState<boolean>(navigator.onLine);

  // State index 5: isFullscreen
  const [isFullscreen, setIsFullscreen] = useState<boolean>(false);

  // Modals
  // State index 6: isPaymentOpen
  const [isPaymentOpen, setIsPaymentOpen] = useState(false);

  // State index 7: isLoginOpen
  const [isLoginOpen, setIsLoginOpen] = useState(false);

  // State index 8: printedReceipt
  const [printedReceipt, setPrintedReceipt] = useState<ReceiptData | null>(null);

  // State index 9: lastConfirmedReceipt
  // T02-AC-04: Lưu hóa đơn đã xác nhận thành công gần nhất để hỗ trợ in lại không cần checkout lại
  const [lastConfirmedReceipt, setLastConfirmedReceipt] = useState<ReceiptData | null>(null);

  // =========================================================================
  // S3-POS-B02 New States (Placed after index 9 to preserve baseline indices)
  // =========================================================================
  // State index 10: realtimeStatus
  const [realtimeStatus, setRealtimeStatus] = useState<RealtimeConnectionStatus>('disconnected');

  // State index 11: isMuted
  const [isMutedState, setIsMutedState] = useState<boolean>(isSoundMuted());

  // State index 12: stockMap
  const [stockMap, setStockMap] = useState<Record<string, StockItemState>>({});

  // State index 13: realtimeToast
  const [realtimeToast, setRealtimeToast] = useState<{ type: 'order' | 'warning'; message: string } | null>(null);

  // Search input ref for F2 keyboard shortcut
  const searchInputRef = useRef<HTMLInputElement>(null);

  // Kiểm tra có giao dịch đang chờ (submitting hoặc unknown) để bảo vệ giỏ hàng và chống xung đột
  const hasActivePendingAttempt = () => {
    const pending = getPendingCheckout(currentUser, selectedBranchId);
    return Boolean(pending && (pending.status === 'unknown' || pending.status === 'submitting'));
  };

  // Load initial branches, online listener and pending recovery
  useEffect(() => {
    const handleOnline = () => setIsOnline(true);
    const handleOffline = () => setIsOnline(false);

    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);

    // POS-T01-F03: Phục hồi record còn trạng thái 'submitting' khi reload giữa lúc gửi sang 'unknown' an toàn
    if (initialPending && initialPending.status === 'submitting') {
      updatePendingCheckoutStatus(
        'unknown',
        'Yêu cầu thanh toán trước đó bị gián đoạn do tải lại trang. Kết quả chưa xác định, vui lòng kiểm tra hoặc thử lại.',
        undefined,
        currentUser,
        initialPending.owner.branchId
      );
    }

    // Fetch branches from API
    fetchBranches().then((data) => {
      setBranches(data);
      if (data.length > 0) {
        setSelectedBranchId((prev) => {
          if (prev) return prev;
          if (initialPending?.owner.branchId) return initialPending.owner.branchId;
          return data[0].id;
        });
      }
    });

    // Lắng nghe sự kiện phiên đăng nhập hết hạn từ API client
    // Bảo toàn giỏ hàng và pending checkout khi hết phiên (T01-AC-03, PR24-F02)
    const handleAuthExpired = () => {
      setPrintedReceipt(null);
      setIsPaymentOpen(false);
      setIsLoginOpen(true);
      alert('Phiên làm việc đã hết hạn. Vui lòng đăng nhập lại để tiếp tục.');
    };
    window.addEventListener('pos-auth-expired', handleAuthExpired);

    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
      window.removeEventListener('pos-auth-expired', handleAuthExpired);
    };
  }, []);

  // Realtime SignalR Lifecycle: Kết nối và quản lý vòng đời theo user và branch (T03-AC-01 -> T03-AC-05)
  useEffect(() => {
    if (currentUser && selectedBranchId) {
      startRealtimeConnection(currentUser, selectedBranchId).catch(() => {});
    } else {
      stopRealtimeConnection().catch(() => {});
    }
  }, [currentUser?.id, currentUser?.tenantId, selectedBranchId]);

  // Realtime Events and Stock Subscriptions (T03-AC-02, T03-AC-03)
  useEffect(() => {
    const unsubStatus = subscribeRealtimeStatus(setRealtimeStatus);

    const unsubOrder = subscribeOrderCreated((order) => {
      const formattedTotal = new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(order.totalAmount);
      setRealtimeToast({
        type: 'order',
        message: `Đơn hàng mới từ sàn/admin #${order.orderNumber} (${order.channel}) - ${formattedTotal}`,
      });
      setTimeout(() => setRealtimeToast(null), 4500);
    });

    const unsubWarning = subscribeLowStockWarning((warning) => {
      setRealtimeToast({
        type: 'warning',
        message: `Cảnh báo tồn kho thấp: SKU ${warning.skuCode} còn ${warning.currentAvailable} (ngưỡng: ${warning.threshold})`,
      });
      setTimeout(() => setRealtimeToast(null), 5000);
    });

    return () => {
      unsubStatus();
      unsubOrder();
      unsubWarning();
    };
  }, []);

  // Đăng ký nhận cập nhật tồn kho cho chi nhánh hiện hành
  useEffect(() => {
    if (!selectedBranchId) return;
    const unsubStock = subscribeBranchStock(selectedBranchId, (stocks) => {
      setStockMap(stocks);
    });
    return () => unsubStock();
  }, [selectedBranchId]);

  // Global Keyboard Shortcuts (F2: Search/Barcode, F4: Pay, Esc: Close) - T05-AC-01
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'F2') {
        e.preventDefault();
        searchInputRef.current?.focus();
      } else if (e.key === 'F4') {
        e.preventDefault();
        const active = getPendingCheckout(currentUser, selectedBranchId);
        // Không mở song song hai checkout hoặc khi đang gửi request
        if (active && active.status === 'submitting') {
          return;
        }
        if (cart.length > 0 && !isPaymentOpen) {
          setIsPaymentOpen(true);
        }
      } else if (e.key === 'Escape') {
        const active = getPendingCheckout(currentUser, selectedBranchId);
        if (active && active.status === 'submitting') {
          return; // Không cho đóng khi đang gửi request
        }
        setIsPaymentOpen(false);
        setIsLoginOpen(false);
        setPrintedReceipt(null);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [cart.length, isPaymentOpen, currentUser, selectedBranchId]);

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

  // Cart operations với kiểm tra tồn khả dụng nghiêm ngặt và chặn sửa khi đang pending
  const handleAddToCart = (product: PosProductSearchResponse) => {
    if (hasActivePendingAttempt()) {
      alert('Đang có giao dịch chưa xác định kết quả trong mục Thanh toán. Vui lòng mở Thanh toán để hoàn tất đối soát hoặc thử lại trước khi thay đổi giỏ hàng.');
      return;
    }

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
    if (hasActivePendingAttempt()) {
      alert('Không thể thay đổi số lượng khi đang có giao dịch chưa xác định kết quả.');
      return;
    }

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
    if (hasActivePendingAttempt()) {
      alert('Không thể thay đổi số lượng khi đang có giao dịch chưa xác định kết quả.');
      return;
    }

    setCart((prevCart) =>
      prevCart.map((item) => {
        if (item.variantId === variantId) {
          const maxAllowed = item.available > 0 ? item.available : 1;
          const clamped = Math.min(Math.max(1, quantity), maxAllowed);
          return { ...item, quantity: clamped };
        }
        return item;
      })
    );
  };

  const handleRemoveItem = (variantId: string) => {
    if (hasActivePendingAttempt()) {
      alert('Không thể xóa sản phẩm khi đang có giao dịch chưa xác định kết quả.');
      return;
    }

    setCart((prevCart) => prevCart.filter((item) => item.variantId !== variantId));
  };

  const handleClearCart = () => {
    if (hasActivePendingAttempt()) {
      alert('Không thể xóa giỏ hàng khi đang có giao dịch chưa xác định kết quả.');
      return;
    }

    if (cart.length === 0) return;
    if (window.confirm('Bạn có chắc muốn xóa toàn bộ giỏ hàng hiện tại?')) {
      setCart([]);
    }
  };

  // Đổi chi nhánh: Xóa giỏ hàng để tránh bán vượt tồn hoặc sai kho của chi nhánh khác
  const handleSelectBranch = (branchId: string) => {
    if (branchId === selectedBranchId) return;

    if (hasActivePendingAttempt()) {
      alert('Không thể đổi chi nhánh khi đang có giao dịch chưa xác định kết quả. Vui lòng mở thanh toán để thử lại hoặc hoàn tất giao dịch.');
      return;
    }

    if (cart.length > 0) {
      if (window.confirm('Đổi chi nhánh sẽ làm mới lại giỏ hàng vì tồn kho các chi nhánh là khác nhau. Bạn có muốn tiếp tục?')) {
        setCart([]);
        setLastConfirmedReceipt(null);
        setSelectedBranchId(branchId);
        switchRealtimeBranch(branchId).catch(() => {});
      }
    } else {
      setLastConfirmedReceipt(null);
      setSelectedBranchId(branchId);
      switchRealtimeBranch(branchId).catch(() => {});
    }
  };

  // Đăng xuất: Xóa toàn bộ giỏ, hóa đơn, trạng thái phiên cũ có cảnh báo khi đang pending
  const handleLogout = () => {
    const pending = getPendingCheckout(currentUser, selectedBranchId);
    if (pending && pending.status === 'submitting') {
      alert('Đang trong quá trình gửi thanh toán, không thể đăng xuất.');
      return;
    }

    if (pending && pending.status === 'unknown') {
      if (!window.confirm('CẢNH BÁO: Đang có giao dịch chưa xác định kết quả trên máy chủ. Đăng xuất lúc này sẽ không mất dữ liệu giao dịch của tài khoản hiện tại, nhưng phiên làm việc sẽ kết thúc. Bạn có chắc muốn đăng xuất?')) {
        return;
      }
    }

    stopRealtimeConnection().catch(() => {});
    removeAuthToken();
    setCurrentUser(null);
    setCart([]);
    setPrintedReceipt(null);
    setLastConfirmedReceipt(null);
    setIsPaymentOpen(false);
  };

  // T03-AC-04: Kiểm tra xem có mặt hàng nào trong giỏ có số lượng vượt tồn khả dụng mới nhất từ API/Hub không
  const stockDeficits = cart
    .map((item) => {
      const entry = stockMap[item.variantId] || stockMap[item.sku];
      if (entry && entry.available < item.quantity) {
        return { item, currentAvailable: entry.available };
      }
      return null;
    })
    .filter((x): x is { item: CartItem; currentAvailable: number } => Boolean(x));

  // Calculations
  const totalQuantity = cart.reduce((sum, item) => sum + item.quantity, 0);
  const totalAmount = cart.reduce((sum, item) => sum + item.retailPrice * item.quantity, 0);

  const selectedBranch = branches.find((b) => b.id === selectedBranchId);

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
  };

  const pendingCheckout = getPendingCheckout(currentUser, selectedBranchId);

  return (
    <div className="min-h-screen flex flex-col bg-slate-100 font-sans text-slate-800">
      {/* Header Bar */}
      <PosHeader
        branches={branches}
        selectedBranchId={selectedBranchId}
        onSelectBranch={handleSelectBranch}
        currentUser={currentUser}
        onOpenLogin={() => setIsLoginOpen(true)}
        onLogout={handleLogout}
        isOnline={isOnline}
        isFullscreen={isFullscreen}
        onToggleFullscreen={toggleFullscreen}
        realtimeStatus={realtimeStatus}
        isMuted={isMutedState}
        onToggleMute={() => {
          const next = !isMutedState;
          setIsMutedState(next);
          setSoundMuted(next);
        }}
      />

      {/* Floating Realtime Notification Toast (T03-AC-02) */}
      {realtimeToast && (
        <div className="fixed top-16 right-4 z-50 max-w-sm animate-fade-in shadow-xl">
          <div
            className={`p-3.5 rounded-2xl border flex items-start gap-2.5 text-xs font-semibold ${
              realtimeToast.type === 'order'
                ? 'bg-indigo-900 text-white border-indigo-700 shadow-indigo-900/30'
                : 'bg-amber-900 text-amber-50 border-amber-700 shadow-amber-900/30'
            }`}
          >
            {realtimeToast.type === 'order' ? (
              <Bell className="w-4 h-4 text-indigo-300 shrink-0 mt-0.5" />
            ) : (
              <AlertTriangle className="w-4 h-4 text-amber-400 shrink-0 mt-0.5" />
            )}
            <div className="flex-1">
              <div className="font-bold text-[11px] uppercase tracking-wider text-slate-300">
                {realtimeToast.type === 'order' ? 'Thông báo Realtime Hub' : 'Cảnh báo kho'}
              </div>
              <div className="mt-0.5 text-xs font-medium">{realtimeToast.message}</div>
            </div>
            <button
              type="button"
              onClick={() => setRealtimeToast(null)}
              className="text-slate-400 hover:text-white transition-colors"
            >
              ×
            </button>
          </div>
        </div>
      )}

      {/* Main POS Workspace */}
      <main className="no-print flex-1 p-4 lg:p-6 flex flex-col lg:flex-row gap-5 overflow-hidden max-w-[1920px] mx-auto w-full">
        {/* Left Side: Product Search & Cart Table */}
        <section className="flex-1 flex flex-col gap-4 min-w-0">
          {/* Unresolved Transaction Banner */}
          {pendingCheckout && pendingCheckout.status === 'unknown' && (
            <div className="p-3.5 bg-amber-50 border border-amber-300 rounded-2xl flex items-center justify-between text-xs text-amber-900 shadow-xs">
              <div className="flex items-center gap-2">
                <RefreshCw className="w-4 h-4 text-amber-600 animate-spin shrink-0" />
                <span>
                  <strong>Lưu ý:</strong> Đang có giao dịch chưa xác định kết quả (Mã:{' '}
                  <span className="font-mono font-bold">{pendingCheckout.requestKey.slice(0, 8)}...</span>).
                  Giỏ hàng được khóa để đảm bảo an toàn.
                </span>
              </div>
              <button
                type="button"
                onClick={() => setIsPaymentOpen(true)}
                className="px-3 py-1 bg-amber-600 hover:bg-amber-700 text-white font-bold rounded-xl transition-colors shrink-0"
              >
                Mở thanh toán để xử lý
              </button>
            </div>
          )}

          {/* T03-AC-04: Cảnh báo tồn khả dụng thấp hơn giỏ hàng */}
          {stockDeficits.length > 0 && (
            <div className="p-3.5 bg-rose-50 border border-rose-300 rounded-2xl flex flex-col gap-1.5 text-xs text-rose-900 shadow-xs animate-fade-in">
              <div className="flex items-center gap-2 font-bold text-rose-800">
                <AlertTriangle className="w-4 h-4 text-rose-600 shrink-0" />
                <span>Cảnh báo biến động tồn kho (Tồn khả dụng thấp hơn số lượng trong giỏ):</span>
              </div>
              <ul className="list-disc list-inside space-y-0.5 text-rose-700 pl-1">
                {stockDeficits.map(({ item, currentAvailable }) => (
                  <li key={item.variantId}>
                    Sản phẩm <strong>"{item.productName}"</strong> (SKU: <span className="font-mono">{item.sku}</span>) hiện chỉ còn <strong>{currentAvailable}</strong> trong kho chi nhánh, ít hơn số lượng <strong>{item.quantity}</strong> trong giỏ hàng.
                  </li>
                ))}
              </ul>
              <p className="text-[11px] text-rose-600 mt-0.5 italic">
                * Hệ thống không tự ý cắt giảm số lượng hoặc đơn giá của bạn. Vui lòng điều chỉnh giỏ hàng trước khi thanh toán.
              </p>
            </div>
          )}

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
                  <span className="font-semibold text-slate-700">Kết nối API & SignalR Hub OISMS: </span>
                  Tồn kho tự động đồng bộ thời gian thực theo chi nhánh. Sau checkout, số dư làm mới tức thì không cần tải lại trang.
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

              {/* T02-AC-04: Nút in lại hóa đơn đã xác nhận thành công mà không gọi checkout lại */}
              {lastConfirmedReceipt && (
                <button
                  type="button"
                  onClick={() => setPrintedReceipt(lastConfirmedReceipt)}
                  className="w-full py-2.5 px-3 bg-emerald-50 hover:bg-emerald-100 border border-emerald-200 text-emerald-800 font-semibold rounded-xl text-xs transition-colors flex items-center justify-center gap-1.5 shadow-xs"
                  title={`In lại hóa đơn ${lastConfirmedReceipt.receiptNumber}`}
                >
                  <Printer className="w-3.5 h-3.5 text-emerald-600" />
                  <span>In lại hóa đơn vừa bán ({lastConfirmedReceipt.receiptNumber})</span>
                </button>
              )}

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
                      branchAddress: selectedBranch?.address?.trim() || undefined,
                      branchPhone: selectedBranch?.phone?.trim() || undefined,
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
                      isDraft: true,
                      paymentStatusTitle: 'CHƯA THANH TOÁN (PHIẾU TẠM TÍNH)',
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
        key={`${currentUser?.tenantId || ''}-${currentUser?.id || currentUser?.email || ''}-${selectedBranchId}`}
        isOpen={isPaymentOpen}
        onClose={() => setIsPaymentOpen(false)}
        items={cart}
        branchId={selectedBranchId}
        branchName={selectedBranch?.name || 'Chi nhánh mặc định'}
        branchAddress={selectedBranch?.address}
        branchPhone={selectedBranch?.phone}
        tenantName={currentUser?.tenantName || 'CỬA HÀNG OISMS'}
        cashierName={currentUser?.fullName || 'Thu ngân'}
        currentUser={currentUser}
        onSuccessAndPrint={(receipt) => {
          setPrintedReceipt(receipt);
          setLastConfirmedReceipt(receipt);
          setCart([]); // Chỉ xóa giỏ khi thanh toán thành công

          // POS-STOCK-F01 & T03-AC-06: Làm mới tồn kho qua API ngay sau checkout
          // Tồn 10 -> Checkout 2 -> UI hiển thị 8 không cần F5, kể cả Hub offline
          const itemsToRefresh = (receipt.items || []).map((i: any) => ({
            skuId: (isUuid(i.skuId) ? i.skuId : undefined) || (isUuid(i.variantId) ? i.variantId : undefined),
            skuCode: i.sku || i.skuCode || undefined,
            barcode: i.barcode || undefined,
          }));
          if (selectedBranchId && itemsToRefresh.length > 0) {
            invalidateAndRefetchStock(selectedBranchId, itemsToRefresh).catch(() => {});
          }
        }}
        onPreviewReceipt={(receipt) => {
          setPrintedReceipt(receipt); // Xem tạm tính: Giữ nguyên giỏ hàng
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
          // POS-T01-F01 v1.3: Tìm pending checkout theo chính người dùng vừa xác thực (incoming user).
          const incomingPending = getUserPendingCheckout(user);
          const isSameUser = Boolean(
            currentUser &&
            isSameUserAndTenant(
              {
                tenantId: currentUser.tenantId,
                tenantCode: currentUser.tenantCode,
                userId: currentUser.id,
                email: currentUser.email,
                branchId: selectedBranchId,
              },
              user
            )
          );

          setCurrentUser(user);

          if (incomingPending && (incomingPending.status === 'unknown' || incomingPending.status === 'submitting')) {
            if (incomingPending.status === 'submitting') {
              updatePendingCheckoutStatus(
                'unknown',
                'Yêu cầu thanh toán trước đó bị gián đoạn. Kết quả chưa xác định, vui lòng kiểm tra hoặc thử lại.',
                undefined,
                user,
                incomingPending.owner.branchId
              );
            }

            if (incomingPending.owner.branchId) {
              setSelectedBranchId(incomingPending.owner.branchId);
            }
            setCart((incomingPending.requestPayload.items || []).map((i) => ({
              productId: i.skuId || i.skuCode,
              variantId: i.skuId || i.skuCode,
              sku: i.skuCode,
              barcode: i.skuCode,
              productName: i.productName,
              variantName: i.variantName || '',
              quantity: i.quantity,
              retailPrice: i.unitPrice,
              available: i.quantity,
            })));
            setPrintedReceipt(null);
            setLastConfirmedReceipt(null);
            fetchBranches().then((data) => {
              setBranches(data);
            });
          } else if (isSameUser) {
            // Cùng tài khoản và tenant: giữ nguyên giỏ hàng đang soạn dở
          } else {
            // Khác tài khoản: làm sạch phiên
            setCart([]);
            setPrintedReceipt(null);
            setLastConfirmedReceipt(null);
            fetchBranches().then((data) => {
              setBranches(data);
              if (data.length > 0) setSelectedBranchId(data[0].id);
            });
          }
          setIsLoginOpen(false);
        }}
      />
    </div>
  );
}

export default App;
