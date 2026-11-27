# Recovery and cross-platform smoke: 2026-11-27

Issue [#31](https://github.com/mirora95/RecallDeck/issues/31) passed the recovery checks below against application commit [`48d55e91d0504fe4afc05aefd3a3fd23cd755078`](https://github.com/mirora95/RecallDeck/commit/48d55e91d0504fe4afc05aefd3a3fd23cd755078). All 157 frozen repository files matched the source used for the previously tested issue #30 artifacts. This documentation change adds no application code; its eventual PR head is a documentation revision, not a newly runtime-tested application revision.

The [sanitized evidence summary](verification/release-smoke-2026-11-27.json) contains artifact/export SHA-256 hashes, counts, process IDs, results and limits. Raw synthetic backups and local machine paths are excluded. One-off orchestration scripts and raw reports stayed outside Git; readers should use the committed commands and UI procedure below, not assume those scripts are available in the repository.

## Observed results

| Check | Result |
| --- | --- |
| Full Release .NET suite / Node suite | 483 passed, 0 failed, 0 skipped / 82 passed, 0 failed |
| Native Chromium IndexedDB contracts | 65 passed; upgrade preservation, failed/canceled migration, replace-all rollback and retry |
| Published C# interop host | Both phases passed across reload; preview/cancel, replacement, exact fields, export and guarded cleanup |
| Published web journey | Created two cards, revealed/rated Again and Remembered, reloaded, downloaded JSON, rejected/canceled imports, rolled back an injected failed restore and retried |
| Android UiSmoke journey | Opened that actual web export through the system picker; restored, added/rated card 3, cold restarted, system-exported, checked rejection/cancellation and real SQLite failure/retry |
| Return to fresh web context | Restored the actual Android export; all three cards/events matched, including original imported rows |
| Native StorageSmoke | Both phases passed with distinct PIDs 2433 / 2513; populated v1 upgraded to v2, exact fields survived restart, review/restore rollback/cancel/retry, empty restore and cascade passed |

Runtime: .NET SDK 10.0.401, Node 24.14.1, Playwright Core 1.65.1, Chromium headless 151.0.7922.34 and Android 17/API 37 x86_64 emulator. UiSmoke cold restart changed PID 1677 to 2794. Comparisons sorted records by stable ID and checked every ID/reference/session ID, Unicode/plain text, level, rating and UTC due/review instant. Seven-digit fractions were converted to `BigInt` 100 ns ticks separately from whole seconds, avoiding JavaScript millisecond truncation.

| Actual transferred JSON | Bytes | Decks / cards / reviews | SHA-256 |
| --- | --- | --- | --- |
| Web → Android | 1259 | 1 / 2 / 2 | `684b194917b144eea6f5a9459d26768a10dd62508cc1be66178b16cc6d7b2da3` |
| Android → web | 1739 | 1 / 3 / 3 | `4ea615e24c8135ee2bf4f59166a5f06afff4a74431c962cde663083355aeccf9` |

The installed Debug APK hashes matched their built files: shipping `44c4c8d8ac3b5dda0567554aa3aea0c7c9f5a4248b48f5e99f94016b6fe32035`, UiSmoke `4e959d7ad8940a344dccec92349c943cbd89826e69d31d394a13973564d9129e`, StorageSmoke `6b8188c8718cdb6caa05516b742caf8b6cacf804b8c43302126fbd0d7078adf2`. Shipping baseline-preserving UI coverage was recorded under [issue #30](https://github.com/mirora95/RecallDeck/issues/30); destructive recovery ran only in the empty dedicated UiSmoke database. Web/UiSmoke artifacts were reused after source-byte verification; StorageSmoke and the published interop host were rebuilt for these checks.

## Repository commands to reproduce coverage

The fresh application-commit reruns executed `dotnet test tests/RecallDeck.Core.Tests -c Release --no-build --no-restore --verbosity minimal -m:2 -nr:false` (483 passed) and `npm test` (82 passed), using the byte-matched compiled artifacts. The interop publish executed `dotnet publish tests/RecallDeck.Web.Interop -c Release -o artifacts/release/interop -m:2 -nr:false --verbosity minimal`; it passed with the optional `wasm-tools` workload absent. The new StorageSmoke APK build passed with zero warnings/errors using the following command (only local toolchain paths are substituted):

```sh
dotnet build tests/RecallDeck.Android.StorageSmoke -c Debug -m:2 -nr:false -p:RuntimeIdentifier=android-x64 -p:EmbedAssembliesIntoApk=true -p:AndroidUseFastDeployment=false -p:AndroidPackageFormats=apk -p:AndroidSdkDirectory="<ANDROID_SDK>" -p:JavaSdkDirectory="<JDK_21>" --verbosity minimal
```

Use the pinned SDK and [Android toolchain](android-toolchain.md). Run from the repository root; supply local SDK/JDK directory overrides when needed. These are the committed entry points, with portable output paths; the local execution also used machine-specific SDK paths and logging/build-worker switches.

```sh
dotnet test tests/RecallDeck.Core.Tests -c Release
npm ci --ignore-scripts
npm test
dotnet publish src/RecallDeck.Web -c Release -o artifacts/recovery/web
dotnet publish tests/RecallDeck.Web.Interop -c Release -o artifacts/recovery/interop
dotnet build src/RecallDeck.Mobile -c Debug -p:RuntimeIdentifier=android-x64 -p:EmbedAssembliesIntoApk=true -p:AndroidPackageFormats=apk
dotnet build tests/RecallDeck.Android.UiSmoke -c Debug -p:RuntimeIdentifier=android-x64 -p:EmbedAssembliesIntoApk=true -p:AndroidPackageFormats=apk
dotnet build tests/RecallDeck.Android.StorageSmoke -c Debug
python scripts/serve-published.py artifacts/recovery/web/wwwroot --port 5189
```

For real IndexedDB contracts, follow [browser adapter verification](storage-contract.md#browser-adapter-verification): serve the repository on loopback and open `tests/RecallDeck.Web.Tests/browser.html`; require all 65 rows to pass. Serve the newly published interop directory on a separate loopback port using `scripts/serve-published.py`; choose **Run checks**, reload, then **Run checks** again and require `COMPLETE` with no failed row. The [native SQLite instructions](sqlite-storage.md#android-runtime-smoke) give the install/start/result/force-stop commands for StorageSmoke; require phase 1 PASS before restart and phase 2 PASS afterwards. These contract fixtures exercise migration and transaction cancellation independently of the learner UI.

The [committed card driver](testing.md#published-web-journey) adds general CRUD/reload/failure checks; it does not automate this cross-platform backup journey. [PWA](pwa.md), [Android lifecycle](android-lifecycle.md) and [accessibility](accessibility.md) checks have their own scope and evidence.

## Repeat the UI recovery journey

1. Use a fresh browser context on the loopback published app and a verified-empty dedicated `io.github.mirora95.recalldeck.uismoke` installation on the intended emulator. Preserve the shipping app and unrelated records. Build/install the dedicated host as described in [testing](testing.md#isolated-android-ui-failure-host). Record the current commit, runtime and APK hashes before starting.
2. In **Decks**, create one uniquely named synthetic deck and two cards containing Unicode, a line break and literal markup. Begin one front with `[RecallDeck synthetic failed restore] `, including the trailing space. In **Review**, reveal/rate one **Again** and the other **Remembered**. Reload, then **Backup → Export current data** and retain the actual downloaded bytes. Expect one deck, two cards and two events. Capture IDs and UTC ticks from this export.
3. Create an extra owned deck. Select malformed JSON and verify rejection without a preview or data changes. Select the real export, check counts, then **Cancel restore** and verify the extra deck remains. In the isolated web context, inject the scoped IndexedDB fault below, confirm restore and check the friendly error, retained preview and unchanged prior rows. Remove the fault, retry once, reload and compare all exported fields. Only the original exported deck should remain.
4. Transfer the unchanged web export to the emulator's Downloads folder and use **Choose backup file** in UiSmoke. Check its preview and confirm. The test-only marker installs a real SQLite Cards INSERT trigger after destructive restore work: the first confirmation fails, leaves the empty baseline intact and keeps the preview. Retry explicitly and compare all restored fields. Complete this step within ten minutes of the web Again rating if expecting both imported cards to remain future-due.
5. Add a third card through Android UI and reveal/rate it **Again**. Verify the original imported deck/cards/events remain identical. Force-stop only UiSmoke, relaunch and require a different PID with identical saved rows. In **Backup**, export through the system save picker. Verify the Downloads breadcrumb, use a unique filename and wait for **Backup saved**. Retain these actual bytes; expect one deck, three cards and three events.
6. Create an extra owned deck on Android. Check system picker cancellation, malformed JSON rejection and valid-preview **Cancel restore** preserve every prior row. Reopen the real Android export, confirm the first restore failure after the cold restart and compare unchanged rows; retry the retained preview and require an exact restored match. The marker fails once per process, so the restart supplies this second controlled failure.
7. In another fresh web context, import the actual Android export. Compare all three cards/events and original imported rows, then repeat the web reject/cancel/failure/retry/reload checks. Compare by record ID rather than collection order; preserve all seven fractional timestamp digits.
8. Remove only the captured owned deck through its confirmed delete UI, verify the original empty baselines, then remove only the three unique hash-checked Android documents. Stop owned servers/contexts. The recorded run completed this cleanup and created no screenshots/photos; only text evidence and synthetic export files were retained outside Git.

### Scoped web restore fault

Use only step 3's empty-origin synthetic fixture. Copy an incoming card ID from its exported JSON, select that file and wait for its preview. In that context's developer console:

```js
const incomingCardId = 'COPY_SYNTHETIC_EXPORTED_CARD_ID';
const originalAdd = IDBObjectStore.prototype.add;
window.restoreOwnedAdd = () => { IDBObjectStore.prototype.add = originalAdd; };
IDBObjectStore.prototype.add = function (value, ...args) {
  if (this.name === 'cards' && value.id === incomingCardId)
    throw new DOMException('Owned injected abort', 'QuotaExceededError');
  return originalAdd.call(this, value, ...args);
};
```

Confirm restore, verify all prior records survived, then run `window.restoreOwnedAdd()` before retry and in cleanup if any assertion fails. This injects a request-time quota error after clear/deck requests; it tests transaction rollback, not actual disk exhaustion. The native equivalent uses the committed [test-only SQLite wrapper](../tests/RecallDeck.Storage.Checks/UiFailureStudyStore.cs), never the shipping registration.

## Limits

No application defect remained in these exercised recovery paths. This record does not claim physical-device, Release trimming/signing, power-loss, eviction, real disk-exhaustion or other browser-engine coverage. Native destructive recovery used a Debug test package linked to production UI/file transport/storage; it does not establish destructive restore acceptance against a learner's shipping database. No public deployment, app-store release or new accessibility certification occurred. Documentation review, CI, merge and issue/project reconciliation remain separate delivery gates.
