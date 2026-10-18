import { ArrowLeft, Banknote, CheckCircle2, CircleHelp, Download, LogOut, Minus, PackageSearch, Plus, Printer, QrCode, Search, ShoppingBag, X } from "lucide-react";
import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Badge, Button, Input, Modal } from "../components/ui";
import { apiAdapter } from "../services/apiAdapter";
import type { Sku } from "../types/domain";
import { useAuth } from "../context/AuthContext";
import { NotificationCenter } from "../components/NotificationCenter";

export function PosPage() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [skusList, setSkusList] = useState<Sku[]>([]);
  const [categories, setCategories] = useState<Array<{ id: string; name: string; rawName?: string }>>([]);
  const [selectedCategory, setSelectedCategory] = useState("");
  const [loadingSkus, setLoadingSkus] = useState(true);
  const [cart, setCart] = useState<{ sku: Sku; quantity: number }[]>([]);
  const [query, setQuery] = useState("");
  const [payment, setPayment] = useState<"cash" | "qr">("cash");
  const [processing, setProcessing] = useState(false);
  const [result, setResult] = useState<"success" | "error" | null>(null);
  const [lastOrderCode, setLastOrderCode] = useState("");
  const [checkoutErrorMsg, setCheckoutErrorMsg] = useState("");
  const [pwaHelp, setPwaHelp] = useState(false);

  const loadPosCatalog = async () => {
    setLoadingSkus(true);
    const [items, cats] = await Promise.all([
      apiAdapter.getSkus(),
      apiAdapter.getCategories(),
    ]);
    setSkusList(items.data || []);
    if ("data" in cats && Array.isArray(cats.data)) {
      setCategories(cats.data);
    }
    setLoadingSkus(false);
  };

  useEffect(() => {
    loadPosCatalog();
  }, []);

  const total = cart.reduce((sum, item) => sum + item.sku.sellingPrice * item.quantity, 0);
  const add = (sku: Sku) => setCart((current) => {
    const avail = Math.max(0, sku.onHand - sku.reserved);
    if (avail <= 0) {
      setCheckoutErrorMsg(`Sản phẩm ${sku.code} hiện đã hết tồn khả dụng (Available = 0).`);
      setResult("error");
      return current;
    }
    const found = current.find((item) => item.sku.id === sku.id);
    if (found) return current.map((item) => item.sku.id === sku.id ? { ...item, quantity: Math.min(item.quantity + 1, avail) } : item);
    return [...current, { sku, quantity: 1 }];
  });

  const handleBarcodeEnter = async (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key !== "Enter" || !query.trim()) return;
    e.preventDefault();
    const keyword = query.trim();
    const localMatch = skusList.find(
      (s) => s.barcode === keyword || s.code.toLowerCase() === keyword.toLowerCase()
    );
    if (localMatch) {
      add(localMatch);
      setQuery("");
      return;
    }
    const apiMatch = await apiAdapter.posGetByBarcode(keyword);
    if (apiMatch.data?.variantId) {
      const matched = skusList.find((s) => s.id === apiMatch.data.variantId);
      if (matched) {
        add(matched);
        setQuery("");
      }
    }
  };

  const checkout = async () => {
    if (!cart[0]) return;
    setProcessing(true);
    setCheckoutErrorMsg("");
    const response = await apiAdapter.checkout(cart[0].sku, cart[0].quantity, "POS", "Khách lẻ tại quầy POS");
    setProcessing(false);
    if (response.error) {
      setCheckoutErrorMsg(response.error.message);
      setResult("error");
    } else {
      setLastOrderCode(response.data?.orderCode || `POS-${Date.now().toString().slice(-6)}`);
      setResult("success");
      await loadPosCatalog();
    }
  };

  const displayName = user?.name || "Nhân viên";
  const userInitials = user?.initials || "NV";

  const filteredSkus = skusList.filter((sku) => {
    const matchesSearch = `${sku.name} ${sku.code} ${sku.barcode || ""}`
      .toLowerCase()
      .includes(query.toLowerCase());
    const matchesCat = !selectedCategory || sku.categoryId === selectedCategory;
    return matchesSearch && matchesCat;
  });

  return (
    <div className="pos-shell">
      <header className="pos-header">
        <div className="pos-brand">
          <div className="brand-mark">O</div>
          <strong>OISM POS</strong>
          <span>{user?.tenantName || "Chi nhánh chính"}</span>
        </div>
        <div className="pos-status">
          <Button variant="ghost" onClick={() => setPwaHelp(true)}>
            <Download size={17} />
            <span className="install-label">Cài ứng dụng</span>
          </Button>
          <NotificationCenter compact />
          <div className="avatar">{userInitials}</div>
          <div>
            <strong>{displayName}</strong>
            <span>{user?.role || "Cashier"}</span>
          </div>
          <Link to="/" aria-label="Quay lại Admin" title="Quay lại Admin">
            <ArrowLeft size={19} />
          </Link>
          <Button variant="ghost" aria-label="Đăng xuất" title="Đăng xuất" onClick={async () => { await logout(); navigate("/login"); }}>
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
              onKeyDown={handleBarcodeEnter}
              placeholder="Quét barcode (nhấn Enter) hoặc tìm tên, SKU..."
            />
            <kbd>Enter</kbd>
          </label>
          <div className="category-pills">
            <Button
              variant={selectedCategory === "" ? "primary" : "secondary"}
              onClick={() => setSelectedCategory("")}
            >
              Tất cả ({skusList.length})
            </Button>
            {categories.map((cat) => (
              <Button
                key={cat.id}
                variant={selectedCategory === cat.id ? "primary" : "secondary"}
                onClick={() => setSelectedCategory(cat.id)}
              >
                {cat.rawName || cat.name}
              </Button>
            ))}
          </div>
          <div className="pos-products">
            {loadingSkus ? (
              <div style={{ padding: "2rem", color: "var(--text-muted)" }}>Đang tải danh mục sản phẩm...</div>
            ) : filteredSkus.length === 0 ? (
              <div style={{ padding: "2rem", color: "var(--text-muted)" }}>Chưa có sản phẩm nào khớp bộ lọc.</div>
            ) : (
              filteredSkus.map((sku) => (
                <button key={sku.id} className="pos-product" onClick={() => add(sku)}>
                  <span className="pos-product-img">{sku.image || "📦"}</span>
                  <div>
                    <strong>{sku.name}</strong>
                    <small>{sku.code} {sku.variant ? `· ${sku.variant}` : ""}</small>
                    <span>{sku.sellingPrice.toLocaleString("vi-VN")} ₫</span>
                    <Badge tone={sku.onHand - sku.reserved <= sku.threshold ? "warning" : "success"}>
                      Khả dụng: {sku.onHand - sku.reserved}
                    </Badge>
                  </div>
                </button>
              ))
            )}
          </div>
        </section>
        <aside className="cart-panel">
          <div className="cart-head">
            <div>
              <ShoppingBag size={21} />
              <h2>Đơn hiện tại</h2>
              <Badge tone="info">{cart.length}</Badge>
            </div>
            <Button variant="ghost" onClick={() => setCart([])}>Xóa giỏ</Button>
          </div>
          <div className="cart-customer">
            <span>Khách hàng: <strong>Khách lẻ tại quầy</strong></span>
          </div>
          <div className="cart-items">
            {cart.length === 0 ? (
              <div className="cart-empty">
                <PackageSearch size={36} />
                <strong>Giỏ hàng trống</strong>
                <span>Quét barcode hoặc chọn sản phẩm</span>
              </div>
            ) : (
              cart.map((item) => (
                <div className="cart-item" key={item.sku.id}>
                  <span className="product-thumb">{item.sku.image || "📦"}</span>
                  <div>
                    <strong>{item.sku.name}</strong>
                    <small>{item.sku.code} · {item.sku.variant}</small>
                    <span>{item.sku.sellingPrice.toLocaleString("vi-VN")} ₫</span>
                  </div>
                  <div className="qty">
                    <Button variant="secondary" onClick={() => setCart(cart.map((line) => line.sku.id === item.sku.id ? { ...line, quantity: Math.max(1, line.quantity - 1) } : line))}>
                      <Minus size={15} />
                    </Button>
                    <strong>{item.quantity}</strong>
                    <Button variant="secondary" onClick={() => add(item.sku)}>
                      <Plus size={15} />
                    </Button>
                  </div>
                  <Button variant="ghost" onClick={() => setCart(cart.filter((line) => line.sku.id !== item.sku.id))}>
                    <X size={17} />
                  </Button>
                </div>
              ))
            )}
          </div>
          <div className="cart-summary">
            <span>Tạm tính<strong>{total.toLocaleString("vi-VN")} ₫</strong></span>
            <span>Giảm giá<strong>0 ₫</strong></span>
            <span className="cart-total">Tổng thanh toán<strong>{total.toLocaleString("vi-VN")} ₫</strong></span>
          </div>
          <div className="payment-tabs">
            <button className={payment === "cash" ? "active" : ""} onClick={() => setPayment("cash")}>
              <Banknote size={19} />Tiền mặt
            </button>
            <button className={payment === "qr" ? "active" : ""} onClick={() => setPayment("qr")}>
              <QrCode size={19} />QR Chuyển khoản
            </button>
          </div>
          {payment === "cash" ? (
            <div className="cash-row">
              <label>
                <span>Khách đưa</span>
                <Input value={total ? Math.max(total, 500000).toLocaleString("vi-VN") : "0"} readOnly />
              </label>
              <div>
                <span>Tiền thừa</span>
                <strong>{Math.max(0, Math.max(total, 500000) - total).toLocaleString("vi-VN")} ₫</strong>
              </div>
            </div>
          ) : (
            <div className="qr-note">
              <QrCode size={28} />
              <span>Mã QR thanh toán động. Thu ngân xác nhận sau khi nhận thông báo giao dịch.</span>
            </div>
          )}
          <Button className="checkout" disabled={!cart.length || processing} onClick={checkout}>
            {processing ? "Đang khóa tồn kho & ghi nhận đơn..." : `Thanh toán · ${total.toLocaleString("vi-VN")} ₫`}
            <kbd>Enter</kbd>
          </Button>
          <div className="pos-footer">
            <span><CircleHelp size={15} />Enter Quét mã · Khóa tồn Advisory Lock</span>
            <span>POST /api/inventory/reserve</span>
          </div>
        </aside>
      </main>
      {result && (
        <Modal title={result === "success" ? "Thanh toán & giữ chỗ thành công" : "Không thể hoàn tất giao dịch"} onClose={() => setResult(null)}>
          {result === "success" ? (
            <div className="success-panel">
              <CheckCircle2 size={48} color="#16a34a" />
              <h3>Mã đơn: {lastOrderCode}</h3>
              <p>Đã khóa giữ chỗ tồn kho nguyên tử (Advisory Lock) và tạo đơn hàng lúc {new Date().toLocaleTimeString("vi-VN")}.</p>
              <strong className="success-amount">{total.toLocaleString("vi-VN")} ₫</strong>
              <div className="modal-actions">
                <Button variant="secondary" onClick={() => window.print()}><Printer size={17} />In hóa đơn</Button>
                <Button onClick={() => { setResult(null); setCart([]); }}>Đơn mới</Button>
              </div>
            </div>
          ) : (
            <div className="error-panel">
              <X size={40} color="#dc2626" />
              <h3>Từ chối giao dịch (Chống bán âm)</h3>
              <p>{checkoutErrorMsg || "Tồn kho khả dụng vừa thay đổi. Vui lòng kiểm tra lại số dư khả dụng trước khi thanh toán."}</p>
              <Button onClick={() => setResult(null)}>Kiểm tra lại tồn</Button>
            </div>
          )}
        </Modal>
      )}
      {pwaHelp && (
        <Modal title="Cài OISM POS trên thiết bị" onClose={() => setPwaHelp(false)}>
          <div className="pwa-guide">
            <Badge tone="info">PWA Ready</Badge>
            <h3>Manifest đã được cấu hình</h3>
            <p>Trên Chrome hoặc Edge, mở menu trình duyệt và chọn “Cài đặt OISM POS”. Trên thiết bị cầm tay, có thể chọn “Thêm vào màn hình chính”.</p>
            <div><strong>Đã hiện thực</strong><span>Web app manifest, icon, màu giao diện và bố cục responsive.</span></div>
            <div><strong>Cơ chế an toàn</strong><span>Mọi giao dịch checkout tại POS đều được xác thực tồn khả dụng trực tiếp qua PostgreSQL Advisory Lock trên Backend.</span></div>
            <Button onClick={() => setPwaHelp(false)}>Đã hiểu</Button>
          </div>
        </Modal>
      )}
    </div>
  );
}
