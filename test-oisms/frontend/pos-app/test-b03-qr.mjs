import test from 'node:test';
import assert from 'node:assert/strict';
import runtime from './test-support/pos-runtime.cjs';
const { world } = runtime;
const bankA = { bankBin: '970415', accountNumber: '123456789', accountName: 'FIXTURE A', bankName: 'Test bank' };
const bankB = { ...bankA, accountNumber: '987654321', accountName: 'FIXTURE B' };
const ACC_A = '0000000a-aaaa-4bbb-8ccc-000000000001';
const ACC_B = '0000000b-aaaa-4bbb-8ccc-000000000001';

test('F06: không có tài khoản trên server thì QR khóa, không dùng tài khoản mẫu', async () => {
  const w = world(); await w.selectQR();
  assert.equal(w.image(), null); assert.equal(w.submitButton().props.disabled, true);
  assert.equal(w.accountRequests.length, 1);
});

test('CS-05/R3: tải tài khoản bằng gốc VITE_API_BASE_URL và token của phiên', async () => {
  const w = world(); w.seedBank(bankA, ACC_A); await w.selectQR();
  assert.equal(w.accountRequests[0].url, 'https://api.fixture.test/api/payment-accounts/active');
  assert.equal(w.accountRequests[0].headers.Authorization, 'Bearer fixture-token');
  assert.ok(w.image()?.includes('970415-123456789'));
});

test('CS-05/R9: cấu hình cũ trong localStorage hay window không còn được dùng', async () => {
  const w = world();
  const scoped = { version: 1, tenantId: 'tenant-a', branchId: 'branch-a', bankConfig: bankA };
  w.local.set('pos_bank_config_v1:tenant-a:branch-a', JSON.stringify(scoped));
  w.local.set('pos_bank_config', JSON.stringify(bankA));
  w.local.set('pos_payment_accounts_v2:tenant-a', JSON.stringify({ version: 2, tenantId: 'tenant-a', accounts: [{ id: ACC_A, ...bankA, bankShortName: 'TB', isDefault: true }] }));
  w.globals.window.__OISMS_POS_PAYMENT_ACCOUNTS__ = [{ version: 2, tenantId: 'tenant-a', accounts: [{ id: ACC_A, ...bankA, bankShortName: 'TB', isDefault: true }] }];
  await w.selectQR();
  assert.equal(w.image(), null); assert.equal(w.submitButton().props.disabled, true);
});

test('CS-05: server lỗi thì báo lỗi, có nút tải lại; tải lại thành công thì hiện QR', async () => {
  const w = world(); w.seedBank(bankA, ACC_A);
  let fail = true;
  w.server.accountsResponse = async () => fail ? { ok: false, status: 503, json: async () => ({}) } : { ok: true, status: 200, json: async () => w.server.accounts };
  await w.selectQR();
  assert.equal(w.image(), null);
  const reload = w.reloadAccountsButton(); assert.ok(reload);
  fail = false; reload.props.onClick(); await w.settle();
  assert.equal(w.accountRequests.length, 2);
  assert.ok(w.image());
});

test('CS-05: tài khoản sai định dạng từ server bị bỏ qua', async () => {
  const w = world();
  w.setServerAccounts([{ id: 'not-a-guid', ...bankA, isDefault: true }, { id: ACC_B, ...bankB, bankBin: '12', isDefault: false }]);
  await w.selectQR();
  assert.equal(w.image(), null);
});

test('F07: QR giữ nguyên khi đóng/mở và reload', async () => {
  const w = world(); w.seedBank(bankA, ACC_A); await w.selectQR(); const shown = w.image(); assert.ok(shown);
  await w.closeOpen(); assert.equal(w.image(), shown);
  const reload = world({ local: w.local, session: w.session, server: w.server }); await reload.selectQR(); assert.equal(reload.image(), shown);
});

test('F07: đổi giỏ tạo intent mới; đóng/mở thì không', async () => {
  const w = world(); w.seedBank(bankA, ACC_A); await w.selectQR(); const first = w.image();
  await w.closeOpen(); assert.equal(w.image(), first);
  w.props.items = [{ ...w.props.items[0], quantity: 2 }]; await w.settle(); assert.notEqual(w.image(), first);
});

