import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

// Source và typescript lấy từ src/frontend/marketplace-simulator.
const APP_URL = new URL('../../../src/frontend/marketplace-simulator/', import.meta.url);
const require = createRequire(new URL('package.json', APP_URL));
const ts = require('typescript');
const dir = fileURLToPath(APP_URL);

function loadModule(relativePath) {
  const source = fs.readFileSync(path.resolve(dir, relativePath), 'utf8');
  const js = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const mod = { exports: {} };
  vm.runInNewContext(js, { module: mod, exports: mod.exports, BigInt });
  return mod.exports;
}

const m = loadModule('src/webhookPayload.ts');
// Object tạo trong vm context khác realm, so qua JSON để bỏ khác biệt prototype.
const plain = (value) => JSON.parse(JSON.stringify(value));

const order = {
  channel: 'Shopee',
  externalShopId: '  SHOP_1 ',
  customerName: 'Khách A',
  customerPhone: '',
  shippingAddress: ' ',
  items: [{ skuCode: ' SKU-1 ', quantity: '2', unitPrice: '75000' }],
};

test('tổng tiền làm tròn AwayFromZero như backend', () => {
  assert.equal(m.calculateTotal([{ skuCode: 'A', quantity: '1.005', unitPrice: '1' }]), 1.01);
  assert.equal(m.calculateTotal([{ skuCode: 'A', quantity: '0.333', unitPrice: '0.15' }]), 0.05);
  assert.equal(m.calculateTotal([{ skuCode: 'A', quantity: '3', unitPrice: '0.1' }, { skuCode: 'B', quantity: '1', unitPrice: '0.2' }]), 0.5);
  assert.equal(m.calculateTotal([{ skuCode: 'A', quantity: '2', unitPrice: '75000' }]), 150000);
});

test('payload cắt khoảng trắng, bỏ trường trống và có orderTimestamp', () => {
  const payload = m.buildPayload(order, 'SPE-1', new Date('2026-11-06T03:00:00Z'));
  assert.equal(payload.externalShopId, 'SHOP_1');
  assert.equal(payload.items[0].skuCode, 'SKU-1');
  assert.equal(payload.items[0].quantity, 2);
  assert.equal(payload.customerPhone, undefined);
  assert.equal(payload.shippingAddress, undefined);
  assert.equal(payload.totalAmount, 150000);
  assert.equal(payload.orderTimestamp, '2026-11-06T03:00:00.000Z');
});

test('đổi nội dung giữ mã đơn, tăng số lượng và tính lại tổng', () => {
  const payload = m.buildPayload({ ...order, items: [{ skuCode: 'A', quantity: '1.125', unitPrice: '10' }] }, 'SPE-1', new Date());
  const changed = m.mutatePayload(payload);
  assert.equal(changed.externalOrderId, 'SPE-1');
  assert.equal(changed.items[0].quantity, 2.125);
  assert.equal(changed.totalAmount, 21.25);
});

test('kiểm tra nhập liệu', () => {
  assert.deepEqual(plain(m.validateOrder(order)), []);
  const errors = m.validateOrder({
    ...order,
    externalShopId: '',
    items: [
      { skuCode: '', quantity: '0', unitPrice: '1.234' },
      { skuCode: 'X', quantity: '1', unitPrice: '10' },
      { skuCode: 'x', quantity: '1', unitPrice: '12' },
    ],
  });
  assert.equal(errors.length, 5);
});

test('mã đơn theo tiền tố sàn', () => {
  const id = m.newExternalOrderId('TikTok', () => 'abcd-ef12-3456-7890');
  assert.equal(id, 'TTS-ABCDEF123456');
});

test('đọc mọi dạng body', () => {
  assert.deepEqual(plain(m.parseResponse(201, { orderNumber: 'ORD-1', status: 'Reserved' })), { ok: true, summary: 'ORD-1 · Reserved' });
  assert.match(m.parseResponse(200, { orderNumber: 'ORD-1', status: 'Reserved' }).summary, /gửi lặp/);
  assert.deepEqual(plain(m.parseResponse(409, { errorCode: 'WEBHOOK_PAYLOAD_MISMATCH', message: 'Khác nội dung' })),
    { ok: false, errorCode: 'WEBHOOK_PAYLOAD_MISMATCH', summary: 'Khác nội dung' });
  assert.equal(m.parseResponse(401, { message: 'Webhook Secret không hợp lệ.' }).summary, 'Webhook Secret không hợp lệ.');
  assert.equal(m.parseResponse(400, { errorCode: 'X', title: 'T', detail: 'D' }).summary, 'D');
  assert.equal(m.parseResponse(502, null).summary, 'Không kết nối được backend (HTTP 502).');
  assert.equal(m.parseResponse(500, null).summary, 'HTTP 500');
});
