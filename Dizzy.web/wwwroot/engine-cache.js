// GitHub Pages serves a versioned engine larger than Chrome's usual HTTP cache entry limit.
const scope = self.registration.scope;
const cacheName = 'dizzy-engine:' + scope;

self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('fetch', event => {
    const request = event.request;
    if (request.method !== 'GET' || !request.url.startsWith(scope) ||
        !/^builds\/\d+-\d+\/godot\.wasm$/.test(request.url.slice(scope.length)) ||
        request.headers.has('Range')) return;

    event.respondWith((async () => {
        let cache;
        try {
            cache = await caches.open(cacheName);
            const hit = await cache.match(request);
            if (hit) return hit;
        } catch {
            // Storage can be unavailable in a private or quota-limited session.
        }
        const response = await fetch(request);
        if (cache && response.status === 200 && !response.redirected) {
            const copy = response.clone();
            event.waitUntil((async () => {
                const previous = await cache.keys();
                await cache.put(request, copy);
                await Promise.all(previous.filter(key => key.url !== request.url).map(key => cache.delete(key)));
            })().catch(() => {}));
        }
        return response;
    })());
});
