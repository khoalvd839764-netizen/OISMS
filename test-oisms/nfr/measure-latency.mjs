#!/usr/bin/env node
/**
 * NFR-PERF-01: Đo độ trễ API (p50, p95, p99) với đồng thời 1 và 10.
 * Không sử dụng thư viện ngoài.
 */
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

function getArg(name, fallback) {
  const idx = process.argv.indexOf(name);
  return idx !== -1 && idx + 1 < process.argv.length ? process.argv[idx + 1] : fallback;
}

const baseUrl = getArg('--base-url', 'http://localhost:55002');
const email = getArg('--email', 'perf@oisms.local');
const password = getArg('--password', 'YourStrongDemoPassword123!');
const nRead = Number(getArg('--n-read', '30'));
const nReport = Number(getArg('--n-report', '10'));
const outFile = getArg('--out', '');
// --only <chuỗi>: chỉ đo các nhóm có nhãn chứa chuỗi này (ví dụ --only "POST /api/orders"), để đo lại một nhóm mà không đo lại số khác
const only = getArg('--only', '');

function percentile(sorted, p) {
  if (sorted.length === 0) return null;
  const idx = Math.ceil((p / 100) * sorted.length) - 1;
  return Number(sorted[Math.max(0, Math.min(sorted.length - 1, idx))].toFixed(2));
}

async function runBatch(label, count, concurrency, thresholdMs, fn) {
  if (only && !label.includes(only)) return null;
  const durations = [];
  let ok = 0;
  let fail = 0;
  let cursor = 0;

  async function worker() {
    while (true) {
      const idx = cursor++;
      if (idx >= count) break;
      const t0 = performance.now();
      try {
        const res = await fn(idx);
        const ms = performance.now() - t0;
        durations.push(ms);
        if (res.ok) ok++;
        else fail++;
      } catch {
        durations.push(performance.now() - t0);
        fail++;
      }
    }
  }

  await Promise.all(Array.from({ length: concurrency }, () => worker()));
  durations.sort((a, b) => a - b);
  const p50 = percentile(durations, 50);
  const p95 = percentile(durations, 95);
  const p99 = percentile(durations, 99);
  const status = fail === 0 && p95 !== null && p95 < thresholdMs ? 'ĐẠT' : 'CHƯA ĐẠT';
  const row = { label, count, concurrency, thresholdMs, ok, fail, p50, p95, p99, status };
  console.log(`${label} [c=${concurrency}, n=${count}] -> p50=${p50}ms p95=${p95}ms p99=${p99}ms ok=${ok}/${count} (${status} < ${thresholdMs}ms)`);
  return row;
}

