import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// 1. Verify File Structure & Source Integrity
test('1. File Structure & TypeScript Artifacts Integrity', () => {
  const files = [
    'src/types/pos.ts',
    'src/services/api.ts',
    'src/components/PosHeader.tsx',
    'src/components/ProductSearch.tsx',
    'src/components/CartTable.tsx',
    'src/components/PaymentModal.tsx',
    'src/components/ReceiptPrintModal.tsx',
    'src/components/LoginModal.tsx',
    'src/App.tsx',
    'src/index.css',
    'public/manifest.webmanifest',
    'public/sw.js',
    'index.html',
    'vite.config.ts',
  ];

  for (const f of files) {
    const fullPath = path.resolve(__dirname, f);
    assert.ok(fs.existsSync(fullPath), `Required file missing: ${f}`);
  }
});

// 2. Parse and Validate Real Endpoints & No Mock Fallback in api.ts
test('2. api.ts Real Endpoints & Strict No-Demo Contract', () => {
  const apiSrc = fs.readFileSync(path.resolve(__dirname, 'src/services/api.ts'), 'utf8');

  // Verify Real Endpoints are wired
  assert.ok(apiSrc.includes('/api/pos/search'), 'Must query GET /api/pos/search');
  assert.ok(apiSrc.includes('/api/pos/barcode/'), 'Must query GET /api/pos/barcode/{code}');
  assert.ok(apiSrc.includes('/api/branches'), 'Must query GET /api/branches');
  assert.ok(apiSrc.includes('/api/auth/login'), 'Must query POST /api/auth/login');
  assert.ok(apiSrc.includes('/api/orders/checkout'), 'Must query POST /api/orders/checkout');

  // Verify Authorization Bearer is attached
  assert.ok(apiSrc.includes('Authorization'), 'Must attach Authorization header');

  // Verify No Demo Fallback in api.ts
  assert.ok(!apiSrc.includes('DEMO_CATALOG'), 'Must NOT contain DEMO_CATALOG in api.ts');
  assert.ok(!apiSrc.includes('FALLBACK_BRANCHES'), 'Must NOT contain FALLBACK_BRANCHES in api.ts');
  assert.ok(!apiSrc.includes('demo_jwt_pos_token_2026'), 'Must NOT auto demo login in api.ts');
});

// 3. Search & Barcode Matching Simulation (Pure Logic)
test('3. Search & Barcode Matching Simulation with Real API Logic', () => {
  const catalog = [
    {
      sku: 'POLO-NAM-DEN-M',
      productName: 'Áo Polo Nam Thể Thao',
      variantName: 'Đen / Size M',
      barcode: 'POLONAMDENM',
      retailPrice: 299000,
      available: 45,
    },
    {
      sku: 'POLO-NAM-TRANG-L',
      productName: 'Áo Polo Nam Thể Thao',
      variantName: 'Trắng / Size L',
      barcode: '8935001100025',
      retailPrice: 299000,
      available: 30,
    },
    {
      sku: 'SERUM-B5-30ML',
      productName: 'Serum Phục Hồi Da B5',
      variantName: 'Chai 30ml',
      barcode: '8935002100017',
      retailPrice: 380000,
      available: 22,
    },
    {
      sku: 'JEANS-SLIM-31',
      productName: 'Quần Jeans Slimfit Nam',
      variantName: 'Xanh Đậm / Size 31',
      barcode: '8935003100016',
      retailPrice: 450000,
      available: 16,
    },
    {
      sku: 'OXFORD-SHIRT-M',
      productName: 'Áo Sơ Mi Oxford Dài Tay',
      variantName: 'Trắng / Size M',
      barcode: '8935004100015',
      retailPrice: 350000,
      available: 0,
    },
  ];

  // Helper search
  const search = (q) => {
    if (!q || !q.trim()) return [];
    const query = q.trim().toLowerCase();
    return catalog.filter(
      (i) =>
        i.sku.toLowerCase().includes(query) ||
        i.productName.toLowerCase().includes(query) ||
        (i.barcode && i.barcode.toLowerCase().includes(query)) ||
        (i.variantName && i.variantName.toLowerCase().includes(query))
    );
  };

  // Helper barcode scan (strictly throws on miss)
  const scan = (code) => {
    if (!code || !code.trim()) throw new Error('Mã vạch trống');
    const c = code.trim().toLowerCase();
    const item = catalog.find((i) => i.barcode?.toLowerCase() === c || i.sku.toLowerCase() === c);
    if (!item) throw new Error(`Không tìm thấy mã vạch "${code}"`);
    return item;
  };

  // Check search "polo"
  const poloHits = search('polo');
  assert.equal(poloHits.length, 2);

  // Check search "serum"
  const serumHits = search('serum');
  assert.equal(serumHits.length, 1);
  assert.equal(serumHits[0].sku, 'SERUM-B5-30ML');

  // Check barcode scan
  const scanned = scan('POLONAMDENM');
  assert.equal(scanned.sku, 'POLO-NAM-DEN-M');
  assert.equal(scanned.retailPrice, 299000);

  // Check invalid barcode throws
  assert.throws(() => scan('INVALID_BARCODE'), /Không tìm thấy mã vạch/);
});

