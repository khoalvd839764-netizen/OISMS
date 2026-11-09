import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { APP_DIR, appRequire } from './test-support/app-path.mjs';

const require = appRequire;
const ts = require('typescript');
const dir = APP_DIR;

function loadModuleWithContext(relativePath, mockContext = {}) {
  const fullPath = path.resolve(dir, relativePath);
  const source = fs.readFileSync(fullPath, 'utf8');
  const js = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;

  const localStore = new Map();
  const sessionStore = new Map();
  const localStorage = {
    getItem: (k) => (localStore.has(k) ? localStore.get(k) : null),
    setItem: (k, v) => localStore.set(k, String(v)),
    removeItem: (k) => localStore.delete(k),
    clear: () => localStore.clear(),
  };
  const sessionStorage = {
    getItem: (k) => (sessionStore.has(k) ? sessionStore.get(k) : null),
    setItem: (k, v) => sessionStore.set(k, String(v)),
    removeItem: (k) => sessionStore.delete(k),
    clear: () => sessionStore.clear(),
  };

  const mod = { exports: {} };

  const customRequire = (specifier) => {
    if (specifier === './qrService') {
      const parentDir = path.dirname(fullPath);
      const targetRel = path.relative(dir, path.resolve(parentDir, 'qrService.ts'));
      return loadModuleWithContext(targetRel, mockContext).exports;
    }
    return require(specifier);
  };

  const sandbox = {
    module: mod,
    exports: mod.exports,
    require: customRequire,
    localStorage,
    sessionStorage,
    window: {
      localStorage,
      sessionStorage,
      ...mockContext.window,
    },
    console,
    TextEncoder: globalThis.TextEncoder,
    crypto: globalThis.crypto,
    ...mockContext,
  };

  vm.runInNewContext(js, sandbox);
  return { exports: mod.exports, sandbox, localStorage, sessionStorage };
}

const targets = [
  {
    name: 'pos-app',
    qrServicePath: 'src/services/qrService.ts',
    sourcePath: 'src/services/paymentAccountSource.ts',
  },
];

const ACC_1 = '11111111-aaaa-4bbb-8ccc-000000000001';
const ACC_2 = '22222222-aaaa-4bbb-8ccc-000000000002';
const ACC_3 = '33333333-aaaa-4bbb-8ccc-000000000003';

const bankFixtureA = {
  bankBin: '970422',
  bankName: 'MB Bank',
  accountNumber: '0123456789',
  accountName: 'NGUYEN VAN A',
  template: 'compact2',
  isTestFixture: true,
};

const bankFixtureB = {
  bankBin: '970415',
  bankName: 'VietinBank',
  accountNumber: '987654321',
  accountName: 'NGUYEN VAN B',
  template: 'compact2',
  isTestFixture: true,
};

