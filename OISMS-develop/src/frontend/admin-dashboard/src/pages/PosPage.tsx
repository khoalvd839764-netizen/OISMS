import {
  ArrowLeft,
  Banknote,
  LogOut,
  Minus,
  PackageSearch,
  Plus,
  QrCode,
  Search,
  ShoppingBag,
  X,
} from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Badge, Button, Input } from '../components/common/ui';
import { NotificationCenter } from '../components/layout/NotificationCenter';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../components/feedback/Toast';
import { catalogApi } from '../api/catalog.api';
import { parseApiError } from '../api/client';
import type { ProductResponse } from '../types/catalog.types';

interface PosSkuItem {
  variantId: string;
  sku: string;
  barcode: string | null;
  productName: string;
  variantName: string;
  retailPrice: number;
}

export function PosPage() {
  const { user, role, hasRole, logout } = useAuth();
  const { showToast } = useToast();
  const navigate = useNavigate();

  const [products, setProducts] = useState<ProductResponse[]>([]);
  const [query, setQuery] = useState('');
  const [cart, setCart] = useState<{ item: PosSkuItem; quantity: number }[]>([]);
  const [payment, setPayment] = useState<'cash' | 'qr'>('cash');

  const loadCatalog = useCallback(async () => {
    try {
      const data = await catalogApi.getProducts();
      setProducts(data);
    } catch (err) {
      const parsed = parseApiError(err);
      showToast('error', 'Không thể tải danh mục sản phẩm POS', parsed.message);
    }
  }, [showToast]);

  useEffect(() => {
    void loadCatalog();
  }, [loadCatalog]);

  const skuList = useMemo<PosSkuItem[]>(() => {
    const items: PosSkuItem[] = [];
    for (const p of products) {
      if (!p.isActive) continue;
      for (const v of p.variants || []) {
        if (!v.isActive) continue;
        items.push({
          variantId: v.id,
          sku: v.sku,
          barcode: v.barcode,
          productName: p.name,
          variantName: v.variantName,
          retailPrice: v.retailPrice,
        });
      }
    }
    return items;
  }, [products]);

  const filteredSkus = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return skuList;
    return skuList.filter(
      (s) =>
        s.productName.toLowerCase().includes(q) ||
        s.sku.toLowerCase().includes(q) ||
        (s.barcode && s.barcode.toLowerCase().includes(q))
    );
  }, [skuList, query]);

  const total = cart.reduce((sum, line) => sum + line.item.retailPrice * line.quantity, 0);

  const addToCart = (skuItem: PosSkuItem) => {
    setCart((current) => {
      const found = current.find((line) => line.item.variantId === skuItem.variantId);
      if (found) {
        return current.map((line) =>
          line.item.variantId === skuItem.variantId
            ? { ...line, quantity: line.quantity + 1 }
            : line
        );
      }
      return [...current, { item: skuItem, quantity: 1 }];
    });
  };

  const handleLogout = async () => {
    await logout();
    navigate('/login', { replace: true });
  };

  return (
    <div className="pos-shell">
      <header className="pos-header">
        <div className="pos-brand">
          <div className="brand-mark">O</div>
          <strong>{user?.tenantName || 'OISM'} POS</strong>
          <span>{user?.tenantCode}</span>
        </div>
        <div className="pos-status">
          <NotificationCenter compact />
          <div className="avatar">
            {user?.fullName
              ?.split(' ')
              .slice(-2)
              .map((p) => p[0])
              .join('')
              .toUpperCase() || 'POS'}
          </div>
          <div>
            <strong>{user?.fullName || 'Thu ngân'}</strong>
            <span>{role}</span>
          </div>
          {hasRole(['Owner', 'Staff']) && (
            <Link to="/" title="Quay về Web Admin">
              <ArrowLeft size={19} />
            </Link>
          )}
          <Button variant="ghost" aria-label="Đăng xuất" onClick={() => void handleLogout()}>
            <LogOut size={18} />
          </Button>
        </div>
      </header>

      <main className="pos-main">
        <section className="pos-catalog">
          <label className="pos-search">
            <Search size={22} />
            <Input
              autoFocus
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Quét mã vạch EAN-13 hoặc tìm tên sản phẩm, mã SKU..."
            />
            <kbd>F2</kbd>
          </label>

          <div className="pos-products">
            {filteredSkus.map((sku) => (
              <button
                key={sku.variantId}
                type="button"
                className="pos-product"
                onClick={() => addToCart(sku)}
              >
                <span className="pos-product-img">{sku.sku.slice(0, 2).toUpperCase()}</span>
                <div>
                  <strong>{sku.productName}</strong>
                  <small>
                    {sku.variantName} · {sku.sku}
                  </small>
                  <span>{sku.retailPrice.toLocaleString('vi-VN')} ₫</span>
                  {sku.barcode && <Badge tone="neutral">{sku.barcode}</Badge>}
                </div>
              </button>
            ))}
          </div>
        </section>

        <aside className="cart-panel">
          <div className="cart-head">
            <div>
              <ShoppingBag size={21} />
              <h2>Giỏ hàng POS</h2>
              <Badge tone="info">{cart.length}</Badge>
            </div>
            <Button variant="ghost" onClick={() => setCart([])}>
              Xóa giỏ
            </Button>
          </div>

          <div className="cart-items">
            {cart.length === 0 ? (
              <div className="cart-empty">
                <PackageSearch size={36} />
                <strong>Giỏ hàng trống</strong>
                <span>Quét barcode hoặc chọn sản phẩm bên trái</span>
              </div>
            ) : (
              cart.map((line) => (
                <div className="cart-item" key={line.item.variantId}>
                  <span className="product-thumb">
                    {line.item.sku.slice(0, 2).toUpperCase()}
                  </span>
                  <div>
                    <strong>{line.item.productName}</strong>
                    <small>
                      {line.item.sku} · {line.item.variantName}
                    </small>
                    <span>{line.item.retailPrice.toLocaleString('vi-VN')} ₫</span>
                  </div>
                  <div className="qty">
                    <Button
                      variant="secondary"
                      onClick={() =>
                        setCart(
                          cart.map((c) =>
                            c.item.variantId === line.item.variantId
                              ? { ...c, quantity: Math.max(1, c.quantity - 1) }
                              : c
                          )
                        )
                      }
                    >
                      <Minus size={15} />
                    </Button>
                    <strong>{line.quantity}</strong>
                    <Button variant="secondary" onClick={() => addToCart(line.item)}>
                      <Plus size={15} />
                    </Button>
                  </div>
                  <Button
                    variant="ghost"
                    onClick={() =>
                      setCart(cart.filter((c) => c.item.variantId !== line.item.variantId))
                    }
                  >
                    <X size={17} />
                  </Button>
                </div>
              ))
            )}
          </div>

          <div className="cart-summary">
            <span>
              Tạm tính<strong>{total.toLocaleString('vi-VN')} ₫</strong>
            </span>
            <span className="cart-total">
              Tổng thanh toán<strong>{total.toLocaleString('vi-VN')} ₫</strong>
            </span>
          </div>

          <div className="payment-tabs">
            <button
              type="button"
              className={payment === 'cash' ? 'active' : ''}
              onClick={() => setPayment('cash')}
            >
              <Banknote size={19} /> Tiền mặt
            </button>
            <button
              type="button"
              className={payment === 'qr' ? 'active' : ''}
              onClick={() => setPayment('qr')}
            >
              <QrCode size={19} /> Chuyển khoản QR
            </button>
          </div>
        </aside>
      </main>
    </div>
  );
}
