import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import vm from 'node:vm';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Import Playwright từ runtime khả dụng
const PLAYWRIGHT_PATH = 'C:/Users/Jacki Khoa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright/index.js';
const playwrightModule = await import(pathToFileURL(PLAYWRIGHT_PATH).href);
const playwright = playwrightModule.default || playwrightModule;
const chromium = playwright.chromium;

// Import TypeScript và React từ pos-app node_modules
const ts = (await import('./node_modules/typescript/lib/typescript.js')).default;
const React = (await import('./node_modules/react/index.js')).default;
const ReactDOMServer = (await import('./node_modules/react-dom/server.node.js')).default;

const EVIDENCE_DIR = path.resolve(__dirname, '../../../docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-01-receipt-print/evidence/v1.1');
if (!fs.existsSync(EVIDENCE_DIR)) {
  fs.mkdirSync(EVIDENCE_DIR, { recursive: true });
}

// 1. Compile và load ReceiptPrintModal từ production source
const modalCode = fs.readFileSync(path.resolve(__dirname, 'src/components/ReceiptPrintModal.tsx'), 'utf8');
const js = ts.transpileModule(modalCode, {
  compilerOptions: {
    module: ts.ModuleKind.CommonJS,
    target: ts.ScriptTarget.ES2022,
    jsx: ts.JsxEmit.ReactJSX,
    esModuleInterop: true,
  },
}).outputText;

const mockLucide = {
  Printer: () => React.createElement('span', null, '[PrinterIcon]'),
  X: () => React.createElement('span', null, '[XIcon]'),
  FileText: () => React.createElement('span', null, '[FileTextIcon]'),
  CheckCircle2: () => React.createElement('span', null, '[CheckCircle2Icon]'),
};

const jsxRuntime = (await import('./node_modules/react/jsx-runtime.js')).default;

const mod = { exports: {} };
vm.runInNewContext(js, {
  module: mod,
  exports: mod.exports,
  require: (name) => {
    if (name === 'react') return React;
    if (name === 'react/jsx-runtime') return jsxRuntime;
    if (name === 'lucide-react') return mockLucide;
    throw new Error('Unknown import in VM: ' + name);
  },
  console,
  localStorage: { getItem: () => null, setItem: () => {} },
  window: { print: () => {} },
});

const ReceiptPrintModal = mod.exports.ReceiptPrintModal;
if (!ReceiptPrintModal) {
  throw new Error('Không thể tải ReceiptPrintModal từ file production.');
}

// Đọc toàn bộ file index.css thật của POS App
const indexCss = fs.readFileSync(path.resolve(__dirname, 'src/index.css'), 'utf8');

// Utilities CSS tối thiểu cho layout Tailwind in ấn khi render SSR không qua Vite client
const tailwindPrintUtilities = `
  .flex { display: flex; }
  .items-center { align-items: center; }
  .items-baseline { align-items: baseline; }
  .justify-between { justify-content: space-between; }
  .text-center { text-align: center; }
  .text-right { text-align: right; }
  .text-left { text-align: left; }
  .font-bold { font-weight: 700; }
  .font-semibold { font-weight: 600; }
  .font-mono { font-family: monospace; }
  .w-full { width: 100%; }
  .w-8 { width: 2rem; }
  .py-1 { padding-top: 0.25rem; padding-bottom: 0.25rem; }
  .pb-1 { padding-bottom: 0.25rem; }
  .pt-1\\.5 { padding-top: 0.375rem; }
  .mb-1 { margin-bottom: 0.25rem; }
  .mb-2 { margin-bottom: 0.5rem; }
  .mb-3 { margin-bottom: 0.75rem; }
  .mt-0\\.5 { margin-top: 0.125rem; }
  .border-b { border-bottom-width: 1px; border-bottom-style: solid; }
  .border-t { border-top-width: 1px; border-top-style: solid; }
  .border-dashed { border-style: dashed; }
  .border-slate-200 { border-color: #e2e8f0; }
  .border-slate-300 { border-color: #cbd5e1; }
  .border-slate-400 { border-color: #94a3b8; }
  .border-slate-800 { border-color: #1e293b; }
  .whitespace-nowrap { white-space: nowrap; }
  .break-words { overflow-wrap: break-word; }
  .leading-tight { line-height: 1.25; }
  .space-y-1 > * + * { margin-top: 0.25rem; }
  .space-y-2 > * + * { margin-top: 0.5rem; }
  .divide-y > * + * { border-top-width: 1px; }
  .divide-slate-100 > * + * { border-color: #f1f5f9; }
`;