for (const target of targets) {
  test(`${target.name}: payload EMVCo + CRC validity and short reference`, () => {
    const { exports: qr } = loadModuleWithContext(target.qrServicePath);
    const requestKey = 'a1b2c3d4-e5f6-4789-abcd-ef1234567890';
    const result = qr.generateVietQrPayload(bankFixtureA, {
      amount: 150000,
      requestKey,
      branchId: 'branch-01',
    });

    assert.equal(result.crcValid, true);
    assert.equal(result.transactionReference, 'A1B2C3D4');
    assert.equal(result.transferContent, 'OISMS POS A1B2C3D4');
    assert.ok(result.rawPayload.startsWith('000201010212'));
    assert.ok(result.quickLinkUrl.includes('970422-0123456789-compact2.png'));
    assert.ok(result.quickLinkUrl.includes('amount=150000'));

    const parsed = qr.parseTlvPayload(result.rawPayload);
    assert.equal(parsed.crcValid, true);
    assert.equal(parsed.crcCalculated, result.crcCode);
  });

  test(`${target.name}: giữ key khi mở lại cùng giỏ và cùng user`, () => {
    const { exports: qr } = loadModuleWithContext(target.qrServicePath);
    const user = { id: 'usr-1', tenantId: 'ten-1' };
    const branchId = 'br-1';
    const cart = JSON.stringify([['sku-1', '', 'SKU1', 2, 50000]]);
    const amount = 100000;
    let keyCount = 0;
    const createKey = () => `c8b7e289-4a0b-46fb-9762-23c8930128${String(++keyCount).padStart(2, '0')}`;

    const intent1 = qr.getOrCreatePosQrIntent(user, branchId, cart, amount, bankFixtureA, createKey);
    assert.ok(intent1);
    assert.equal(intent1.requestKey, 'c8b7e289-4a0b-46fb-9762-23c893012801');

    // Mở lại với cùng user, branch, cart, amount, bankConfig -> phải giữ nguyên intent
    const intent2 = qr.getOrCreatePosQrIntent(user, branchId, cart, amount, bankFixtureA, createKey);
    assert.equal(intent2.requestKey, intent1.requestKey);
    assert.equal(keyCount, 1);
  });

  test(`${target.name}: đổi tài khoản ra key mới`, () => {
    const { exports: qr } = loadModuleWithContext(target.qrServicePath);
    const user = { id: 'usr-1', tenantId: 'ten-1' };
    const branchId = 'br-1';
    const cart = JSON.stringify([['sku-1', '', 'SKU1', 2, 50000]]);
    const amount = 100000;
    let keyCount = 0;
    const createKey = () => `c8b7e289-4a0b-46fb-9762-23c8930128${String(++keyCount).padStart(2, '0')}`;

    const intentA = qr.getOrCreatePosQrIntent(user, branchId, cart, amount, bankFixtureA, createKey, 'acc-a');
    assert.equal(intentA.requestKey, 'c8b7e289-4a0b-46fb-9762-23c893012801');

    // Đổi tài khoản sang acc-b -> phải tạo intent mới với key mới
    const intentB = qr.getOrCreatePosQrIntent(user, branchId, cart, amount, bankFixtureB, createKey, 'acc-b');
    assert.notEqual(intentB.requestKey, intentA.requestKey);
    assert.equal(intentB.requestKey, 'c8b7e289-4a0b-46fb-9762-23c893012802');
  });

  test(`${target.name}: thiếu tài khoản thì không tạo intent, không dùng tài khoản mẫu`, () => {
    const { exports: qr } = loadModuleWithContext(target.qrServicePath);
    assert.equal(typeof qr.getPosBankConfig, 'undefined');
    assert.equal(typeof qr.getPaymentAccountsSync, 'undefined');
    const user = { id: 'usr-1', tenantId: 'ten-1' };
    const intent = qr.getOrCreatePosQrIntent(user, 'br-none', '[]', 50000, null, () => 'key');
    assert.equal(intent, null);
  });

  test(`${target.name}: intent không có paymentAccountId không bị dùng lại cho tài khoản từ API`, () => {
    const { exports: qr } = loadModuleWithContext(target.qrServicePath);
    const user = { id: 'usr-1', tenantId: 'ten-1' };
    let n = 0; const createKey = () => `c8b7e289-4a0b-46fb-9762-23c8930128${String(++n).padStart(2, '0')}`;
    const legacy = qr.getOrCreatePosQrIntent(user, 'br-1', '[]', 1000, bankFixtureA, createKey);
    const fromApi = qr.getOrCreatePosQrIntent(user, 'br-1', '[]', 1000, bankFixtureB, createKey, ACC_1);
    assert.notEqual(fromApi.requestKey, legacy.requestKey);
    assert.equal(fromApi.paymentAccountId, ACC_1);
    assert.equal(qr.getSavedPosQrIntent(user, 'br-1').requestKey, fromApi.requestKey);
    assert.equal(qr.getSavedPosQrIntent({ id: 'usr-2', tenantId: 'ten-1' }, 'br-1'), null);
  });

  test(`${target.name}: loadPaymentAccounts gọi đúng URL, gửi token, chuẩn hóa dữ liệu`, async () => {
    const { exports: qr } = loadModuleWithContext(target.qrServicePath);
    const calls = [];
    const fetchFn = async (url, init) => { calls.push({ url, init }); return { ok: true, status: 200, json: async () => [
      { id: ACC_1, bankBin: '970422', bankName: 'MB Bank', bankShortName: 'MB', accountNumber: '0123456789', accountName: 'SHOP A', isDefault: true },
      { id: ACC_2, bankBin: '970415', bankName: 'VietinBank', bankShortName: '', accountNumber: '987654321', accountName: 'SHOP B', isDefault: true },
      { id: ACC_1, bankBin: '970422', bankName: 'Trùng id', accountNumber: '0123456789', accountName: 'X' },
      { id: 'legacy-970422-1', bankBin: '970422', bankName: 'MB', accountNumber: '0123456789', accountName: 'X' },
      { id: ACC_3, bankBin: '97', bankName: 'Sai BIN', accountNumber: '0123456789', accountName: 'X' },
    ] }; };
    const result = await qr.loadPaymentAccounts({ baseUrl: 'https://api.example.test/', token: 'tok', fetchFn });
    assert.equal(calls.length, 1);
    assert.equal(calls[0].url, 'https://api.example.test/api/payment-accounts/active');
    assert.equal(calls[0].init.headers.Authorization, 'Bearer tok');
    assert.equal(result.status, 'ok');
    assert.equal(result.accounts.map((a) => a.id).join(','), [ACC_1, ACC_2].join(','));
    assert.equal(result.accounts.map((a) => a.isDefault).join(','), 'true,false');
    assert.equal(result.accounts[1].bankShortName, 'VietinBank');
  });

  test(`${target.name}: loadPaymentAccounts báo lỗi rõ khi thiếu token, 401, 403, 5xx, mất mạng, JSON sai`, async () => {
    const { exports: qr } = loadModuleWithContext(target.qrServicePath);
    let called = 0;
    const noToken = await qr.loadPaymentAccounts({ baseUrl: 'x', token: null, fetchFn: async () => { called++; } });
    assert.equal(noToken.status, 'error'); assert.equal(noToken.httpStatus, 401); assert.equal(called, 0);
    for (const status of [401, 403, 500]) {
      const r = await qr.loadPaymentAccounts({ baseUrl: 'x', token: 't', fetchFn: async () => ({ ok: false, status, json: async () => ({}) }) });
      assert.equal(r.status, 'error'); assert.equal(r.httpStatus, status); assert.ok(r.message.length > 0);
    }
    const offline = await qr.loadPaymentAccounts({ baseUrl: 'x', token: 't', fetchFn: async () => { throw new TypeError('Failed to fetch'); } });
    assert.equal(offline.status, 'error');
    const badJson = await qr.loadPaymentAccounts({ baseUrl: 'x', token: 't', fetchFn: async () => ({ ok: true, status: 200, json: async () => ({ items: [] }) }) });
    assert.equal(badJson.status, 'error');
    const empty = await qr.loadPaymentAccounts({ baseUrl: 'x', token: 't', fetchFn: async () => ({ ok: true, status: 200, json: async () => [] }) });
    assert.equal(empty.status, 'empty');
  });

  test(`${target.name}: không đọc localStorage hay window làm nguồn tài khoản`, async () => {
    const legacy = [{ version: 2, tenantId: 'ten-1', accounts: [{ id: ACC_1, bankBin: '970422', bankName: 'MB', bankShortName: 'MB', accountNumber: '0123456789', accountName: 'A', isDefault: true }] }];
    const { exports: qr, localStorage } = loadModuleWithContext(target.qrServicePath, { window: { __OISMS_POS_PAYMENT_ACCOUNTS__: legacy } });
    localStorage.setItem('pos_payment_accounts_v2:ten-1', JSON.stringify(legacy[0]));
    const result = await qr.loadPaymentAccounts({ baseUrl: 'x', token: 't', fetchFn: async () => ({ ok: true, status: 200, json: async () => [] }) });
    assert.equal(result.status, 'empty');
  });

  test(`${target.name}: paymentAccountSource chỉ xuất hàm tải từ API`, () => {
    const source = fs.readFileSync(path.resolve(dir, target.sourcePath), 'utf8');
    assert.match(source, /export function fetchActivePaymentAccounts\(/);
    assert.doesNotMatch(source, /localStorage|__OISMS_|getPaymentAccountsSync/);
  });
}
