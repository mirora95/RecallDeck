# Published web offline use and updates

Open the published app online first. Keep the connection until its service worker has installed the complete published asset cache. Opening a new browser/profile offline before installation cannot load the app. Development builds intentionally do not provide offline caching. Production hosting needs HTTPS (HTTP loopback is allowed for local checks), correct asset MIME types and the app at the origin root.

After installation, the same browser/profile and origin can reload Decks, add/edit cards, reveal/rate due reviews and save their changes offline. Local JSON backup export also works offline. Reconnecting does not upload or synchronize study data. Unsupported service workers and rejected registration show a visible explanation; the app does not use the browser's online flag as proof that offline use is ready.

The service worker caches only assets listed in the content-hashed published manifest. Its existing include filter covers the actual framework modules and host assets. It never adds fetched files or learner exports to its cache. Decks, cards and reviews remain in IndexedDB; exported backups are browser downloads outside that cache. There are no accounts, telemetry or cloud backup, and local storage is not promised to be encrypted. Browser/site storage and caches may be cleared or evicted; private browsing can be temporary or unavailable. A different origin, browser or profile has separate data. Use **Backup → Export current data**, check that the download saved, and keep it outside the app.

## When an update is ready

A new worker downloads a complete version in the background, then waits while an old RecallDeck tab still uses the app. A polite status above the app says: **Save your work, then close all RecallDeck tabs and reopen the app to update.** All existing tabs keep their current version and input. Save every open form before closing; unsaved input does not survive a user closing a tab. A normal reload can keep the old version while other tabs are open.

There is no immediate activation, update button or automatic reload. Once every RecallDeck tab is closed, the browser can activate the waiting worker and remove the old app cache. Reopen to use the new version. The update does not reset IndexedDB or replace saved study rows.

## Repeatable published PWA check

From the repository root, use the pinned SDK/Node versions and installed headless Chromium described in [testing](testing.md):

~~~sh
npm ci --ignore-scripts
npm test
dotnet publish src/RecallDeck.Web -c Release -o artifacts/pwa
dotnet publish src/RecallDeck.Web -c Release -o artifacts/pwa-update -p:Version=1.0.0-pwa-update
npm run test:pwa -- artifacts/pwa/wwwroot artifacts/pwa-update/wwwroot
~~~

Use the [guarded headless browser setup](testing.md#headless-browser-setup) for PowerShell/POSIX commands if installation is needed; it sets `PLAYWRIGHT_SKIP_BROWSER_GC=1` to preserve existing caches. `RECALLDECK_CHROMIUM_EXECUTABLE` can select an existing compatible headless executable. This uses no user browser/profile, desktop input, external origin or Android device.

The driver accepts two distinct publish roots contained in this repository's ignored artifacts directory, checks real asset manifests with different versions, then creates its own loopback server on a free port. It switches the server's published root between the two builds, leaving source and artifacts unchanged. Browser contexts, downloads and the owned server close after the run; results are JSON text and no screenshots are created.

Assertions cover a fresh offline browser's initial-load failure; installed worker/controller; offline reload and deck/card creation; reveal/rate and persisted due queue; offline backup download; reconnection without row changes; exact manifest-only cache keys and an uncached downloaded JSON fixture. At 360 CSS pixels, two open tabs retain exact unsaved card/deck fields and receive the save/close warning, with no navigation. Closing just one tab keeps the update waiting. The test saves a card, closes all owned tabs, observes natural activation, then reopens offline and compares saved decks/cards/reviews exactly. Only synthetic data in fresh browser contexts is used.

CI publishes both builds and runs this driver after the existing web journey. Registration unit tests cover unsupported browsers, rejection, first installation and waiting updates; they also forbid forced activation/reload. This browser coverage does not prove physical-device, Android-runtime, storage eviction, real disk-quota failure, every browser engine or full lifecycle behavior. Android compilation and native runtime checks remain separate.