// Định nghĩa dữ liệu mẫu chuẩn Backend Contract
const officialReceipt = {
  receiptNumber: 'HD-20261027-0089',
  orderNumber: 'ORD-20261027-0089',
  createdAt: '27/10/2026, 21:15:00',
  tenantName: 'CỬA HÀNG OISMS THẬT',
  branchName: 'Chi nhánh Quận 1 - OISMS Store',
  branchAddress: '123 Lê Lợi, Phường Bến Thành, Quận 1, TP.HCM',
  branchPhone: '028 3822 9999',
  cashierName: 'Nguyễn Văn Thu Ngân (TN01)',
  customerName: 'Trần Thị Khách Hàng',
  customerPhone: '0912 345 678',
  items: [
    { name: 'Áo thun OISMS Premium Cotton - Size L', sku: 'AT-COTTON-L', quantity: 2, unitPrice: 150000, subtotal: 300000 },
    { name: 'Quần Jeans Slimfit Co Giãn - Size 31', sku: 'QJ-SLIM-31', quantity: 1, unitPrice: 350000, subtotal: 350000 },
  ],
  totalAmount: 650000,
  paymentMethod: 'CASH',
  amountGiven: 700000,
  changeDue: 50000,
  isDraft: false,
};

const draftReceipt = {
  receiptNumber: 'DRAFT-982145',
  orderNumber: 'DRAFT-982145',
  createdAt: '27/10/2026, 21:16:30',
  tenantName: 'CỬA HÀNG OISMS THẬT',
  branchName: 'Chi nhánh Quận 1 - OISMS Store',
  branchAddress: '123 Lê Lợi, Phường Bến Thành, Quận 1, TP.HCM',
  branchPhone: '028 3822 9999',
  cashierName: 'Nguyễn Văn Thu Ngân (TN01)',
  customerName: 'Khách lẻ',
  items: [
    { name: 'Cà phê rang xay Robusta Đắk Lắk 500g', sku: 'CP-ROBUSTA-500', quantity: 3, unitPrice: 95000, subtotal: 285000 },
  ],
  totalAmount: 285000,
  paymentMethod: 'CASH',
  amountGiven: 300000,
  changeDue: 15000,
  isDraft: true,
};

function renderFullHtml(receipt, paperWidth) {
  const componentHtml = ReactDOMServer.renderToStaticMarkup(
    React.createElement(ReceiptPrintModal, {
      receipt,
      onClose: () => {},
      defaultPaperWidth: paperWidth,
    })
  );

  return `<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="UTF-8">
  <title>${receipt.isDraft ? 'Phiếu Tạm Tính' : 'Hóa Đơn'} - ${paperWidth}</title>
  <style>
    /* Toàn bộ stylesheet thật của POS */
    ${indexCss}
    ${tailwindPrintUtilities}
  </style>
</head>
<body class="bg-slate-100 font-sans antialiased text-slate-800">
  ${componentHtml}
</body>
</html>`;
}

function parsePdfMediaBox(pdfBuffer) {
  const text = pdfBuffer.toString('latin1');
  const match = text.match(/\/MediaBox\s*\[\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*\]/);
  if (!match) return null;
  const x1 = parseFloat(match[1]);
  const y1 = parseFloat(match[2]);
  const x2 = parseFloat(match[3]);
  const y2 = parseFloat(match[4]);
  const widthPt = x2 - x1;
  const heightPt = y2 - y1;
  const widthMm = (widthPt * 25.4) / 72;
  const heightMm = (heightPt * 25.4) / 72;
  return {
    rawBox: [x1, y1, x2, y2],
    widthPt,
    heightPt,
    widthMm,
    heightMm,
  };
}

console.log('=== KHỞI ĐỘNG CHU KỲ KIỂM CHỨNG B01-F01, F02, F03 (v1.1) ===');
console.log('Evidence dir:', EVIDENCE_DIR);

const EDGE_EXECUTABLE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';

const browser = await chromium.launch({
  executablePath: EDGE_EXECUTABLE_PATH,
  headless: true,
  args: ['--no-sandbox', '--disable-setuid-sandbox'],
});

const testCases = [
  { name: '80mm Official', filename: 'receipt-80mm-official.pdf', receipt: officialReceipt, paperWidth: '80mm', expectedWidthMin: 78, expectedWidthMax: 82 },
  { name: '58mm Official', filename: 'receipt-58mm-official.pdf', receipt: officialReceipt, paperWidth: '58mm', expectedWidthMin: 56, expectedWidthMax: 60 },
  { name: '80mm Draft', filename: 'receipt-80mm-draft.pdf', receipt: draftReceipt, paperWidth: '80mm', expectedWidthMin: 78, expectedWidthMax: 82 },
  { name: '58mm Draft', filename: 'receipt-58mm-draft.pdf', receipt: draftReceipt, paperWidth: '58mm', expectedWidthMin: 56, expectedWidthMax: 60 },
];

