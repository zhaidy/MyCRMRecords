/* sw.js: what makes MyCRMRecords installable as an app on a phone or computer (manifest.json, the Install app button in
   common.js). It keeps no company data: every page and every api.ashx answer comes from the server as before. Only the
   page shown when the server cannot be reached is kept, so the installed app says so instead of a browser error. */
'use strict';
const CACHE = 'mycrmrecords-offline-1';
const OFFLINE = 'offline.html';

self.addEventListener('install', event => {
  event.waitUntil(caches.open(CACHE).then(cache => cache.add(new Request(OFFLINE, {cache: 'reload'}))).then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
  event.waitUntil(caches.keys()
    .then(keys => Promise.all(keys.filter(key => key !== CACHE).map(key => caches.delete(key))))
    .then(() => self.clients.claim()));
});

// Pages: from the server; when it cannot be reached, the offline page. Everything else is left to the browser.
self.addEventListener('fetch', event => {
  if (event.request.mode !== 'navigate') return;
  event.respondWith(fetch(event.request).catch(() => caches.match(OFFLINE)));
});