test('Q11: chọn tài khoản khác tạo mã mới; reload giữ đúng tài khoản đã chọn', async () => {
  const w = world(); w.seedBank(bankA, ACC_A, true); w.seedBank(bankB, ACC_B, false); await w.selectQR();
  const first = w.image(); assert.ok(first.includes('123456789'));
  w.accountSelect().props.onChange({ target: { value: ACC_B } }); await w.settle();
  const second = w.image(); assert.ok(second.includes('987654321')); assert.notEqual(second, first);
  const reload = world({ local: w.local, session: w.session, server: w.server }); await reload.selectQR();
  assert.equal(reload.image(), second);
});

test('CS-05: checkout gửi paymentAccountId của tài khoản đã sinh QR', async () => {
  const w = world(); w.seedBank(bankA, ACC_A, true); w.seedBank(bankB, ACC_B, false); await w.selectQR();
  w.accountSelect().props.onChange({ target: { value: ACC_B } }); await w.settle();
  await w.submit();
  assert.equal(w.requests[0].payload.paymentAccountId, ACC_B);
  assert.equal(w.requests[0].payload.paymentMethod, 'BANK_TRANSFER_QR');
  assert.match(w.requests[0].payload.transferReference, /^OISMS POS [0-9A-F]{8}$/);
});

test('F09: snapshot của lần gửi chưa rõ giữ nguyên người nhận dù server đổi tài khoản, đóng/mở, reload', async () => {
  const w = world(); w.seedBank(bankA, ACC_A); await w.selectQR(); const shown = w.image(); await w.submit();
  const original = w.api.getPendingCheckout(w.user, 'branch-a'); assert.ok(original.qrSnapshot);
  w.setServerAccounts([]); w.seedBank(bankB, ACC_B, true); await w.settle(); assert.equal(w.image(), shown);
  await w.closeOpen(); assert.equal(w.image(), shown);
  const reload = world({ local: w.local, session: w.session, server: w.server }); await reload.settle(); assert.equal(reload.image(), shown);
  await reload.submit(); assert.deepEqual(reload.requests[0].payload, w.requests[0].payload);
});

test('F08/F09: người dùng khác không gửi lại được pending QR', async () => {
  const w = world(); w.seedBank(bankA, ACC_A); await w.selectQR(); await w.submit();
  const pendingA = w.api.getPendingCheckout(w.user, 'branch-a');
  const userB = { ...w.user, id: 'user-b', tenantId: 'tenant-b', email: 'b@fixture.test' };
  // Tenant B chưa có tài khoản: không thấy QR hay pending của A.
  w.setServerAccounts([]); w.props.currentUser = userB; await w.settle();
  assert.equal(w.api.getPendingCheckout(userB, 'branch-a'), null);
  assert.equal(w.image(), null); assert.equal(w.submitButton().props.disabled, true);
  // Tenant B có tài khoản riêng: sinh QR với khóa mới, không dùng lại khóa của A.
  w.seedBank(bankB, ACC_B, true); await w.closeOpen();
  assert.ok(w.image()); assert.ok(!w.image().includes(pendingA.requestKey.replace(/-/g, '').slice(0, 8).toUpperCase()));
  await w.submit();
  assert.notEqual(w.requests.at(-1).payload.requestKey, pendingA.requestKey);
});

test('F09: pending QR cũ không có snapshot người nhận chỉ gửi lại yêu cầu gốc, không sinh QR mới', async () => {
  const w = world(); w.seedBank(bankA, ACC_A); await w.selectQR(); await w.submit();
  const pending = w.api.getPendingCheckout(w.user, 'branch-a'); delete pending.qrSnapshot; w.api.savePendingCheckout(pending);
  const reload = world({ local: w.local, session: w.session, server: w.server }); await reload.settle();
  assert.equal(reload.image(), null); assert.equal(reload.submitButton().props.disabled, false);
  await reload.submit(); assert.equal(reload.requests[0].payload.requestKey, pending.requestKey);
});
