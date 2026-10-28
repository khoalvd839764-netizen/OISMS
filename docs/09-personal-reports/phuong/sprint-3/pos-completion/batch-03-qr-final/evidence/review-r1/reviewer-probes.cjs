const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');

const root = process.cwd();
const batch = path.resolve(__dirname, '../..');
const ts = require(path.join(root, 'src/frontend/pos-app/node_modules/typescript'));
function load(relative, withBuffer = true) {
  const source = fs.readFileSync(path.join(batch, relative), 'utf8');
  const js = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const mod = { exports: {} };
  const globals = { module: mod, exports: mod.exports, console, Date, encodeURIComponent };
  if (withBuffer) globals.Buffer = Buffer;
  vm.runInNewContext(js, globals, { filename: relative });
  return mod.exports;
}
const service = load('qr-module/qr-service.ts');
const fixture = load('qr-module/qr-fixtures.ts');
const config = fixture.VALID_TEST_BANK_VIETINBANK;
const payload = fixture.VALID_PAYLOAD_STANDARD;
const observations = [];

let browserError;
try { load('qr-module/qr-service.ts', false).generateVietQrPayload(config, payload); }
catch (error) { browserError = `${error.name}: ${error.message}`; }
assert.match(browserError || '', /Buffer is not defined/);
observations.push({ case: 'B03-F01-browser-globals', provider: 'actual TS module in VM without Node Buffer', actual: browserError, reproduced: true });

const largeAmount = service.generateVietQrPayload(config, { ...payload, amount: 999_999_999_999_999 });
assert.equal(new URL(largeAmount.quickLinkUrl).searchParams.get('amount').length, 15);
const longAccount = service.generateVietQrPayload({ ...config, accountNumber: '123456789012345678901234567890' }, payload);
assert.ok(longAccount.quickLinkUrl.includes('123456789012345678901234567890'));
const longDescription = service.generateVietQrPayload(config, { ...payload, orderDescription: 'A'.repeat(100) });
let offset = 0;
let parseError = null;
while (offset < longDescription.rawPayload.length) {
  const id = longDescription.rawPayload.slice(offset, offset + 2);
  const lengthText = longDescription.rawPayload.slice(offset + 2, offset + 4);
  if (!/^\d{2}$/.test(id) || !/^\d{2}$/.test(lengthText) || Number(lengthText) < 1) {
    parseError = { offset, id, lengthText };
    break;
  }
  const length = Number(lengthText);
  if (offset + 4 + length > longDescription.rawPayload.length) {
    parseError = { offset, id, lengthText, reason: 'value overruns payload' };
    break;
  }
  offset += 4 + length;
}
assert.ok(parseError);
observations.push({ case: 'B03-F02-serializer-boundaries', provider: 'actual generator; independent two-digit TLV reader; no HTTP request', actual: { acceptedAmountDigits: 15, acceptedAccountLength: 30, acceptedDescriptionLength: 100, malformedTlv: parseError, crcStillReportedValid: longDescription.crcValid }, reproduced: true });

const first = service.generateVietQrPayload(config, payload);
const differentKey = 'a1b2c3d4-0000-4000-8000-000000000001';
assert.notEqual(differentKey, payload.requestKey);
const different = service.generateVietQrPayload(config, { ...payload, requestKey: differentKey });
const incorrectlySame = service.assertIdempotentPayload(first, different);
assert.equal(incorrectlySame, true);
observations.push({ case: 'B03-F03-full-request-identity', provider: 'actual generator and assertion', actual: { firstRequestKey: payload.requestKey, differentRequestKey: differentKey, sameRawPayload: first.rawPayload === different.rawPayload, assertIdempotentPayload: incorrectlySame }, reproduced: true });

const runner = path.join(batch, 'e2e/test-runner-e2e-guard.mjs');
const cases = [
  { name: 'actual-demo-name-without-marker', vars: { POSTGRES_TEST_PORT: '5432', POSTGRES_TEST_DB: 'oism_db' } },
  { name: 'alternate-port-without-marker', vars: { POSTGRES_TEST_PORT: '5433', POSTGRES_TEST_DB: 'oisms' } },
  { name: 'invalid-port-without-marker', vars: { POSTGRES_TEST_PORT: 'not-a-port', POSTGRES_TEST_DB: 'oisms' } },
  { name: 'demo-bypass-switch', vars: { POSTGRES_TEST_PORT: '5432', POSTGRES_TEST_DB: 'oisms', ALLOW_DEMO_MUTATION: '1' } },
  { name: 'claimed-test-env-no-server', vars: { POSTGRES_TEST_PORT: '5433', POSTGRES_TEST_DB: 'oisms_test', POS_TEST_ENV: '1' } },
];
for (const entry of cases) {
  const env = { ...process.env };
  for (const key of ['POSTGRES_TEST_PORT', 'POSTGRES_TEST_HOST', 'POSTGRES_TEST_DB', 'POS_TEST_ENV', 'ALLOW_DEMO_MUTATION']) delete env[key];
  Object.assign(env, entry.vars);
  const run = spawnSync(process.execPath, [runner], { cwd: root, env, encoding: 'utf8', timeout: 5000 });
  assert.equal(run.status, 0);
  assert.match(run.stdout, /GUARD PASSED/);
  observations.push({ case: `B03-F04-guard-${entry.name}`, provider: 'actual runner subprocess; no backend or DB operations exist in runner', input: entry.vars, actual: { exitCode: run.status, guardPassed: true, output: run.stdout }, reproduced: true });
}

const protectedBaseline = JSON.parse(fs.readFileSync(path.join(batch, 'evidence/baseline-hashes.json'), 'utf8'));
const protectedFiles = Object.entries(protectedBaseline.protectedFiles).map(([relative, expected]) => {
  const actual = crypto.createHash('sha256').update(fs.readFileSync(path.join(root, relative))).digest('hex');
  return { path: relative, expected, actual, matches: actual === expected };
});
assert.ok(protectedFiles.every(file => file.matches));
const sourceHashes = ['qr-module/qr-service.ts', 'qr-module/qr-types.ts', 'qr-module/qr-fixtures.ts', 'qr-module/test-qr-module.mjs', 'e2e/test-runner-e2e-guard.mjs', 'brief.md', 'handover.md'].map(relative => ({ path: relative, sha256: crypto.createHash('sha256').update(fs.readFileSync(path.join(batch, relative))).digest('hex') }));
const result = { checkedAt: new Date().toISOString(), head: spawnSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).stdout.trim(), scope: 'B03 parallel preparation; no product edits, network payment calls or DB mutations', observations, protectedFiles, sourceHashes };
fs.writeFileSync(path.join(__dirname, 'probe-results.json'), JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify(result, null, 2));
