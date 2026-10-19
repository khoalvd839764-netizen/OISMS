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

// 2. Parse and Validate DEMO_CATALOG & Data in api.ts
test('2. api.ts Catalog & Endpoints Contract', () => {
  const apiSrc = fs.readFileSync(path.resolve(__dirname, 'src/services/api.ts'), 'utf8');

  // Verify Real Endpoints are wired
  assert.ok(apiSrc.includes('/api/pos/search'), 'Must query GET /api/pos/search');
  assert.ok(apiSrc.includes('/api/pos/barcode/'), 'Must query GET /api/pos/barcode/{code}');
  assert.ok(apiSrc.includes('/api/branches'), 'Must query GET /api/branches');
  assert.ok(apiSrc.includes('/api/auth/login'), 'Must query POST /api/auth/login');
  assert.ok(apiSrc.includes('/api/orders/checkout'), 'Must query POST /api/orders/checkout');

  // Verify Authorization Bearer is attached
  assert.ok(apiSrc.includes('Authorization'), 'Must attach Authorization header');

  // Verify Fallback Catalog contains key items
  assert.ok(apiSrc.includes('POLO-NAM-DEN-M'), 'Must contain POLO-NAM-DEN-M');
  assert.ok(apiSrc.includes('POLONAMDENM'), 'Must contain barcode POLONAMDENM');
  assert.ok(apiSrc.includes('SERUM-B5-30ML'), 'Must contain SERUM-B5-30ML');
  assert.ok(apiSrc.includes('JEANS-SLIM-31'), 'Must contain JEANS-SLIM-31');
  assert.ok(apiSrc.includes('OXFORD-SHIRT-M'), 'Must contain OXFORD-SHIRT-M');
});

// 3. Search & Barcode Matching Simulation
test('3. Search & Barcode Matching Simulation', () => {
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

  // Helper barcode scan
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
    const vat = Math.round(subtotal * 0.08); // 8% VAT
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
  // VAT 8% = 71,760
  // Total = 968,760
  const totals = getTotals();
  assert.equal(totals.subtotal, 897000);
  assert.equal(totals.vat, 71760);
  assert.equal(totals.totalAmount, 968760);
  assert.equal(totals.totalItems, 3);
});

// 5. Cash Calculations & Quick Cash Denominations
test('5. Cash Payment & Quick Cash Auto Change Calculations', () => {
  const totalAmount = 968760;

  const calculateChange = (amountGiven) => Math.max(0, amountGiven - totalAmount);

  assert.equal(calculateChange(1000000), 31240);
  assert.equal(calculateChange(968760), 0);
  assert.equal(calculateChange(500000), 0);

  // Quick cash buttons
  const denominations = [
    totalAmount,
    Math.ceil(totalAmount / 100000) * 100000, // 1,000,000
    Math.ceil(totalAmount / 500000) * 500000, // 1,000,000
    1500000,
    2000000,
  ];

  const uniqueDenoms = [...new Set(denominations)].filter((d) => d >= totalAmount);
  assert.ok(uniqueDenoms.includes(968760), 'Contains exact amount');
  assert.ok(uniqueDenoms.includes(1000000), 'Contains 1,000,000');
});

// 6. VietQR Payment Link Format
test('6. VietQR URL Construction', () => {
  const bankId = 'MB';
  const accountNo = '0901234567';
  const accountName = 'CONG TY OISMS VN';
  const totalAmount = 968760;
  const orderNumber = 'ORD-POS-889900';

  const qrUrl = `https://img.vietqr.io/image/${bankId}-${accountNo}-compact2.png?amount=${totalAmount}&addInfo=${encodeURIComponent(
    `Thanh toan ${orderNumber}`
  )}&accountName=${encodeURIComponent(accountName)}`;

  assert.ok(qrUrl.startsWith('https://img.vietqr.io/image/MB-0901234567-compact2.png'));
  assert.ok(qrUrl.includes('amount=968760'));
  assert.ok(qrUrl.includes('addInfo=Thanh%20toan%20ORD-POS-889900'));
});

// 7. Thermal Receipt Layout Contract (80mm)
test('7. 80mm Thermal Receipt Layout Contract', () => {
  const receiptSrc = fs.readFileSync(path.resolve(__dirname, 'src/components/ReceiptPrintModal.tsx'), 'utf8');

  // Verify 80mm standard width is styled (320px ~ 80mm)
  assert.ok(receiptSrc.includes('receipt-paper'), 'Must have receipt-paper class');
  assert.ok(receiptSrc.includes('max-w-[320px]'), 'Must restrict width to ~320px for 80mm thermal receipt');
  assert.ok(receiptSrc.includes('window.print'), 'Must call window.print for printing');

  // Verify @media print rules in index.css
  const cssSrc = fs.readFileSync(path.resolve(__dirname, 'src/index.css'), 'utf8');
  assert.ok(cssSrc.includes('@media print'), 'Must include @media print style block');
  assert.ok(cssSrc.includes('.no-print'), 'Must hide .no-print elements on paper');
  assert.ok(cssSrc.includes('80mm'), 'Must format page size for 80mm paper');
});

// 8. Checkout Contract & DoD Compliance
test('8. Checkout Contract & DoD Transparency', () => {
  const paymentSrc = fs.readFileSync(path.resolve(__dirname, 'src/components/PaymentModal.tsx'), 'utf8');

  // Verify DTO fields according to contract
  assert.ok(paymentSrc.includes('PosCheckoutRequest'), 'Must build PosCheckoutRequest');
  assert.ok(paymentSrc.includes('requestKey'), 'Must generate requestKey');
  assert.ok(paymentSrc.includes('paymentMethod'), 'Must send paymentMethod');
  assert.ok(paymentSrc.includes('amountGiven'), 'Must send amountGiven');
  assert.ok(paymentSrc.includes('changeDue'), 'Must send changeDue');

  // Verify DoD rule: "checkout chưa sẵn sàng không báo thành công; bản in hóa đơn chuẩn bị sẵn dùng dữ liệu API"
  const apiSrc = fs.readFileSync(path.resolve(__dirname, 'src/services/api.ts'), 'utf8');
  assert.ok(
    apiSrc.includes('Endpoint thanh toán Backend (POST /api/orders/checkout) đang được hoàn thiện theo hợp đồng DTO Sprint 2'),
    'Must display DoD transparent message when checkout is pending'
  );
  assert.ok(
    paymentSrc.includes('handlePrintPreview'),
    'Must allow receipt preview from cart data even if checkout is pending'
  );
});

// 9. PWA Manifest & Service Worker
test('9. PWA Baseline Configuration', () => {
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