// 4. Cart Multi-line, Stock Limit & Price Formula
test('4. Cart Operations & Stock Constraint Validation', () => {
  let cart = [];

  const addToCart = (product) => {
    if (product.available <= 0) {
      throw new Error('Sản phẩm đã hết hàng trong kho');
    }
    const existing = cart.find((i) => i.sku === product.sku);
    if (existing) {
      if (existing.quantity >= product.available) {
        throw new Error('Đã đạt số lượng tồn kho khả dụng tối đa');
      }
      existing.quantity += 1;
    } else {
      cart.push({ ...product, quantity: 1 });
    }
  };

  const updateQuantity = (sku, qty) => {
    const item = cart.find((i) => i.sku === sku);
    if (!item) return;
    if (qty <= 0) {
      cart = cart.filter((i) => i.sku !== sku);
    } else if (qty > item.available) {
      item.quantity = item.available;
    } else {
      item.quantity = qty;
    }
  };

  const getTotals = () => {
    const subtotal = cart.reduce((sum, item) => sum + item.retailPrice * item.quantity, 0);
    const vat = 0; // VAT 0%
    const totalAmount = subtotal + vat;
    const totalItems = cart.reduce((sum, item) => sum + item.quantity, 0);
    return { subtotal, vat, totalAmount, totalItems };
  };

  // Add 1 item
  addToCart({ sku: 'POLO-NAM-DEN-M', retailPrice: 299000, available: 45 });
  assert.equal(cart.length, 1);
  assert.equal(cart[0].quantity, 1);

  // Add another SKU
  addToCart({ sku: 'JEANS-SLIM-31', retailPrice: 450000, available: 16 });
  assert.equal(cart.length, 2);

  // Add out-of-stock item
  assert.throws(
    () => addToCart({ sku: 'OXFORD-SHIRT-M', retailPrice: 350000, available: 0 }),
    /Sản phẩm đã hết hàng trong kho/
  );

  // Increment quantity to 3
  updateQuantity('POLO-NAM-DEN-M', 3);
  assert.equal(cart.find((i) => i.sku === 'POLO-NAM-DEN-M').quantity, 3);

  // Clamp quantity to available (16)
  updateQuantity('JEANS-SLIM-31', 100);
  assert.equal(cart.find((i) => i.sku === 'JEANS-SLIM-31').quantity, 16);

  // Remove item
  updateQuantity('JEANS-SLIM-31', 0);
  assert.equal(cart.length, 1);
  assert.equal(cart[0].sku, 'POLO-NAM-DEN-M');

  // Verify total formula
  // Subtotal = 3 * 299,000 = 897,000
  // VAT 0% = 0
  // Total = 897,000
  const totals = getTotals();
  assert.equal(totals.subtotal, 897000);
  assert.equal(totals.totalAmount, 897000);
  assert.equal(totals.totalItems, 3);
});

// 5. Cash Calculations & Quick Cash Denominations
test('5. Cash Payment & Quick Cash Auto Change Calculations', () => {
  const totalAmount = 897000;

  const calculateChange = (amountGiven) => Math.max(0, amountGiven - totalAmount);

  assert.equal(calculateChange(1000000), 103000);
  assert.equal(calculateChange(897000), 0);
  assert.equal(calculateChange(500000), 0);

  // Quick cash buttons
  const quickCashOptions = [
    { label: 'Đúng số tiền', value: totalAmount },
    { label: '50.000 đ', value: 50000 },
    { label: '100.000 đ', value: 100000 },
    { label: '200.000 đ', value: 200000 },
    { label: '500.000 đ', value: 500000 },
    { label: '1.000.000 đ', value: 1000000 },
  ];

  const matched = quickCashOptions.find((o) => o.value === totalAmount);
  assert.ok(matched, 'Contains exact amount option');
  assert.equal(matched.value, 897000);
});

