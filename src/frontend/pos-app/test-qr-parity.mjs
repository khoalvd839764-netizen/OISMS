import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const ts = require('typescript');
const dir = path.dirname(fileURLToPath(import.meta.url));

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
  {
    name: 'admin-dashboard',
    qrServicePath: '../admin-dashboard/src/services/qrService.ts',
    sourcePath: '../admin-dashboard/src/services/paymentAccountSource.ts',
  },
];

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

  test(`${target.name}: thiếu cấu hình trả về null hoặc empty, không dùng fake`, () => {
    const { exports: qr } = loadModuleWithContext(target.qrServicePath);
    assert.equal(qr.getPosBankConfig('br-none', 'ten-none'), null);
    assert.equal(qr.getPaymentAccountsSync('ten-none', 'br-none').length, 0);

    const user = { id: 'usr-1', tenantId: 'ten-1' };
    const cart = '[]';
    const intent = qr.getOrCreatePosQrIntent(user, 'br-none', cart, 50000, null, () => 'key');
    assert.equal(intent, null);
  });

  test(`${target.name}: sai tenant không đọc được tài khoản của tenant khác`, () => {
    const { exports: qr, localStorage } = loadModuleWithContext(target.qrServicePath);
    const tenantA = 'tenant-a-1111';
    const tenantB = 'tenant-b-2222';

    // Lưu envelope v2 cho tenant A
    localStorage.setItem(
      `pos_payment_accounts_v2:${encodeURIComponent(tenantA)}`,
      JSON.stringify({
        version: 2,
        tenantId: tenantA,
        accounts: [
          {
            id: 'acc-1',
            bankBin: '970422',
            bankName: 'MB',
            bankShortName: 'MB',
            accountNumber: '1111222233',
            accountName: 'SHOP A',
            isDefault: true,
          },
        ],
      })
    );

    // Tenant A lấy được
    const accountsA = qr.getPaymentAccountsSync(tenantA);
    assert.equal(accountsA.length, 1);
    assert.equal(accountsA[0].accountNumber, '1111222233');

    // Tenant B truy vấn không lấy được tài khoản của tenant A
    const accountsB = qr.getPaymentAccountsSync(tenantB);
    assert.equal(accountsB.length, 0);
  });

  test(`${target.name}: envelope v1 cũ bị bỏ qua khi đọc theo tenant`, () => {
    const { exports: qr, localStorage } = loadModuleWithContext(target.qrServicePath);
    const tenant = 'tenant-test-v1';

    // Giả lập dữ liệu format v1 không đúng chuẩn v2
    localStorage.setItem(
      `pos_payment_accounts_v2:${encodeURIComponent(tenant)}`,
      JSON.stringify({
        version: 1, // Sai version
        tenantId: tenant,
        accounts: [{ id: 'old-acc' }],
      })
    );

    const accounts = qr.getPaymentAccountsSync(tenant);
    assert.equal(accounts.length, 0);
  });

  test(`${target.name}: paymentAccountSource xuất đủ các hàm đồng bộ`, () => {
    const { exports: src } = loadModuleWithContext(target.sourcePath);
    assert.equal(typeof src.loadPaymentAccounts, 'function');
    assert.equal(typeof src.getPaymentAccountsSync, 'function');
    assert.equal(typeof src.toBankConfig, 'function');

    const bankConfig = src.toBankConfig({
      id: 'acc-1',
      bankBin: '970422',
      bankName: 'MB Bank',
      bankShortName: 'MB',
      accountNumber: '0123456789',
      accountName: 'NGUYEN VAN A',
      isDefault: true,
    });
    assert.equal(bankConfig.bankBin, '970422');
    assert.equal(bankConfig.accountNumber, '0123456789');
    assert.equal(bankConfig.accountName, 'NGUYEN VAN A');
    assert.equal(bankConfig.bankName, 'MB');
  });
}
