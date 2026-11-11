import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import vm from 'node:vm';

const app = new URL('../../../src/frontend/admin-dashboard/', import.meta.url);
const appRequire = createRequire(new URL('package.json', app));
const ts = appRequire('typescript');

function load(relativePath, require = appRequire) {
  const source = readFileSync(new URL(relativePath, app), 'utf8');
  const js = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
  }).outputText;
  const module = { exports: {} };
  vm.runInNewContext(js, { module, exports: module.exports, require });
  return module.exports;
}

const format = load('src/components/AiAssistant/chatFormat.ts');
const parse = (text) => JSON.parse(JSON.stringify(format.parseChatMessage(text)));

test('nhận chữ đậm và mã nội tuyến', () => {
  assert.deepEqual(parse('Đã bán **5 cái** mã `SKU-1`.'), [{
    type: 'paragraph',
    lines: [[{ text: 'Đã bán ' }, { text: '5 cái', bold: true }, { text: ' mã ' }, { text: 'SKU-1', code: true }, { text: '.' }]],
  }]);
});

test('gom gạch đầu dòng và danh sách số thành các khối', () => {
  assert.deepEqual(parse('- Nhập\n* Xuất\n• Trả\n1. Quận 1\n2. Quận 2'), [
    { type: 'bullets', items: [[{ text: 'Nhập' }], [{ text: 'Xuất' }], [{ text: 'Trả' }]] },
    { type: 'numbers', items: [[{ text: 'Quận 1' }], [{ text: 'Quận 2' }]] },
  ]);
});

test('bỏ dấu đậm lẻ và gộp nhiều dòng trắng', () => {
  assert.deepEqual(parse('**Thiếu dấu đóng\r\n\r\n\r\nThiếu dấu mở**'), [
    { type: 'paragraph', lines: [[{ text: 'Thiếu dấu đóng' }]] },
    { type: 'paragraph', lines: [[{ text: 'Thiếu dấu mở' }]] },
  ]);
});

test('chuyển tiêu đề thành đoạn in đậm', () => {
  assert.deepEqual(parse('## Doanh thu\nHôm nay có 5 đơn.'), [
    { type: 'paragraph', lines: [[{ text: 'Doanh thu', bold: true }]] },
    { type: 'paragraph', lines: [[{ text: 'Hôm nay có 5 đơn.' }]] },
  ]);
});

test('chuyển bảng thành chữ và bỏ dòng ngăn', () => {
  assert.deepEqual(parse('| SKU | Tồn |\n| :--- | ---: |\n| SKU-1 | 5 |\n|---|'), [{
    type: 'paragraph', lines: [[{ text: 'SKU · Tồn' }], [{ text: 'SKU-1 · 5' }]],
  }]);
});

test('giữ thẻ script là chữ và React không tạo thẻ hay link từ nội dung', () => {
  const script = '<script>alert(1)</script>';
  assert.deepEqual(parse(script), [{ type: 'paragraph', lines: [[{ text: script }]] }]);
  const { ChatMessageBody } = load('src/components/AiAssistant/ChatMessageBody.tsx', (id) =>
    id === './chatFormat' ? format : appRequire(id));
  const React = appRequire('react');
  const { renderToStaticMarkup } = appRequire('react-dom/server');
  const html = renderToStaticMarkup(React.createElement(ChatMessageBody, {
    content: script + '\n[Chi tiết](https://example.invalid)\n- **5 cái**\n1. `SKU-1`',
  }));
  assert.ok(html.includes('&lt;script&gt;alert(1)&lt;/script&gt;'));
  assert.ok(html.includes('[Chi tiết](https://example.invalid)'));
  assert.ok(html.includes('<ul><li><strong>5 cái</strong></li></ul>'));
  assert.ok(html.includes('<ol><li><code>SKU-1</code></li></ol>'));
  assert.doesNotMatch(html, /<(script|a)(\s|>)/i);
});

test('hai tình huống doanh thu và tồn kho không còn dấu ** thừa', () => {
  for (const response of [
    'Để có thể tổng hợp **doanh thu hôm nay**, bạn vui lòng chọn chi nhánh.',
    'Để kiểm tra **SKU nào sắp hết ở chi nhánh Quận 1**, vui lòng chọn chi nhánh.**',
  ]) {
    const segments = parse(response).flatMap((block) => block.lines ?? block.items).flat();
    assert.ok(segments.some((segment) => segment.bold));
    assert.ok(segments.every((segment) => !segment.text.includes('**')));
  }
});
