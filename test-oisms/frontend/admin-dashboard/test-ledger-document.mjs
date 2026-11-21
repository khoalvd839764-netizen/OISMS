import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import vm from 'node:vm';

const app = new URL('../../../src/frontend/admin-dashboard/', import.meta.url);
const appRequire = createRequire(new URL('package.json', app));
const ts = appRequire('typescript');

function load(relativePath, require = appRequire) {
  const source = readFileSync(new URL(relativePath, app), 'utf8');
  const js = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const module = { exports: {} };
  vm.runInNewContext(js, { module, exports: module.exports, require });
  return module.exports;
}

const { resolveLedgerDocument } = load('src/utils/resolveLedgerDocument.ts');

test('GoodsReceipt IN -> phiếu nhập kho 01-VT', () => {
  const res = resolveLedgerDocument({
    direction: 'IN',
    documentType: 'GoodsReceipt',
    documentId: 'gr-1',
    documentNumber: 'GR-20261121-0001',
  });
  assert.equal(res.supported, true);
  assert.equal(res.template, 'goods-receipt');
  assert.equal(res.targetId, 'gr-1');
});

test('Order OUT có goodsIssueId -> phiếu xuất kho 02-VT', () => {
  const res = resolveLedgerDocument({
    direction: 'OUT',
    documentType: 'Order',
    documentId: 'ord-1',
    goodsIssueId: 'gi-1',
    salesInvoiceId: 'inv-1',
    documentNumber: 'POS-001',
  });
  assert.equal(res.supported, true);
  assert.equal(res.template, 'goods-issue');
  assert.equal(res.targetId, 'gi-1');
});

test('Order OUT thiếu goodsIssueId -> fallback hóa đơn/đơn hàng kèm ghi chú Q6', () => {
  const withInvoice = resolveLedgerDocument({
    direction: 'OUT',
    documentType: 'Order',
    documentId: 'ord-legacy',
    salesInvoiceId: 'inv-legacy',
    documentNumber: 'POS-OLD-1',
  });
  assert.equal(withInvoice.supported, true);
  assert.equal(withInvoice.template, 'legacy-order');
  assert.equal(withInvoice.targetId, 'inv-legacy');
  assert.equal(withInvoice.fallbackOrderId, 'ord-legacy');
  assert.equal(withInvoice.note, 'Chứng từ trước khi có phiếu xuất kho');

  const orderOnly = resolveLedgerDocument({
    direction: 'OUT',
    documentType: 'Order',
    documentId: 'ord-legacy-2',
    documentNumber: 'ORD-OLD-2',
  });
  assert.equal(orderOnly.supported, true);
  assert.equal(orderOnly.template, 'legacy-order');
  assert.equal(orderOnly.targetId, 'ord-legacy-2');
});

test('StockTransfer OUT / IN -> xuất kho vận chuyển nội bộ / nhập kho nhận chuyển', () => {
  const outRes = resolveLedgerDocument({
    direction: 'OUT',
    documentType: 'StockTransfer',
    documentId: 'trf-1',
    documentNumber: 'TRF-001',
  });
  assert.equal(outRes.supported, true);
  assert.equal(outRes.template, 'stock-transfer-out');
  assert.equal(outRes.targetId, 'trf-1');

  const inRes = resolveLedgerDocument({
    direction: 'IN',
    documentType: 'StockTransfer',
    documentId: 'trf-1',
    documentNumber: 'TRF-001',
  });
  assert.equal(inRes.supported, true);
  assert.equal(inRes.template, 'stock-transfer-in');
  assert.equal(inRes.targetId, 'trf-1');
});

test('StockCount IN / OUT -> biên bản kiểm kê 05-VT kèm highlightSkuId', () => {
  for (const direction of ['IN', 'OUT']) {
    const res = resolveLedgerDocument({
      direction,
      documentType: 'StockCount',
      documentId: 'cnt-1',
      documentNumber: 'CNT-001',
      skuId: 'sku-99',
    });
    assert.equal(res.supported, true);
    assert.equal(res.template, 'stock-count');
    assert.equal(res.targetId, 'cnt-1');
    assert.equal(res.highlightSkuId, 'sku-99');
  }
});

test('SalesReturn IN -> phiếu nhập kho hàng trả lại', () => {
  const res = resolveLedgerDocument({
    direction: 'IN',
    documentType: 'SalesReturn',
    documentId: 'rt-1',
    documentNumber: 'RT-001',
  });
  assert.equal(res.supported, true);
  assert.equal(res.template, 'sales-return');
  assert.equal(res.targetId, 'rt-1');
});

test('InventoryAdjustment IN / OUT -> phiếu nhập/xuất điều chỉnh theo chiều', () => {
  for (const direction of ['IN', 'OUT']) {
    const res = resolveLedgerDocument({
      direction,
      documentType: 'InventoryAdjustment',
      documentId: 'adj-1',
      documentNumber: 'ADJ-001',
    });
    assert.equal(res.supported, true);
    assert.equal(res.template, 'inventory-adjustment');
    assert.equal(res.targetId, 'adj-1');
    assert.equal(res.direction, direction);
  }
});

test('Unknown hoặc thiếu documentId -> vô hiệu nút với tooltip chuẩn', () => {
  for (const entry of [
    { direction: 'IN', documentType: 'Unknown' },
    { direction: 'OUT', documentType: '' },
    { direction: 'IN', documentType: 'GoodsReceipt', documentId: '' },
    null,
  ]) {
    const res = resolveLedgerDocument(entry);
    assert.equal(res.supported, false);
    assert.equal(res.template, 'unknown');
    assert.equal(res.targetId, null);
    assert.equal(res.disabledReason, 'Không xác định được chứng từ nguồn');
  }
});