const loginRes = await fetch(`${baseUrl}/api/auth/login`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email, password }),
});
if (!loginRes.ok) {
  throw new Error(`Login failed: ${loginRes.status} ${await loginRes.text()}`);
}
const { accessToken } = await loginRes.json();
const authHeaders = { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' };

const branches = await (await fetch(`${baseUrl}/api/branches`, { headers: authHeaders })).json();
const branchId = branches[0].id;
const skus = await (await fetch(`${baseUrl}/api/pos/search?query=PERF-SKU-&branchId=${branchId}`, { headers: authHeaders })).json();
if (!Array.isArray(skus) || skus.length < 20) {
  throw new Error(`Expected >= 20 PERF SKUs, got ${skus?.length}`);
}

const sku1 = skus[0];
const sku20Ids = skus.slice(0, 20).map((s) => s.variantId);
const q1 = `skuIds=${sku1.variantId}`;
const q20 = sku20Ids.map((id) => `skuIds=${id}`).join('&');

// 30 ngày gần nhất theo UTC+07 (khớp cách ReportsPage gửi)
const vnNow = new Date(Date.now() + 7 * 3600 * 1000);
const endVnDate = vnNow.toISOString().slice(0, 10);
const startVnDate = new Date(vnNow.getTime() - 29 * 86400 * 1000).toISOString().slice(0, 10);
const q30Days = `startDate=${encodeURIComponent(`${startVnDate}T00:00:00+07:00`)}&endDate=${encodeURIComponent(`${endVnDate}T23:59:59.999+07:00`)}`;

// Warmup
await fetch(`${baseUrl}/api/pos/search?query=PERF-SKU-01&branchId=${branchId}`, { headers: authHeaders });

const results = [];
for (const c of [1, 10]) {
  results.push(await runBatch('GET /api/pos/search (keyword)', nRead, c, 200, () =>
    fetch(`${baseUrl}/api/pos/search?query=PERF-SKU-01&branchId=${branchId}`, { headers: authHeaders })
  ));
  results.push(await runBatch('GET /api/pos/barcode/{code}', nRead, c, 200, () =>
    fetch(`${baseUrl}/api/pos/barcode/${encodeURIComponent(sku1.barcode || '8930000000001')}?branchId=${branchId}`, { headers: authHeaders })
  ));
  results.push(await runBatch('POST /api/pos/checkout', nRead, c, 500, () =>
    fetch(`${baseUrl}/api/pos/checkout`, {
      method: 'POST',
      headers: authHeaders,
      body: JSON.stringify({
        branchId,
        channel: 'POS',
        requestKey: randomUUID(),
        paymentMethod: 'CASH',
        amountGiven: sku1.retailPrice,
        changeDue: 0,
        totalAmount: sku1.retailPrice,
        items: [{ skuId: sku1.variantId, skuCode: sku1.sku, productName: sku1.productName, quantity: 1, unitPrice: sku1.retailPrice }],
      }),
    })
  ));
  // Ca 10 quầy bán các SKU khác nhau: request thứ idx bán SKU thứ (idx % 20), nên các request đồng thời
  // không tranh cùng khóa tồn kho (so với ca trên: mọi request cùng bán SKU đầu tiên).
  results.push(await runBatch('POST /api/pos/checkout (multi-SKU)', nRead, c, 500, (idx) => {
    const s = skus[idx % 20];
    return fetch(`${baseUrl}/api/pos/checkout`, {
      method: 'POST',
      headers: authHeaders,
      body: JSON.stringify({
        branchId,
        channel: 'POS',
        requestKey: randomUUID(),
        paymentMethod: 'CASH',
        amountGiven: s.retailPrice,
        changeDue: 0,
        totalAmount: s.retailPrice,
        items: [{ skuId: s.variantId, skuCode: s.sku, productName: s.productName, quantity: 1, unitPrice: s.retailPrice }],
      }),
    });
  }));
  results.push(await runBatch('POST /api/orders (Admin)', nRead, c, 500, () =>
    fetch(`${baseUrl}/api/orders`, {
      method: 'POST',
      headers: authHeaders,
      body: JSON.stringify({
        branchId,
        channel: 'Admin',
        requestKey: randomUUID(),
        totalAmount: sku1.retailPrice,
        // FIN-D-fix1: OrderItemRequestDto bắt buộc skuCode (thiếu thì 400 VALIDATION_ERROR)
        items: [{ skuId: sku1.variantId, skuCode: sku1.sku, productName: sku1.productName, quantity: 1, unitPrice: sku1.retailPrice }],
      }),
    })
  ));
  results.push(await runBatch('GET /api/reports/revenue-profit (unfiltered)', nReport, c, 2000, () =>
    fetch(`${baseUrl}/api/reports/revenue-profit`, { headers: authHeaders })
  ));
  results.push(await runBatch('GET /api/reports/revenue-profit (30 days)', nReport, c, 2000, () =>
    fetch(`${baseUrl}/api/reports/revenue-profit?${q30Days}`, { headers: authHeaders })
  ));
  results.push(await runBatch('GET /api/reports/revenue-profit (1 SKU)', nReport, c, 2000, () =>
    fetch(`${baseUrl}/api/reports/revenue-profit?${q1}`, { headers: authHeaders })
  ));
  results.push(await runBatch('GET /api/reports/revenue-profit (20 SKUs)', nReport, c, 2000, () =>
    fetch(`${baseUrl}/api/reports/revenue-profit?${q20}`, { headers: authHeaders })
  ));
}

if (outFile) {
  fs.mkdirSync(path.dirname(outFile), { recursive: true });
  fs.writeFileSync(outFile, JSON.stringify({ baseUrl, measuredAt: new Date().toISOString(), only: only || null, results: results.filter(Boolean) }, null, 2), 'utf8');
  console.log(`Saved raw results to ${outFile}`);
}
