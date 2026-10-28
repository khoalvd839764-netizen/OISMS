// Service Worker for OISMS Web POS (PWA)
// Namespace: 'oisms-pos-' ensures isolation from other applications on the same origin
const CACHE_NAMESPACE = 'oisms-pos-';
const CACHE_VERSION = 'v1.0';
const CACHE_NAME = `${CACHE_NAMESPACE}${CACHE_VERSION}`;

// Essential shell assets to precache on install
const PRECACHE_ASSETS = [
  '/',
  '/index.html',
  '/manifest.webmanifest',
  '/favicon.svg',
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => {
      return cache.addAll(PRECACHE_ASSETS);
    })
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((keys) => {
      return Promise.all(
        keys.map((key) => {
          // Strictly delete ONLY outdated caches belonging to the POS namespace.
          // Preserves caches of other apps on the same origin.
          if (key.startsWith(CACHE_NAMESPACE) && key !== CACHE_NAME) {
            return caches.delete(key);
          }
        })
      );
    })
  );
  self.clients.claim();
});

self.addEventListener('fetch', (event) => {
  const request = event.request;

  // 1. Chỉ can thiệp các request GET.
  // Tuyệt đối không can thiệp hoặc fallback HTML cho POST, PUT, DELETE (vd: checkout POST).
  if (request.method !== 'GET') {
    return;
  }

  const url = new URL(request.url);

  // 2. Không can thiệp hoặc cache dữ liệu API, Auth, SignalR Hub negotiate & WebSocket/SSE
  if (
    url.pathname.startsWith('/api/') ||
    url.pathname.startsWith('/hubs/') ||
    url.pathname.startsWith('/swagger') ||
    url.pathname.startsWith('/health')
  ) {
    return; // Pass directly to network
  }

  // 3. Với navigation request (load trang HTML):
  if (request.mode === 'navigate') {
    event.respondWith(
      fetch(request).catch(() => {
        return caches.match('/index.html');
      })
    );
    return;
  }

  // 4. Với static assets (JS, CSS, hình ảnh, SVG, fonts):
  // Cache-first với network fallback, lưu asset mới vào cache
  event.respondWith(
    caches.match(request).then((cachedResponse) => {
      if (cachedResponse) {
        return cachedResponse;
      }
      return fetch(request).then((networkResponse) => {
        // Chỉ cache response hợp lệ (status 200, GET, cùng origin hoặc static CDN)
        if (
          networkResponse &&
          networkResponse.status === 200 &&
          (url.origin === self.location.origin || url.hostname.includes('fonts.'))
        ) {
          const responseToCache = networkResponse.clone();
          caches.open(CACHE_NAME).then((cache) => {
            cache.put(request, responseToCache);
          });
        }
        return networkResponse;
      }).catch(() => {
        return cachedResponse;
      });
    })
  );
});

// Lắng nghe message cập nhật SW an toàn không gây mất giỏ / pending
self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'SKIP_WAITING') {
    self.skipWaiting();
  }
});