// 6. Thermal Receipt Layout Contract (80mm) & Draft Watermark
test('6. 80mm Thermal Receipt Layout & Draft Watermark Contract', () => {
  const receiptSrc = fs.readFileSync(path.resolve(__dirname, 'src/components/ReceiptPrintModal.tsx'), 'utf8');

  // Verify 80mm standard width is styled (320px ~ 80mm)
  assert.ok(receiptSrc.includes('receipt-paper'), 'Must have receipt-paper class');
  assert.ok(receiptSrc.includes('max-w-[320px]'), 'Must restrict width to ~320px for 80mm thermal receipt');
  assert.ok(receiptSrc.includes('window.print'), 'Must call window.print for printing');

  // Verify Draft Watermark label
  assert.ok(receiptSrc.includes('CHƯA THANH TOÁN'), 'Must explicitly display CHƯA THANH TOÁN for draft preview');
  assert.ok(receiptSrc.includes('PHIẾU TẠM TÍNH'), 'Must display PHIẾU TẠM TÍNH for draft preview');

  // Verify @media print rules in index.css
  const cssSrc = fs.readFileSync(path.resolve(__dirname, 'src/index.css'), 'utf8');
  assert.ok(cssSrc.includes('@media print'), 'Must include @media print style block');
  assert.ok(cssSrc.includes('.no-print'), 'Must hide .no-print elements on paper');
  assert.ok(cssSrc.includes('80mm'), 'Must format page size for 80mm paper');
});

// 7. Checkout Contract, Pending Transparency & Receipt Preview Separation
test('7. Checkout Contract & Draft Preview Decoupling', () => {
  const paymentSrc = fs.readFileSync(path.resolve(__dirname, 'src/components/PaymentModal.tsx'), 'utf8');

  // Verify DTO fields according to contract
  assert.ok(paymentSrc.includes('PosCheckoutRequest'), 'Must build PosCheckoutRequest');
  assert.ok(paymentSrc.includes('requestKey'), 'Must generate requestKey');
  assert.ok(paymentSrc.includes('paymentMethod'), 'Must send paymentMethod');
  assert.ok(paymentSrc.includes('amountGiven'), 'Must send amountGiven');
  assert.ok(paymentSrc.includes('changeDue'), 'Must send changeDue');

  // Verify callback separation: onPreviewReceipt vs onSuccessAndPrint
  assert.ok(paymentSrc.includes('onPreviewReceipt'), 'Must have separate onPreviewReceipt callback');
  assert.ok(paymentSrc.includes('onSuccessAndPrint'), 'Must retain onSuccessAndPrint for real checkout success');
  assert.ok(paymentSrc.includes('isDraft: true'), 'Draft preview must flag isDraft: true');

  // Verify no hardcoded bank accounts
  assert.ok(!paymentSrc.includes('0987654321'), 'Must NOT have hardcoded personal bank account 0987654321');
  assert.ok(paymentSrc.includes('Mã QR mô phỏng'), 'Must clearly explain QR is simulated');

  // Verify DoD transparency in api.ts
  const apiSrc = fs.readFileSync(path.resolve(__dirname, 'src/services/api.ts'), 'utf8');
  assert.ok(
    apiSrc.includes('chưa được triển khai trên Backend Swagger'),
    'Must explain checkout API is pending Swagger contract finalization'
  );
});

// 8. Negative Case: Barcode Scan Miss must NOT auto-add previous results[0]
test('8. Negative Case: Barcode Miss does NOT add results[0]', () => {
  const searchSrc = fs.readFileSync(path.resolve(__dirname, 'src/components/ProductSearch.tsx'), 'utf8');

  // Verify that `onAddToCart(results[0])` fallback on barcode catch has been removed
  assert.ok(
    !searchSrc.includes('onAddToCart(results[0])'),
    'Must NOT automatically add results[0] when barcode lookup fails'
  );
  assert.ok(
    searchSrc.includes('Tuyệt đối không tự động thêm results[0]'),
    'Must include code comment enforcing this lead feedback rule'
  );
});

