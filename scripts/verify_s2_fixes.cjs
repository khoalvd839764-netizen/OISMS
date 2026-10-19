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

const BASE_URL = 'http://localhost:5000';

async function runTests() {
  console.log('=== TEST SUITE: OISMS SPRINT 2 LIVE VERIFICATION ===\n');
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

  // Test 1: UUID Generator
  const u1 = generateUuid();
  const u2 = generateUuid();
  assert(isValidUuid(u1), `generateUuid produces valid RFC4122 UUID (${u1})`);
  assert(u1 !== u2, 'Consecutive generateUuid calls produce unique IDs');

  // Test 2: Register a test tenant and obtain JWT token
  const stamp = Date.now().toString().slice(-6);
  const tenantCode = `T${stamp}`;
  const ownerEmail = `qa_${stamp}@oisms.local`;
  const password = 'Password@123';

  console.log(`\n--- Registering Test Tenant: ${tenantCode} (${ownerEmail}) ---`);
  const regRes = await fetch(`${BASE_URL}/api/auth/register-tenant`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      tenantName: `Cửa hàng QA ${stamp}`,
      tenantCode: tenantCode,
      email: ownerEmail,
      password: password,
      fullName: `Chủ shop QA ${stamp}`
    })
  });
  const regData = await regRes.json();
  assert(regRes.status === 201 && regData.tenantId, `Registered tenant successfully: ID=${regData.tenantId}, Code=${regData.tenantCode}`);
  const tenantId = regData.tenantId;

  // Login
  const loginRes = await fetch(`${BASE_URL}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: ownerEmail, password })
  });
  const loginData = await loginRes.json();
  const token = loginData.accessToken;
  assert(loginRes.status === 200 && token, `Logged in successfully: token acquired`);

  async function api(method, path, body = null, headers = {}) {
    const res = await fetch(`${BASE_URL}${path}`, {
      method,
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`,
        'X-Tenant-Id': tenantId,
        ...headers
      },
      body: body ? JSON.stringify(body) : undefined
    });
    const text = await res.text();
    let data = null;
    try { data = JSON.parse(text); } catch { data = text; }
    return { status: res.status, ok: res.ok, data };
  }

  try {
    // 1. Create Branch
    const branchRes = await api('POST', '/api/branches', { name: `CN Kiem Thu ${stamp}`, code: `BR-${stamp}` });
    const branchId = branchRes.data.id;
    assert(branchId, `Created branch: ${branchId}`);

    // 2. Create Warehouse
    const whRes = await api('POST', '/api/warehouses', { name: `Kho Kiem Thu ${stamp}`, branchId });
    const warehouseId = whRes.data.id;
    assert(warehouseId, `Created warehouse: ${warehouseId}`);

    // 3. Create Supplier
    const supRes = await api('POST', '/api/suppliers', { name: `NCC Kiem Thu ${stamp}` });
    const supplierId = supRes.data.id;
    assert(supplierId, `Created supplier: ${supplierId}`);

    // 4. Create Product & SKU
    const skuCode = `SKU-F2-${stamp}`;
    const prodRes = await api('POST', '/api/products', {
      name: `Ao Thun Test F2 ${stamp}`,
      variants: [{ sku: skuCode, variantName: 'Size L', retailPrice: 150000, wholesalePrice: 120000 }]
    });
    assert(prodRes.status === 201 && prodRes.data.productId, `Created product: ${prodRes.data?.productId}`);
    
    const prodList = await api('GET', '/api/products');
    const skuObj = prodList.data?.[0]?.variants?.find(v => v.sku === skuCode) || prodList.data?.[0]?.variants?.[0];
    const skuId = skuObj?.id;
    assert(skuId, `Found SKU ID: ${skuId}`);

    // 5. Inward stock (Goods Receipt) so we have onHand = 20
    const receiptRes = await api('POST', '/api/goods-receipts', {
      branchId,
      warehouseId,
      supplierId,
      items: [{ skuId, quantity: 20, unitPrice: 90000 }]
    });
    const receiptId = receiptRes.data.id;
    assert(receiptId, `Created goods receipt Draft: ${receiptId}`);

    const confirmRes = await api('POST', `/api/goods-receipts/${receiptId}/confirm`);
    assert(confirmRes.ok, `Confirmed goods receipt #${receiptId}, 20 items inward.`);

    // 6. Check balance before reserve
    const balBefore = await api('GET', `/api/inventory/balances?branchId=${branchId}&warehouseId=${warehouseId}`);
    const itemsBefore = Array.isArray(balBefore.data) ? balBefore.data : (balBefore.data.items || []);
    const skuBalBefore = itemsBefore.find(x => x.id === skuId || x.skuId === skuId);
    assert(skuBalBefore && skuBalBefore.onHand === 20 && skuBalBefore.reserved === 0, `Initial balance onHand=20, reserved=0`);

    // 7. F2: Reserve with Idempotency-Key UUID
    const requestKey = generateUuid();
    const reservePayload = {
      branchId,
      warehouseId,
      skuId,
      quantity: 3,
      durationMinutes: 15,
      requestKey: requestKey
    };
    const reserveHeaders = {
      'Idempotency-Key': requestKey,
      'X-Request-Key': requestKey
    };

    console.log(`Sending Reserve #1 with requestKey=${requestKey}`);
    const res1 = await api('POST', '/api/inventory/reserve', reservePayload, reserveHeaders);
    assert(res1.status === 200 && res1.data.reservationId, `Reserve #1 success: reservationId=${res1.data.reservationId}`);

    // Check balance after #1
    const balMid = await api('GET', `/api/inventory/balances?branchId=${branchId}&warehouseId=${warehouseId}`);
    const itemsMid = Array.isArray(balMid.data) ? balMid.data : (balMid.data.items || []);
    const skuBalMid = itemsMid.find(x => x.id === skuId || x.skuId === skuId);
    assert(skuBalMid.reserved === 3, `After Reserve #1: reserved=3 (expected 3)`);

    // 8. Send Reserve #2 with SAME requestKey and SAME payload (Simulating client retry after network lost)
    console.log(`Sending Reserve #2 (Retry) with same key and payload`);
    const res2 = await api('POST', '/api/inventory/reserve', reservePayload, reserveHeaders);
    assert(res2.status === 200 && res2.data.reservationId === res1.data.reservationId, `Reserve #2 returns identical reservationId (${res2.data.reservationId})`);

    // Check balance after #2 (Must NOT increment reserved again!)
    const balAfter = await api('GET', `/api/inventory/balances?branchId=${branchId}&warehouseId=${warehouseId}`);
    const itemsAfter = Array.isArray(balAfter.data) ? balAfter.data : (balAfter.data.items || []);
    const skuBalAfter = itemsAfter.find(x => x.id === skuId || x.skuId === skuId);
    assert(skuBalAfter.reserved === 3, `After Reserve #2 retry: reserved=3 (NOT 6!) -> F2 IDEMPOTENCY SUCCESS`);

    // 9. Send Reserve #3 with SAME key but DIFFERENT payload (e.g. quantity = 5 instead of 3)
    console.log(`Sending Reserve #3 with SAME key but DIFFERENT payload (quantity=5)`);
    const res3 = await api('POST', '/api/inventory/reserve', { ...reservePayload, quantity: 5 }, reserveHeaders);
    assert(res3.status === 409, `Reserve #3 with modified payload correctly rejected with HTTP 409 Conflict (${res3.status})`);

    // 10. Key contract test: body requestKey alone (without header)
    const bodyOnlyKey = generateUuid();
    console.log(`Sending Reserve with body requestKey only (${bodyOnlyKey})`);
    const resBody1 = await api('POST', '/api/inventory/reserve', { ...reservePayload, requestKey: bodyOnlyKey });
    assert(resBody1.status === 200 && resBody1.data.reservationId, `Body requestKey reserve #1 success: ${resBody1.data.reservationId}`);

    const resBody2 = await api('POST', '/api/inventory/reserve', { ...reservePayload, requestKey: bodyOnlyKey });
    assert(resBody2.status === 200 && resBody2.data.reservationId === resBody1.data.reservationId, `Body requestKey reserve #2 (retry) returned same reservationId`);

    // 11. Concurrent requests with same key: execute 5 concurrent requests simultaneously
    const concurrentKey = generateUuid();
    console.log(`Testing concurrency: sending 5 parallel requests with same key ${concurrentKey}`);
    const concurrentPromises = Array.from({ length: 5 }, () =>
      api('POST', '/api/inventory/reserve', { ...reservePayload, requestKey: concurrentKey }, { 'Idempotency-Key': concurrentKey })
    );
    const concurrentResults = await Promise.all(concurrentPromises);
    const allSuccessful = concurrentResults.every(r => r.status === 200);
    const allSameId = concurrentResults.every(r => r.data.reservationId === concurrentResults[0].data.reservationId);
    assert(allSuccessful && allSameId, `Concurrent 5 requests with same key all returned 200 and identical reservation ID (${concurrentResults[0].data.reservationId})`);

    // Balance check: reserved should have incremented by 3 (from res1) + 3 (from bodyOnlyKey) + 3 (from concurrentKey) -> total 3 + 3 + 3 = 9
    const balFinal = await api('GET', `/api/inventory/balances?branchId=${branchId}&warehouseId=${warehouseId}`);
    const itemsFinal = Array.isArray(balFinal.data) ? balFinal.data : (balFinal.data.items || []);
    const skuBalFinal = itemsFinal.find(x => x.id === skuId || x.skuId === skuId);
    assert(skuBalFinal.reserved === 9, `Final reserved stock is exactly 9 (3+3+3), concurrency did not over-reserve!`);

  } catch (err) {
    console.error('Unexpected error during live test:', err);
    failed++;
  }

  console.log(`\n=== SUMMARY: ${passed} PASSED, ${failed} FAILED ===`);
  process.exit(failed > 0 ? 1 : 0);
}

runTests();
