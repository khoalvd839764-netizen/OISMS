// Kiểm tra POS tích hợp trong POS (admin-dashboard/src/components/pos/PaymentModal.tsx):
// [BF-003] gửi lại sau lỗi chưa rõ phải dùng đúng RequestKey và payload; [CS-05] tài khoản nhận tiền chỉ từ API.
import test from 'node:test';
import assert from 'node:assert/strict';
import runtime from './test-support/pos-runtime.cjs';
const { world } = runtime;
const ACC_A = '0000000a-aaaa-4bbb-8ccc-000000000001';
const accountA = { id: ACC_A, bankBin: '970415', bankName: 'Test bank', bankShortName: 'TB', accountNumber: '123456789', accountName: 'FIXTURE A', isDefault: true };
const ok = (requestKey) => ({ ok: true, status: 200, json: async () => ({ orderNumber: 'ORD-1', receipt: { receiptNumber: 'HD-1', orderNumber: 'ORD-1', createdAt: '2026-11-08T05:00:00Z', paymentMethod: 'Cash', totalAmount: 100000, items: [{ skuCode: 'A', productName: 'A', quantity: 1, unitPrice: 100000, subtotal: 100000 }] }, requestKey }) });

test('BF-003 POS: tiền mặt, mất mạng rồi bấm lại gửi cùng RequestKey và payload', async () => {
  const w = world(); await w.settle();
  await w.submit();
  assert.equal(w.requests.length, 1);
  const first = w.requests[0].payload;
  assert.match(first.requestKey, /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i);
  assert.equal(w.store.getPendingCheckout(w.user, 'branch-a')?.status, 'unknown');
  await w.submit();
  assert.equal(w.requests.length, 2);
  assert.deepEqual(w.requests[1].payload, first);
});

test('BF-003 POS: 5xx là kết quả chưa rõ, reload trang vẫn gửi lại đúng khóa cũ', async () => {
  const w = world(); w.server.checkout = () => ({ ok: false, status: 503, json: async () => ({ detail: 'Gateway timeout' }) });
  await w.settle(); await w.submit();
  const first = w.requests[0].payload;
  const reload = world({ local: w.local, session: w.session, server: { accounts: [], checkout: (p) => ok(p.requestKey) } });
  await reload.settle(); await reload.submit();
  assert.deepEqual(reload.requests[0].payload, first);
  assert.equal(reload.successes.length, 1);
  assert.equal(reload.store.getPendingCheckout(reload.user, 'branch-a'), null);
});

test('BF-003 POS: lỗi chắc chắn 409 xóa pending, lần sau dùng khóa mới', async () => {
  const w = world(); w.server.checkout = () => ({ ok: false, status: 409, json: async () => ({ detail: 'Không đủ tồn', errorCode: 'INSUFFICIENT_STOCK' }) });
  await w.settle(); await w.submit();
  assert.equal(w.store.getPendingCheckout(w.user, 'branch-a'), null);
  await w.submit();
  assert.equal(w.requests.length, 2);
  assert.notEqual(w.requests[1].payload.requestKey, w.requests[0].payload.requestKey);
});

test('BF-003 POS: render nhiều lần không đổi RequestKey của mã QR', async () => {
  const w = world(); w.server.accounts.push(accountA); await w.selectQR();
  const shown = w.image(); assert.ok(shown);
  w.render(); w.render(); await w.settle();
  assert.equal(w.image(), shown);
  await w.closeOpen(); assert.equal(w.image(), shown);
});

test('CS-05 POS: tải tài khoản bằng VITE_API_BASE_URL và token oisms_pos_v2_access; checkout gửi paymentAccountId', async () => {
  const w = world(); w.server.accounts.push(accountA); w.server.checkout = (p) => ok(p.requestKey);
  await w.selectQR();
  assert.equal(w.accountRequests[0].url, 'https://api.fixture.test/api/payment-accounts/active');
  assert.equal(w.accountRequests[0].headers.Authorization, 'Bearer fixture-token');
  await w.submit();
  assert.equal(w.requests[0].payload.paymentAccountId, ACC_A);
  assert.equal(w.requests[0].payload.paymentMethod, 'BANK_TRANSFER_QR');
  assert.equal(w.successes.length, 1);
});

test('CS-05 POS: không có tài khoản trên server hoặc thiếu định danh user thì QR khóa', async () => {
  const w = world(); w.local.set('pos_payment_accounts_v2:tenant-a', JSON.stringify({ version: 2, tenantId: 'tenant-a', accounts: [accountA] }));
  await w.selectQR();
  assert.equal(w.image(), null); assert.equal(w.submitButton().props.disabled, true);
  const anon = world({ user: { fullName: 'Guest', email: '', tenantName: 'Tenant A' } }); anon.server.accounts.push(accountA);
  await anon.selectQR();
  assert.equal(anon.accountRequests.length, 0);
  assert.equal(anon.image(), null); assert.equal(anon.submitButton().props.disabled, true);
});