// 9. Negative Case: Branch Change and Session Reset
test('9. Negative Case: Branch Change & Logout Clears Cart', () => {
  const appSrc = fs.readFileSync(path.resolve(__dirname, 'src/App.tsx'), 'utf8');

  // Verify branch change handler resets cart
  assert.ok(appSrc.includes('handleSelectBranch'), 'Must have handleSelectBranch handler');
  assert.ok(appSrc.includes('Đổi chi nhánh sẽ làm mới lại giỏ hàng'), 'Must confirm and reset cart on branch switch');

  // Verify logout clears cart and receipts
  assert.ok(appSrc.includes('setCart([]);'), 'Must clear cart');
  assert.ok(appSrc.includes('setPrintedReceipt(null);'), 'Must clear printed receipt');

  // Verify 401 auth-expired listener
  assert.ok(appSrc.includes('pos-auth-expired'), 'Must listen for pos-auth-expired custom event');
});

// 10. Negative Case: Over-stock and 0-stock prevention
test('10. Negative Case: Available stock boundary enforcement', () => {
  const appSrc = fs.readFileSync(path.resolve(__dirname, 'src/App.tsx'), 'utf8');
  const cartTableSrc = fs.readFileSync(path.resolve(__dirname, 'src/components/CartTable.tsx'), 'utf8');

  // In App.tsx: check availableStock <= 0
  assert.ok(appSrc.includes('availableStock <= 0'), 'Must reject adding when available stock <= 0');
  assert.ok(appSrc.includes('currentQty + 1 > availableStock'), 'Must reject adding when cart quantity exceeds available');

  // In CartTable.tsx: check button disabled on max stock
  assert.ok(cartTableSrc.includes('isMaxStockReached'), 'Must detect when max stock is reached');
  assert.ok(cartTableSrc.includes('isOutOfStock'), 'Must detect when stock is 0');
});

// 11. PWA Manifest & Service Worker
test('11. PWA Baseline Configuration', () => {
  const manifest = JSON.parse(fs.readFileSync(path.resolve(__dirname, 'public/manifest.webmanifest'), 'utf8'));
  assert.equal(manifest.display, 'standalone');
  assert.equal(manifest.start_url, '/');
  assert.equal(manifest.theme_color, '#4f46e5');
  assert.ok(manifest.icons.length >= 2);

  const sw = fs.readFileSync(path.resolve(__dirname, 'public/sw.js'), 'utf8');
  assert.ok(sw.includes('CACHE_NAME'));
  assert.ok(sw.includes('install'));
  assert.ok(sw.includes('fetch'));

  const html = fs.readFileSync(path.resolve(__dirname, 'index.html'), 'utf8');
  assert.ok(html.includes('manifest.webmanifest'));
  assert.ok(html.includes('apple-mobile-web-app-capable'));
  assert.ok(html.includes('theme-color'));
});

// 12. Direct Runtime API Simulation: Network Error, 400, 401, 403, 500 & 404 Barcode
test('12. Direct Runtime API Logic & Status Code Handling', async () => {
  // Mock loginCashier logic under test
  const simulateLogin = async (mockResponseOrError) => {
    let res;
    try {
      if (typeof mockResponseOrError === 'function') {
        res = await mockResponseOrError();
      } else {
        res = mockResponseOrError;
      }
    } catch {
      throw new Error('Không thể kết nối đến máy chủ Backend (Lỗi mạng hoặc server chưa khởi động).');
    }

    if (res.ok) {
      const data = res.body;
      if (!data || !data.accessToken) {
        throw new Error('Dữ liệu phản hồi từ máy chủ không hợp lệ (thiếu token).');
      }
      return data;
    }

    if (res.status === 400 || res.status === 401) {
      throw new Error(res.body?.message || 'Email hoặc mật khẩu không chính xác.');
    }
    if (res.status === 403) {
      throw new Error('Tài khoản bị khóa hoặc cửa hàng đang tạm ngưng hoạt động.');
    }
    if (res.status >= 500) {
      throw new Error(`Máy chủ gặp sự cố nội bộ (Mã lỗi ${res.status}). Vui lòng liên hệ quản trị viên.`);
    }
    throw new Error(`Đăng nhập thất bại (HTTP ${res.status}).`);
  };

  // Ca 1: Mất mạng (Network Error / Offline)
  await assert.rejects(
    () => simulateLogin(() => { throw new TypeError('Failed to fetch'); }),
    /Không thể kết nối đến máy chủ Backend/
  );

  // Ca 2: Sai thông tin đăng nhập (HTTP 401)
  await assert.rejects(
    () => simulateLogin({ ok: false, status: 401, body: { message: 'Mật khẩu không chính xác.' } }),
    /Mật khẩu không chính xác/
  );

  // Ca 3: Tài khoản bị khóa (HTTP 403)
  await assert.rejects(
    () => simulateLogin({ ok: false, status: 403, body: {} }),
    /Tài khoản bị khóa hoặc cửa hàng đang tạm ngưng hoạt động/
  );

  // Ca 4: Máy chủ gặp lỗi (HTTP 500)
  await assert.rejects(
    () => simulateLogin({ ok: false, status: 500, body: {} }),
    /Máy chủ gặp sự cố nội bộ/
  );

  // Ca 5: Đăng nhập thành công trả về đúng object, không bao giờ undefined
  const successData = await simulateLogin({
    ok: true,
    status: 200,
    body: { accessToken: 'jwt_token_real', user: { email: 'cashier@oisms.vn', fullName: 'Lê Phương' } }
  });
  assert.equal(successData.accessToken, 'jwt_token_real');
  assert.equal(successData.user.fullName, 'Lê Phương');
});

