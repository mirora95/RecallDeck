# Issue 28: published PWA offline use and safe updates

Scope: only public mirora95/RecallDeck, existing authorized checkout. PR50 is merged; dependency26 is delivered. No public deployment or desktop/user-browser controls.

Goal: after initial successful asset caching, real deck/card/review work and persisted data survive offline route reload and reconnect. A waiting app update warns clearly without discarding unsaved form input.

Smallest approach: retain the template content-hashed manifest cache and default waiting-worker lifecycle; do not add skipWaiting, clients.claim, automatic reload or a forced-update button. Add an accessible host-only update notice when registration.waiting/updatefound indicates a ready update: save work, close all RecallDeck tabs and reopen. Existing tabs keep the previous complete version while open. Catch unsupported/failed registration with honest first-load/offline-unavailable text. No invented online/offline guarantee from navigator.onLine.

Audit actual published manifest/cache asset filters (including framework modules) and cache misses before changing caching. Cache only build-manifest assets; never put selected files, downloads or arbitrary responses into caches. Explain offline is available only after first successful online load and full service-worker installation, HTTPS/loopback, clearing/eviction/origin/profile limits, local data separate from asset cache, exports external.

Tasks:
1. Red meaningful Node tests for registration notices/error/waiting and no forced activation/reload; source wiring + worker cache allowlist behavior as needed. Keep dependencies unchanged.
2. Implement smallest host-only module/UI text; avoid shared UI abstraction/native registration.
3. Add repeatable guarded published PWA test (fresh headless loopback context, service workers allowed) and integrate CI if feasible. Two actual artifact versions, no user profile/public deployment. Initial install/controller, offline reload, offline synthetic deck/card/review, review persistence after reload, reconnect. Serve versionB and force normal registration.update; assert a real waiting worker, visible warning and exact unsaved fields retained. Close all owned pages; reopen same context and verify B active, saved rows preserved. Check cache keys contain build assets only and no downloaded fixture JSON. Clean exact fixture/download/media.
4. Full .NET/Node, web Release publish, relevant Android compile for regression only; no native runtime change claimed. Full independent spec then quality, scoped rendered notice inspection and docs, exact source/head CI/merge guard.

Primary sources checked 2026-11-27:
- https://learn.microsoft.com/aspnet/core/blazor/progressive-web-app?view=aspnetcore-10.0 : published-only offline, cache-first consistent snapshots, waiting version per tab lifetime, initial online requirements.
- https://developer.mozilla.org/en-US/docs/Web/API/ServiceWorkerGlobalScope/skipWaiting : immediate activation bypasses normal waiting; intentionally not used here.

Limits: local headless Chromium evidence only; installability/system home-screen UI and other browsers are not implied. Keep issue29 native release gate separate and Project synchronization pending (no desktop controls).
