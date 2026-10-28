import http from 'node:http';
import net from 'node:net';

/** Loopback fault proxy. Business responses always come from the real backend. */
export async function startLiveProxy(targetUrl) {
  const target = new URL(targetUrl), sockets = new Set(), upgrades = new Set(), held = [];
  const control = { checkout: [], dropNextCheckout: false, rejectNextAuth: false, hubOffline: false, failStock: false, stockReads: 0, stockFailures: 0, holdNextStock: false, stockHeld: false, hubConnections: 0 };
  const server = http.createServer(async (req, res) => {
    const route = new URL(req.url, 'http://fixture.local').pathname;
    const cors = { 'access-control-allow-origin': req.headers.origin || '*', 'access-control-allow-credentials': 'true', 'access-control-allow-headers': req.headers['access-control-request-headers'] || 'authorization,content-type,idempotency-key,x-request-key,x-signalr-user-agent', 'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS' };
    if (req.method === 'OPTIONS') { res.writeHead(204, cors).end(); return; }
    if (control.hubOffline && route.startsWith('/hubs/')) { res.writeHead(503, cors).end(); return; }
    const isStock = /\/api\/pos\/(search|barcode)/.test(route) || route.startsWith('/api/inventory/balances/');
    if (isStock) { control.stockReads++; if (control.failStock) { control.stockFailures++; res.writeHead(503, cors).end(); return; } }
    const chunks = []; for await (const chunk of req) chunks.push(chunk); const body = Buffer.concat(chunks);
    const headers = { ...req.headers, host: target.host };
    if (control.rejectNextAuth && req.method === 'POST' && route === '/api/pos/checkout') { control.rejectNextAuth = false; headers.authorization = 'Bearer deliberately-invalid-test-token'; }
    const upstream = http.request({ hostname: target.hostname === 'localhost' ? '127.0.0.1' : target.hostname, port: target.port, path: req.url, method: req.method, headers }, upstreamResponse => {
      const data = []; upstreamResponse.on('data', b => data.push(b)); upstreamResponse.on('end', () => {
        const result = Buffer.concat(data);
        if (req.method === 'POST' && route === '/api/pos/checkout') {
          let response = null;
          try { response = JSON.parse(result.toString()); } catch { /* Real401 may have an empty body. */ }
          const record = { payload: JSON.parse(body.toString()), status: upstreamResponse.statusCode, response, dropped: false };
          control.checkout.push(record);
          if (control.dropNextCheckout && upstreamResponse.statusCode >= 200 && upstreamResponse.statusCode < 300) {
            control.dropNextCheckout = false; record.dropped = true;
            // Deliver headers then truncate the body. A bare socket reset can
            // make Chromium transparently resend POST before React sees it.
            res.writeHead(200, { ...cors, 'content-type': 'application/json', 'content-length': result.length + 100 });
            res.write(result.subarray(0, 2)); setTimeout(() => res.destroy(), 20); return;
          }
        }
        const deliver = () => { res.writeHead(upstreamResponse.statusCode, { ...upstreamResponse.headers, ...cors }); res.end(result); };
        if (isStock && control.holdNextStock) { control.holdNextStock = false; control.stockHeld = true; held.push(deliver); } else deliver();
      });
    });
    upstream.on('error', () => res.destroy()); upstream.end(body);
  });
  server.on('connection', s => { sockets.add(s); s.on('error', () => {}); s.on('close', () => sockets.delete(s)); });
  server.on('upgrade', (req, client, head) => {
    if (control.hubOffline) { client.destroy(); return; }
    control.hubConnections++; upgrades.add(client);
    const upstream = net.connect(target.port, target.hostname === 'localhost' ? '127.0.0.1' : target.hostname, () => {
      upstream.write(`${req.method} ${req.url} HTTP/${req.httpVersion}\r\n`);
      for (let i = 0; i < req.rawHeaders.length; i += 2) upstream.write(`${req.rawHeaders[i]}: ${req.rawHeaders[i].toLowerCase() === 'host' ? target.host : req.rawHeaders[i + 1]}\r\n`);
      upstream.write('\r\n'); if (head.length) upstream.write(head); client.pipe(upstream); upstream.pipe(client);
    });
    sockets.add(upstream);
    client.on('error', () => upstream.destroy()); upstream.on('error', () => client.destroy());
    client.on('close', () => { upgrades.delete(client); upstream.destroy(); }); upstream.on('close', () => { sockets.delete(upstream); client.destroy(); });
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  return { ...control, control, url: `http://127.0.0.1:${server.address().port}`,
    pauseHub() { control.hubOffline = true; for (const s of upgrades) s.destroy(); },
    restoreHub() { control.hubOffline = false; },
    releaseStock() { held.splice(0).forEach(deliver => deliver()); control.stockHeld = false; },
    async close() { for (const s of sockets) s.destroy(); server.closeAllConnections(); await new Promise(r => server.close(r)); },
  };
}
