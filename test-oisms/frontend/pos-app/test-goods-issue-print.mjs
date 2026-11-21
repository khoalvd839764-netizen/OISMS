import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import { ts, APP_URL } from './test-support/app-path.mjs';

const mod = { exports: {} };
const source = fs.readFileSync(new URL('./src/services/goodsIssuePrint.ts', APP_URL), 'utf8');
vm.runInNewContext(
  ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText,
  { module: mod, exports: mod.exports }
);

const { canPrintGoodsIssue, splitGoodsIssuePages, getGoodsIssueFetchErrorMessage } = mod.exports;

test('canPrintGoodsIssue: chỉ hiện nút khi checkout thành công có orderId và không phải phiếu tạm tính', () => {
  assert.equal(canPrintGoodsIssue({ isDraft: false, orderId: 'ord-123' }), true);
  assert.equal(canPrintGoodsIssue({ isDraft: true, orderId: 'ord-123' }), false);
  assert.equal(canPrintGoodsIssue({ isDraft: false, orderId: '' }), false);
  assert.equal(canPrintGoodsIssue({ isDraft: false }), false);
  assert.equal(canPrintGoodsIssue(null), false);
  assert.equal(canPrintGoodsIssue(undefined), false);
});

test('splitGoodsIssuePages: 1 kho không ngắt trang; nhiều kho mỗi kho một trang có pageBreakAfter', () => {
  assert.equal(splitGoodsIssuePages([]).length, 0);

  const single = splitGoodsIssuePages([{ id: 'gi-1', warehouseName: 'Kho A' }]);
  assert.equal(single.length, 1);
  assert.equal(single[0].pageNumber, 1);
  assert.equal(single[0].totalPages, 1);
  assert.equal(single[0].hasPageBreakAfter, false);

  const multi = splitGoodsIssuePages([
    { id: 'gi-1', warehouseName: 'Kho A' },
    { id: 'gi-2', warehouseName: 'Kho B' },
  ]);
  assert.equal(multi.length, 2);
  assert.equal(multi[0].pageNumber, 1);
  assert.equal(multi[0].hasPageBreakAfter, true);
  assert.equal(multi[1].pageNumber, 2);
  assert.equal(multi[1].hasPageBreakAfter, false);
});

test('getGoodsIssueFetchErrorMessage: 404 trả thông báo chuẩn, lỗi khác trả chi tiết hoặc fallback', () => {
  assert.equal(getGoodsIssueFetchErrorMessage(404), 'Không tìm thấy phiếu xuất kho của đơn này');
  assert.equal(getGoodsIssueFetchErrorMessage(500, 'Lỗi máy chủ'), 'Lỗi máy chủ');
  assert.match(getGoodsIssueFetchErrorMessage(undefined), /Không thể tải phiếu xuất kho/);
});

test('GoodsIssuePrintModal POS: có A4/A5, không có cột giá vốn', () => {
  const component = fs.readFileSync(
    new URL('./src/components/pos/GoodsIssuePrintModal.tsx', APP_URL),
    'utf8'
  );
  assert.match(component, /A4/);
  assert.match(component, /A5/);
  assert.doesNotMatch(component, /unitCost|totalCost|Đơn giá vốn/);
});