const reportResults = [];

for (const tc of testCases) {
  const page = await browser.newPage();
  const html = renderFullHtml(tc.receipt, tc.paperWidth);

  await page.setContent(html, { waitUntil: 'networkidle' });
  await page.emulateMedia({ media: 'print' });

  // Đảm bảo font và render pipeline trong layout engine hoàn tất
  await page.evaluate(async () => {
    if (document.fonts) {
      await document.fonts.ready;
    }
    await new Promise((resolve) => {
      requestAnimationFrame(() => {
        requestAnimationFrame(resolve);
      });
    });
  });

  // Kiểm tra B01-F01: Các ancestor và .receipt-paper trong @media print
  const printLayoutCheck = await page.evaluate(() => {
    const bg = document.querySelector('.no-print-bg');
    const border = document.querySelector('.no-print-border');
    const paper = document.querySelector('.receipt-paper');
    const noPrintElements = Array.from(document.querySelectorAll('.no-print'));

    const getComputedDisplay = (el) => (el ? window.getComputedStyle(el).display : 'null');

    return {
      bgDisplay: getComputedDisplay(bg),
      borderDisplay: getComputedDisplay(border),
      paperDisplay: getComputedDisplay(paper),
      paperRect: paper ? { width: paper.getBoundingClientRect().width, height: paper.getBoundingClientRect().height } : null,
      noPrintHiddenCount: noPrintElements.filter(el => window.getComputedStyle(el).display === 'none').length,
      totalNoPrintCount: noPrintElements.length,
    };
  });

  const pdfPath = path.join(EVIDENCE_DIR, tc.filename);
  await page.pdf({
    path: pdfPath,
    preferCSSPageSize: true,
    printBackground: true,
    displayHeaderFooter: false,
  });

  await page.close();

  const pdfBuffer = fs.readFileSync(pdfPath);
  const mediaBox = parsePdfMediaBox(pdfBuffer);

  const isWidthValid = mediaBox && mediaBox.widthMm >= tc.expectedWidthMin && mediaBox.widthMm <= tc.expectedWidthMax;
  const isPrintVisible = printLayoutCheck.bgDisplay !== 'none' && printLayoutCheck.paperDisplay !== 'none' && printLayoutCheck.paperRect && printLayoutCheck.paperRect.width > 0;
  const hasTextAndFonts = pdfBuffer.length > 5000 && pdfBuffer.includes(Buffer.from('Font'));

  const resultItem = {
    testCase: tc.name,
    filename: tc.filename,
    fileSizeBytes: pdfBuffer.length,
    hasTextAndFonts,
    paperWidth: tc.paperWidth,
    expectedWidthRangeMm: `${tc.expectedWidthMin}mm - ${tc.expectedWidthMax}mm`,
    actualMediaBox: mediaBox,
    isWidthValid,
    printLayoutCheck,
    isPrintVisible,
    status: isWidthValid && isPrintVisible && hasTextAndFonts ? 'PASSED' : 'FAILED',
  };

  reportResults.push(resultItem);

  console.log(`\n[${resultItem.status}] ${tc.name}:`);
  console.log(`  File: ${tc.filename} (${pdfBuffer.length} bytes, hasFonts: ${hasTextAndFonts})`);
  console.log(`  MediaBox: [${mediaBox?.rawBox.join(', ')}] -> Width: ${mediaBox?.widthMm.toFixed(1)}mm (${mediaBox?.widthPt.toFixed(1)}pt), Height: ${mediaBox?.heightMm.toFixed(1)}mm`);
  console.log(`  Print Layout Check:`);
  console.log(`    .no-print-bg display: ${printLayoutCheck.bgDisplay} (must NOT be 'none')`);
  console.log(`    .receipt-paper display: ${printLayoutCheck.paperDisplay}, rect: ${JSON.stringify(printLayoutCheck.paperRect)}`);
  console.log(`    Hidden .no-print elements: ${printLayoutCheck.noPrintHiddenCount}/${printLayoutCheck.totalNoPrintCount}`);
}

await browser.close();

const reportJsonPath = path.join(EVIDENCE_DIR, 'print-verification-report.json');
fs.writeFileSync(reportJsonPath, JSON.stringify(reportResults, null, 2), 'utf8');
console.log(`\nBáo cáo kiểm chứng in đã được lưu tại: ${reportJsonPath}`);

const allPassed = reportResults.every(r => r.status === 'PASSED');
if (!allPassed) {
  console.error('\nLỖI: Một hoặc nhiều ca kiểm chứng in thất bại!');
  process.exit(1);
} else {
  console.log('\nTẤT CẢ 4 CA KIỂM CHỨNG IN ĐÃ ĐẠT CHUẨN (B01-F01 & B01-F03)!');
}
