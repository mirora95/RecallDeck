# Sprint 1 persistence journey

Issue [#16](https://github.com/mirora95/RecallDeck/issues/16) checks the existing deck/card UI on both platforms. All added code is test infrastructure. The shipping UI, storage adapter and mobile registration are unchanged.

## Automated checks

Use .NET SDK 10.0.401 (`global.json`), Node 22 or newer and Python 3.11 or newer. Android snapshot reads require Python SQLite `deserialize` support.

```sh
dotnet test tests/RecallDeck.Core.Tests -c Release
npm ci --ignore-scripts
npm test
python scripts/serve-published.py --self-test
node --check scripts/check-card-journey.mjs
dotnet build tests/RecallDeck.Android.UiSmoke -c Debug -p:AndroidBuildApplicationPackage=false
```

`npm test` retains the storage suite and adds two argument/refusal tests; it launches no browsers. `playwright-core` 1.65.1 is exact-pinned, test-only and Apache-2.0 licensed. Playwright is not included in the shipped app.

## Published web journey

Install the managed headless shell through the locally pinned CLI, then publish the actual web host:

```sh
npx --no-install playwright-core install --with-deps chromium --only-shell
dotnet publish src/RecallDeck.Web -c Release -o artifacts/web
python scripts/serve-published.py artifacts/web/wwwroot --port 5189
```

In a second terminal:

```sh
npm run test:ui -- web http://127.0.0.1:5189
```

The static server binds only `127.0.0.1`, supplies the WebAssembly MIME type and handles SPA routes while preserving missing-asset errors. Stop only this owned server. CI starts it in the background and traps its exact PID for cleanup even after a failed journey.

The driver accepts only an HTTP loopback origin, blocks other origins, always launches a fresh headless context and never opens a user profile. `RECALLDECK_CHROMIUM_EXECUTABLE` can select an installed compatible headless executable; otherwise Playwright uses its managed browser. There is no headed flag. Service workers are blocked in this isolated context; this check does not verify offline support or PWA updates.

A controlled failure overrides `IDBObjectStore.add` only for cards in the owned fixture deck. It throws a `QuotaExceededError`, checks the friendly UI error, exact retained input and zero committed cards, restores the original method in `finally`, and retries once. This is injected quota failure, not actual disk exhaustion or a storage-permission test.

## Isolated Android UI failure host

`tests/RecallDeck.Android.UiSmoke` runs production `RecallDeck.UI.App`, real `SqliteStudyStore` and the same application services/clock in its own test package, `io.github.mirora95.recalldeck.uismoke`. Its database is app-private `files/ui-smoke.db3`. The driver has no package override.

The test-only `UiFailureStudyStore` wrapper recognizes `[RecallDeck synthetic failed save] ` at the start of the first created card front per process. It initializes the schema through the real adapter, installs a SQLite `BEFORE INSERT` trigger restricted to that validated card GUID, and uses `RAISE(ABORT, 'synthetic UI failure')`. It removes the trigger in `finally` before retry. Ordinary inputs delegate unchanged. The shipping app never references this wrapper; no production test API or new `InternalsVisibleTo` declaration is added.

Two real desktop-SQLite tests prove unchanged data after failure, trigger removal, exactly one successful retry and ordinary-input behavior. Native execution is a separate gate. The host enables developer tools for Playwright's experimental Android WebView API, uses the shipping MAUI 10.0.20 packages and links original icon/splash assets. It adds no broad permissions, accounts, telemetry or signing secrets.

Build/package for the already running authorized x86_64 emulator:

```sh
dotnet build tests/RecallDeck.Android.UiSmoke -c Debug -p:RuntimeIdentifier=android-x64 -p:EmbedAssembliesIntoApk=true -p:AndroidPackageFormats=apk
```

Install only that built test APK using `adb -s SERIAL install -r APK_PATH`, preserving existing test data. Check the existing AVD name before opening the dedicated app:

```sh
adb -s emulator-5554 emu avd name
adb -s emulator-5554 shell am start -n io.github.mirora95.recalldeck.uismoke/io.github.mirora95.recalldeck.uismoke.MainActivity
npm run test:ui -- android --serial emulator-5554 --avd EXPECTED_AVD_NAME
```

The driver requires that this app is already open. It never launches an AVD, resets data, changes permissions, touches other apps or sends Windows mouse/keyboard/focus input. `RECALLDECK_ADB` selects the installed ADB executable; `RECALLDECK_PYTHON` selects Python. Snapshots read only the fixed test package's database via `adb exec-out run-as`, then parse SQLite in memory without writing a copy. The serial, AVD, package, fixture and pre-existing records must match expectations. Actual rendered UI assertions accompany the persistence reads.

Native checks cover friendly failed-save UI, exact retained input, zero persisted rows, successful retry and cold restarts with distinct package PIDs. Force-stop/relaunch is limited to the dedicated package. This host adds failure-path integration evidence; it remains separate from normal-flow evidence against the shipping APK.

## Assertions and ownership

Both modes create a unique deck and capture exact persisted deck/card IDs. They exercise both 4000-UTF-16-unit Unicode/HTML-like fields, escaping/no script execution, edit/reload or native restart, canceled edits/card/deck deletion, confirmed card deletion, populated deck deletion, wrapping/reachable controls and explicit Enter/Space activation. They await completed business state and expected content/form mode; an existing DOM node alone is insufficient while Blazor/MAUI work completes.

Normal cleanup uses the confirmed production UI and only exact owned records. Every pre-existing deck/card/review record is compared before destructive steps and at the end. On failure, the driver stops and reports IDs for investigation, preserving uncertain data. Recover only after checking the ID/title, then use normal UI confirmation. Do not clear app/browser data to hide a failed run.

JSON results go to stdout with actual Node/Playwright/browser versions, checks, IDs and native PIDs. Save reports outside tracked source. No screenshots/photos are created. Remove any separate temporary visual-validation images after inspection and before sending/pushing; retain results as text and preserve original assets.

## Runtime record and limits

The parent session supplied this local toolchain on 2026-11-27: .NET SDK 10.0.401; Node 24.14.1; Playwright Core 1.65.1; installed Chromium headless 151.0.7922.34; JDK 21.0.11; ADB 1.0.41; Android 17/API 37 x86_64. Package-managed Chromium is 159.0.8154.6; CI reports the actually launched version in JSON. Installing a browser does not prove it ran.

At the infrastructure checkpoint, Release .NET tests passed 360/360, Node tests passed 63/63 and the isolated Android host compiled with zero warnings/errors. The parent owns final published-web, Android-runtime and independent-review evidence at the final source hash. Compilation never substitutes for emulator/device execution. Shipping normal-flow evidence and the isolated failure host are distinct.

No physical-device, TalkBack, whole-lifecycle, offline or PWA-update coverage is claimed. Existing Android safe-area issue [#29](https://github.com/mirora95/RecallDeck/issues/29) remains separate. The GitHub token lacks Projects scopes: issue #16's intended In Progress move was not saved and must not be described as confirmed board status.
