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

function loadHelper(relativePath) {
  const source = fs.readFileSync(path.resolve(dir, relativePath), 'utf8');
  const js = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const mod = { exports: {} };
  vm.runInNewContext(js, { module: mod, exports: mod.exports });
  return mod.exports;
}

const helpers = [
  ['pos-app', loadHelper('src/services/posCartUnits.ts')],
  ['admin-dashboard', loadHelper('../admin-dashboard/src/services/posCartUnits.ts')],
];

const base = { unitId: 'hop', unitName: 'hộp', unitCode: 'hop', factor: 1, decimalPlaces: 0, salePrice: 20000, isBase: true, isDefaultSale: true };
const bao = { unitId: 'bao', unitName: 'bao', unitCode: 'bao', factor: 10, decimalPlaces: 0, salePrice: 180000, isBase: false, isDefaultSale: false };
const kg = { unitId: 'kg', unitName: 'kg', unitCode: 'kg', factor: 1, decimalPlaces: 3, salePrice: 5000, isBase: true, isDefaultSale: true };

const line = (unit, quantity, available = 25, units = [base, bao]) => ({
  variantId: 'sku-1',
  lineKey: `sku-1:${unit.unitId}`,
  unitId: unit.unitId,
  factorToBase: unit.factor,
  retailPrice: unit.salePrice,
  quantity,
  available,
  units,
});

for (const [name, h] of helpers) {
  test(`${name}: mã vạch quy cách chọn đúng đơn vị và giá của đơn vị đó`, () => {
    const out = h.resolveCartUnit({ retailPrice: 20000, units: [base, bao], matchedUnitId: 'bao' });
    assert.equal(out.unit.unitId, 'bao');
    assert.equal(out.price, 180000);
  });

  test(`${name}: mã vạch của đơn vị chưa có giá bị chặn, không tự tính giá`, () => {
    const unpriced = { ...bao, salePrice: null };
    assert.equal(h.resolveCartUnit({ retailPrice: 20000, units: [base, unpriced], matchedUnitId: 'bao' }), null);
  });

  test(`${name}: đơn vị bán mặc định chưa có giá thì dùng đơn vị gốc có giá`, () => {
    const out = h.resolveCartUnit({ retailPrice: 20000, units: [{ ...bao, salePrice: null, isDefaultSale: true }, { ...base, isDefaultSale: false }] });
    assert.equal(out.unit.unitId, 'hop');
    assert.equal(out.price, 20000);
  });

  test(`${name}: số lượng tối đa chia theo hệ số và trừ dòng khác cùng SKU`, () => {
    const hop = line(base, 7);
    const baoLine = line(bao, 1);
    assert.equal(h.baseQuantityInCart([hop, baoLine], 'sku-1'), 17);
    assert.equal(h.maxQuantityForLine([hop, baoLine], baoLine), 1);
    assert.equal(h.maxQuantityForLine([hop, baoLine], hop), 15);
    assert.equal(h.maxQuantityForLine([line(bao, 1, 25)], line(bao, 1, 25)), 2);
  });

  test(`${name}: đơn vị cho phép số lẻ không bị làm tròn thành số nguyên`, () => {
    const item = line(kg, 0.5, 1.25, [kg]);
    assert.equal(h.lineDecimalPlaces(item), 3);
    assert.equal(h.maxQuantityForLine([item], item), 1.25);
    assert.equal(h.normalizeQuantity(0.1 + 0.2, 3), 0.3);
    assert.equal(h.normalizeQuantity(1.9, 0), 2);
  });

  test(`${name}: giá mới khi PRICE_MISMATCH chỉ áp vào đúng SKU và đơn vị`, () => {
    const cart = [line(base, 1), line(bao, 1), { ...line(base, 1), variantId: 'sku-2', lineKey: 'sku-2:hop' }];
    const next = h.applyCurrentPrices(cart, [{ skuId: 'sku-1', unitId: 'bao', currentPrice: 200000 }]);
    assert.equal(next[0].retailPrice, 20000);
    assert.equal(next[1].retailPrice, 200000);
    assert.equal(next[1].units.find((u) => u.unitId === 'bao').salePrice, 200000);
    assert.equal(next[2].retailPrice, 20000);
  });
}
