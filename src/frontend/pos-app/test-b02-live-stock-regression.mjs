import test from 'node:test';
import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';

const run = promisify(execFile);
const root = fileURLToPath(new URL('../../../', import.meta.url));
const runner = fileURLToPath(new URL('./test-b02-hub-transport.mjs', import.meta.url));
const fixture = new URL('./test-fixtures/b02-corrupt-stock.mjs', import.meta.url).href;

test('B02-F06: live branch rejects wrong and missed-offline stock', {
  skip: process.env.POS_LIVE_TEST_ENV !== '1' && 'Requires the isolated live test backend',
  timeout: 100000,
}, async (t) => {
  for (const mode of ['wrong', 'stale']) {
    await t.test(mode, async () => {
      const childEnvironment = { ...process.env, POS_PREFLIGHT_HARNESS: '0', POS_STOCK_NEGATIVE_CONTROL: mode };
      // A fresh node:test process must not inherit the parent's test-worker flag.
      delete childEnvironment.NODE_TEST_CONTEXT;
      let result;
      try {
        const output = await run(process.execPath, ['--import', fixture, '--test', runner], {
          cwd: root,
          windowsHide: true,
          timeout: 45000,
          maxBuffer: 2 * 1024 * 1024,
          env: childEnvironment,
        });
        result = { ...output, code: 0 };
      } catch (error) {
        if (error.code !== 1) throw error;
        result = error;
      }
      const output = result.stdout + result.stderr;
      const match = output.match(/\[B02-STOCK-NEGATIVE-CONTROL\] (\{[^\n]+\})/);
      assert.ok(match, `Negative control must reach the real reconnect stock GET:\n${output}`);
      const values = JSON.parse(match[1]);
      assert.notEqual(values.actual, values.injected);
      assert.equal(result.code, 1, `Live runner must reject ${mode} stock instead of PASS:\n${output}`);
      assert.match(output, /Số tồn khôi phục phải khớp chính xác/);
      assert.doesNotMatch(output, /Live Event Mutation & Recovery Workflow: PASS/);
      console.log(JSON.stringify({ mode, ...values, runnerExit: result.code, result: 'wrong stock rejected' }));
    });
  }
});
