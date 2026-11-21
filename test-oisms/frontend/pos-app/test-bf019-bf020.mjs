// Hồi quy POS [BF-019] HTTP 200 sai contract, [BF-020] phản hồi checkout muộn sau khi đổi chi nhánh/giỏ, và [BF-003] retry giữ RequestKey.
import test from 'node:test';
import assert from 'node:assert/strict';
import runtime from './test-support/pos-runtime.cjs';

const { world } = runtime;

const validReceipt = (orderNumber, requestKey) => ({
  orderId: '11111111-1111-4111-8111-111111111111',
  orderNumber,
  receipt: {
    receiptNumber: `HD-${orderNumber}`,
    orderNumber,
    createdAt: '2026-11-21T10:00:00Z',
    paymentMethod: 'Cash',
    totalAmount: 100000,
    items: [{ skuCode: 'A', productName: 'A', quantity: 1, unitPrice: 100000, subtotal: 100000 }],
  },
  requestKey,
});

test('BF-019 POS: HTTP 200 body rỗng / thiếu mã đơn / mã là GUID rỗng -> isUnknown, giữ giỏ và RequestKey', async () => {
  const cases = [
    { name: 'body rỗng (null)', body: null },
    { name: 'thiếu mã đơn', body: { unexpected: true } },
    { name: 'GUID rỗng', body: { orderNumber: '00000000-0000-0000-0000-000000000000' } },
  ];

  for (const tc of cases) {
    const w = world({
      server: {
        accounts: [],
        checkout: () => ({ ok: true, status: 200, json: async () => tc.body }),
      },
    });
    await w.settle();
    await w.submit();

    assert.equal(w.requests.length, 1, tc.name);
    const firstKey = w.requests[0].payload.requestKey;
    assert.equal(w.successes.length, 0, `${tc.name}: không được gọi onSuccessAndPrint/xóa giỏ`);
    const pending = w.store.getPendingCheckout(w.user, 'branch-a');
    assert.ok(pending, `${tc.name}: phải giữ pending checkout`);
    assert.equal(pending.status, 'unknown', `${tc.name}: status phải là unknown`);
    assert.equal(pending.requestKey, firstKey, `${tc.name}: phải giữ nguyên RequestKey`);

    // Lần retry sau khi server trả đúng -> dùng lại đúng RequestKey cũ
    w.server.checkout = (p) => ({ ok: true, status: 200, json: async () => validReceipt('ORD-OK-1', p.requestKey) });
    await w.submit();
    assert.equal(w.requests.length, 2, tc.name);
    assert.equal(w.requests[1].payload.requestKey, firstKey, `${tc.name}: retry phải giữ nguyên RequestKey`);
    assert.equal(w.successes.length, 1, `${tc.name}: retry thành công`);
    assert.equal(w.store.getPendingCheckout(w.user, 'branch-a'), null);
  }
});

test('BF-020 POS: checkout A chưa trả -> đóng modal -> đổi chi nhánh B, thêm hàng -> A trả thành công không xóa giỏ B hay hiện hóa đơn A', async () => {
  let resolveCheckoutA;
  const pendingA = new Promise((resolve) => { resolveCheckoutA = resolve; });

  const w = world({
    server: {
      accounts: [],
      checkout: () => pendingA,
    },
  });
  await w.settle();

  // Bắt đầu checkout ở branch-a (chưa trả về)
  const submitPromise = w.submit();
  await new Promise((r) => setImmediate(r));
  assert.equal(w.requests.length, 1);
  assert.equal(w.requests[0].payload.branchId, 'branch-a');

  // Đóng modal, đổi sang branch-b và thêm hàng mới vào giỏ B
  w.props.isOpen = false;
  w.props.branchId = 'branch-b';
  w.props.branchName = 'Branch B';
  w.props.items = [{ variantId: 'sku-b', sku: 'B', productName: 'Sản phẩm B', retailPrice: 250000, quantity: 2 }];
  w.render();

  // Checkout A trả về muộn thành công
  resolveCheckoutA({
    ok: true,
    status: 200,
    json: async () => validReceipt('ORD-A-LATE', w.requests[0].payload.requestKey),
  });
  await submitPromise;
  await w.settle();

  // Kỳ vọng: hóa đơn A không được hiện/gọi onSuccessAndPrint trong ngữ cảnh B, giỏ B còn nguyên
  assert.equal(w.successes.length, 0, 'BF-020: phản hồi muộn của branch A không được kích hoạt onSuccessAndPrint ở ngữ cảnh branch B');
  assert.equal(w.props.items.length, 1);
  assert.equal(w.props.items[0].variantId, 'sku-b');
});

test('BF-003 POS: retry cùng RequestKey sau kết quả chưa rõ vẫn đúng', async () => {
  const w = world();
  await w.settle();
  await w.submit();
  const key1 = w.requests[0].payload.requestKey;
  assert.equal(w.store.getPendingCheckout(w.user, 'branch-a')?.status, 'unknown');

  w.server.checkout = (p) => ({ ok: true, status: 200, json: async () => validReceipt('ORD-RETRY', p.requestKey) });
  await w.submit();
  assert.equal(w.requests.length, 2);
  assert.equal(w.requests[1].payload.requestKey, key1);
  assert.equal(w.successes.length, 1);
  assert.equal(w.store.getPendingCheckout(w.user, 'branch-a'), null);
});
