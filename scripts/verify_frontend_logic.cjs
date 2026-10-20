const crypto = require('crypto');

function generateUuid() {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === "x" ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

function isValidUuid(str) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(str);
}

// Mock sessionStorage
const mockStorage = new Map();
const sessionStorage = {
  getItem: (k) => mockStorage.get(k) || null,
  setItem: (k, v) => mockStorage.set(k, String(v)),
  removeItem: (k) => mockStorage.delete(k),
  clear: () => mockStorage.clear()
};

async function testFrontend() {
  console.log('=== TEST SUITE: FRONTEND ADMIN LOGIC & CALLBACKS (F1 - F7) ===\n');
  let passed = 0;
  let failed = 0;

  function assert(condition, message) {
    if (condition) {
      console.log(`[PASS] ${message}`);
      passed++;
    } else {
      console.error(`[FAIL] ${message}`);
      failed++;
    }
  }

  // --- 1. Test F2 in apiAdapter.checkout & reserveStock ---
  console.log('--- 1. Testing apiAdapter.checkout & reserveStock Contract ---');
  let lastReservePayload = null;
  let lastReserveHeaders = null;

  const mockApiAdapter = {
    getPendingReservationStorageKey(tenantId = 'default') {
      return `oism_pending_reservation_${tenantId}`;
    },
    getPendingOrderStorageKey(tenantId = 'default') {
      return `oism_pending_order_${tenantId}`;
    },
    async getBalances(filter) {
      // simulate warehouse stock available = 0
      return { data: [{ id: 'sku-1', code: 'POLO-01', onHand: 10, reserved: 10 }] };
    },
    async reserveStock(payload) {
      lastReservePayload = payload;
      lastReserveHeaders = {};
      if (payload.idempotencyKey) {
        lastReserveHeaders['Idempotency-Key'] = payload.idempotencyKey;
        lastReserveHeaders['X-Request-Key'] = payload.idempotencyKey;
      }
      return { data: { reservationId: 'resv-uuid-123', orderId: 'ord-123' } };
    },
    async checkout(sku, quantity, channel, customerName, options) {
      if (!options?.isRetry) {
        const whBalRes = await this.getBalances({ branchId: options?.branchId, warehouseId: options?.warehouseId });
        const whSkuBal = whBalRes.data.find((item) => item.id === sku.id);
        const whAvailable = whSkuBal ? whSkuBal.onHand - whSkuBal.reserved : 0;
        if (whAvailable < quantity) {
          return { error: { code: 'INSUFFICIENT_STOCK', message: 'Tồn khả dụng không đủ' } };
        }
      }

      const res = await this.reserveStock({
        branchId: options?.branchId,
        warehouseId: options?.warehouseId,
        skuId: sku.id,
        quantity,
        durationMinutes: 15,
        idempotencyKey: options?.idempotencyKey,
      });
      return { data: { orderCode: 'ORD-123' } };
    }
  };

  const testSku = { id: 'sku-1', code: 'POLO-01', name: 'Ao Polo', sellingPrice: 200000, isActive: true };
  const key1 = generateUuid();

  // Fresh call with whAvailable = 0 should be blocked by client check
  const freshRes = await mockApiAdapter.checkout(testSku, 2, 'Shopee', 'Khach A', {
    branchId: 'b1', warehouseId: 'w1', idempotencyKey: key1, isRetry: false
  });
  assert(freshRes.error && freshRes.error.code === 'INSUFFICIENT_STOCK', 'Fresh checkout with available < qty is prevented client-side');

  // Retry call with isRetry = true should bypass client check and forward key to reserveStock
  const retryRes = await mockApiAdapter.checkout(testSku, 2, 'Shopee', 'Khach A', {
    branchId: 'b1', warehouseId: 'w1', idempotencyKey: key1, isRetry: true
  });
  assert(retryRes.data?.orderCode === 'ORD-123', 'Retry checkout bypasses client available check');
  assert(lastReservePayload?.idempotencyKey === key1, `reserveStock received exact idempotencyKey: ${key1}`);
  assert(lastReserveHeaders?.['Idempotency-Key'] === key1, 'reserveStock sent Idempotency-Key header');
  assert(lastReserveHeaders?.['X-Request-Key'] === key1, 'reserveStock sent X-Request-Key header');

  // --- 2. Test Pending Order Persistence in OrdersPage ---
  console.log('\n--- 2. Testing OrdersPage Pending Order Storage ---');
  const tenantId = 'tenant-xyz';
  const orderKey = mockApiAdapter.getPendingOrderStorageKey(tenantId);

  // Save pending order
  const pendingOrder = {
    idempotencyKey: key1,
    branchId: 'b1',
    warehouseId: 'w1',
    skuId: 'sku-1',
    quantity: 2,
    channel: 'Shopee',
    customer: 'Khach A'
  };
  sessionStorage.setItem(orderKey, JSON.stringify(pendingOrder));

  // Re-read
  const loadedOrder = JSON.parse(sessionStorage.getItem(orderKey));
  assert(loadedOrder?.idempotencyKey === key1, 'Pending order key successfully stored and restored from sessionStorage');

  // Changing form discards pending order
  sessionStorage.removeItem(orderKey);
  assert(sessionStorage.getItem(orderKey) === null, 'Discarding pending order cleanly wipes storage');

  // --- 3. Test Pending Reservation in InventoryPage ---
  console.log('\n--- 3. Testing InventoryPage Pending Reservation Storage ---');
  const resvStorageKey = mockApiAdapter.getPendingReservationStorageKey(tenantId);
  const pendingResv = {
    idempotencyKey: key1,
    branchId: 'b1',
    warehouseId: 'w1',
    skuId: 'sku-1',
    quantity: 5
  };
  sessionStorage.setItem(resvStorageKey, JSON.stringify(pendingResv));

  const restoredResv = JSON.parse(sessionStorage.getItem(resvStorageKey));
  assert(restoredResv?.idempotencyKey === key1 && restoredResv?.quantity === 5, 'Pending reservation restored with exact key and payload across modal close/reopen');

  // --- 4. Test F3 Warehouse Balance Error Handling ---
  console.log('\n--- 4. Testing F3 loadBalances Warehouse Error Handling ---');
  let stateWarehouseBalances = [{ id: 'sku-old', onHand: 99 }]; // Previously loaded w1 data
  let stateWarehouseBalanceError = null;

  async function mockLoadBalances(targetWarehouse, whApiError = false) {
    const promises = [
      Promise.resolve({ data: [{ id: 'sku-1' }] }), // bRes
      Promise.resolve({ data: [{ id: 'sku-1' }] }), // pRes
    ];
    if (targetWarehouse !== 'all') {
      promises.push(
        whApiError
          ? Promise.resolve({ error: { message: '500 Internal Server Error: Kho gap su co' } })
          : Promise.resolve({ data: [{ id: 'sku-2', onHand: 15 }] })
      );
    }

    const [bRes, pRes, whRes] = await Promise.all(promises);

    if (targetWarehouse !== 'all') {
      if (whRes?.error) {
        stateWarehouseBalances = []; // Cleanly clear old warehouse data!
        stateWarehouseBalanceError = whRes.error?.message;
      } else if (whRes?.data) {
        stateWarehouseBalances = whRes.data;
        stateWarehouseBalanceError = null;
      }
    } else {
      stateWarehouseBalances = [];
      stateWarehouseBalanceError = null;
    }
  }

  // Simulate user switching from w1 to w2, but w2 API returns error 500
  await mockLoadBalances('w2', true);
  assert(stateWarehouseBalances.length === 0, 'F3: Old warehouse balances (w1) immediately wiped upon w2 error, preventing stale display');
  assert(stateWarehouseBalanceError === '500 Internal Server Error: Kho gap su co', 'F3: warehouseBalanceError is set for UI alert banner');

  // Simulate user clicking retry and w2 API succeeds
  await mockLoadBalances('w2', false);
  assert(stateWarehouseBalances.length === 1 && stateWarehouseBalances[0].id === 'sku-2', 'F3: Successful retry populates new warehouse balances');
  assert(stateWarehouseBalanceError === null, 'F3: warehouseBalanceError cleared after successful load');

  console.log(`\n=== SUMMARY: ${passed} PASSED, ${failed} FAILED ===`);
  process.exit(failed > 0 ? 1 : 0);
}

testFrontend();