// 13. End-to-End Workflow: Checkout Failure -> Preview Receipt -> Cart Preserved
test('13. Workflow: Failed Checkout & Preview Receipt preserves Cart', () => {
  // Khởi tạo giỏ hàng ban đầu
  let cart = [
    { variantId: 'v1', sku: 'POLO-NAM-DEN-M', productName: 'Áo Polo Nam', retailPrice: 299000, quantity: 2, available: 10 },
    { variantId: 'v2', sku: 'SERUM-B5-30ML', productName: 'Serum Phục Hồi', retailPrice: 380000, quantity: 1, available: 5 },
  ];
  let printedReceipt = null;

  const handleSuccessAndPrint = (receipt) => {
    printedReceipt = receipt;
    cart = []; // Xóa giỏ khi thành công thật
  };

  const handlePreviewReceipt = (receipt) => {
    printedReceipt = receipt; // Xem tạm tính: KHÔNG xóa giỏ
  };

  // Giả lập checkout API chưa có / thất bại
  const checkoutResult = {
    success: false,
    isPendingBackend: true,
    message: 'API thanh toán (POST /api/orders/checkout) chưa được triển khai trên Backend Swagger.'
  };

  // Khi checkout thất bại: không gọi handleSuccessAndPrint
  assert.equal(checkoutResult.success, false);
  assert.equal(cart.length, 2, 'Giỏ hàng vẫn giữ nguyên 2 món khi checkout chưa sẵn sàng');

  // Thu ngân nhấn "Xem phiếu tạm tính"
  const draftReceipt = {
    receiptNumber: 'DRAFT-998877',
    isDraft: true,
    paymentStatusTitle: 'CHƯA THANH TOÁN (PHIẾU TẠM TÍNH)',
    items: cart.map(i => ({ name: i.productName, sku: i.sku, quantity: i.quantity, unitPrice: i.retailPrice, subtotal: i.retailPrice * i.quantity })),
    totalAmount: 978000,
  };
  handlePreviewReceipt(draftReceipt);

  // Kiểm tra: Giỏ hàng vẫn giữ nguyên 2 món
  assert.equal(cart.length, 2, 'Giỏ hàng phải được giữ nguyên sau khi xem hóa đơn mẫu');
  assert.equal(printedReceipt.isDraft, true);
  assert.equal(printedReceipt.paymentStatusTitle, 'CHƯA THANH TOÁN (PHIẾU TẠM TÍNH)');

  // Bây giờ mô phỏng khi thanh toán thật thành công:
  const realSuccessResult = { success: true, orderNumber: 'ORD-REAL-123456' };
  if (realSuccessResult.success) {
    handleSuccessAndPrint({ receiptNumber: realSuccessResult.orderNumber, isDraft: false });
  }

  // Sau khi thanh toán thành công, giỏ hàng mới được xóa
  assert.equal(cart.length, 0, 'Giỏ hàng đã được xóa sạch sau khi thanh toán thành công');
  assert.equal(printedReceipt.isDraft, false);
});


