#!/usr/bin/env node
/**
 * NFR-USA-01: Script thăm dò độ sẵn sàng (/health/live và trang POS) theo chu kỳ.
 * Chỉ tính uptime trong khung giờ cao điểm 07:00–22:00 Asia/Ho_Chi_Minh (UTC+07).
 * Không dùng thư viện ngoài.
 */
import fs from 'node:fs';
import path from 'node:path';

function getArg(name, fallback) {
  const idx = process.argv.indexOf(name);
  return idx !== -1 && idx + 1 < process.argv.length ? process.argv[idx + 1] : fallback;
}

const healthUrl = getArg('--health-url', 'http://localhost:5000/health/live');
const posUrl = getArg('--pos-url', 'http://localhost:5174/');
const intervalSec = Number(getArg('--interval-sec', '30'));
const durationSec = Number(getArg('--duration-sec', '3600'));
const maxProbes = Number(getArg('--max-probes', '0'));
const outDir = path.resolve(getArg('--out-dir', 'docs/06-testing-and-qa/evidence/goi-5/NFR-USA-01'));

function getVietnamHourMinute(date) {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false
  }).formatToParts(date);
  const map = Object.fromEntries(parts.map((p) => [p.type, p.value]));
  const hour = Number(map.hour);
  const vnIso = `${map.year}-${map.month}-${map.day}T${map.hour}:${map.minute}:${map.second}+07:00`;
  const inPeakWindow = hour >= 7 && hour < 22;
  return { vnIso, inPeakWindow };
}

async function probeOnce(url) {
  const started = performance.now();
  try {
    const res = await fetch(url, { signal: AbortSignal.timeout(5000) });
    const ms = Math.round(performance.now() - started);
    return { status: res.status, ms, ok: res.ok };
  } catch {
    const ms = Math.round(performance.now() - started);
    return { status: 0, ms, ok: false };
  }
}

fs.mkdirSync(outDir, { recursive: true });
const csvPath = path.join(outDir, 'uptime-probe.csv');
const summaryPath = path.join(outDir, 'uptime-summary.json');

fs.writeFileSync(
  csvPath,
  'timestamp_utc,timestamp_vn,in_peak_window,health_status,health_ms,pos_status,pos_ms,ok\n',
  'utf8'
);

const startedAt = Date.now();
const endAt = startedAt + durationSec * 1000;
let totalProbes = 0;
let totalOk = 0;
let peakProbes = 0;
let peakOk = 0;

while (true) {
  const now = new Date();
  const { vnIso, inPeakWindow } = getVietnamHourMinute(now);
  const [health, pos] = await Promise.all([probeOnce(healthUrl), probeOnce(posUrl)]);
  const ok = health.ok && pos.ok;

  totalProbes++;
  if (ok) totalOk++;
  if (inPeakWindow) {
    peakProbes++;
    if (ok) peakOk++;
  }

  const line = `${now.toISOString()},${vnIso},${inPeakWindow},${health.status},${health.ms},${pos.status},${pos.ms},${ok}\n`;
  fs.appendFileSync(csvPath, line, 'utf8');
  console.log(`[${vnIso}] peak=${inPeakWindow} health=${health.status}(${health.ms}ms) pos=${pos.status}(${pos.ms}ms) ok=${ok}`);

  if (maxProbes > 0 && totalProbes >= maxProbes) break;
  if (Date.now() >= endAt) break;

  await new Promise((r) => setTimeout(r, intervalSec * 1000));
}

const summary = {
  healthUrl,
  posUrl,
  intervalSec,
  totalProbes,
  totalOk,
  overallUptimePct: totalProbes > 0 ? Number(((totalOk / totalProbes) * 100).toFixed(2)) : null,
  peakWindow: '07:00-22:00 Asia/Ho_Chi_Minh',
  peakProbes,
  peakOk,
  peakUptimePct: peakProbes > 0 ? Number(((peakOk / peakProbes) * 100).toFixed(2)) : null
};

fs.writeFileSync(summaryPath, JSON.stringify(summary, null, 2), 'utf8');
console.log('Summary:', JSON.stringify(summary));
