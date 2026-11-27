# Android SQLite storage

The Android host registers `SqliteStudyStore` with `IStudyStore` and uses `Path.Combine(FileSystem.AppDataDirectory, "study.db3")`. The storage adapter is a separate net10.0 library so the same implementation can run against temporary files in desktop contract tests. The Web project does not reference it. Storage registration does not yet connect deck/card/review feature screens.

The selected stable library is **sqlite-net-pcl 1.11.285** (MIT), named in [Microsoft's MAUI SQLite guidance](https://learn.microsoft.com/dotnet/maui/data-cloud/database-sqlite?view=net-maui-10.0). Its current dependencies are SQLitePCLRaw core/provider 3.0.3 (Apache-2.0) and SourceGear.sqlite3 3.53.3 (SQLite public domain). Package versions and license files were checked against the restored NuGet packages. See the [maintainer repository](https://github.com/praeclarum/sqlite-net) and [package metadata](https://www.nuget.org/packages/sqlite-net-pcl/1.11.285).

Each operation runs off the UI thread, opens its own connection and closes it before returning. Calls on one adapter instance are serialized. SQLite transactions protect snapshot reads and writes; foreign-key cascades remove related cards/history, and unique review/session IDs prevent duplicate ratings. Core constructors validate rehydrated values; instants use UTC ticks. A multi-entity write reports success only after commit. Cancellation is checked before work and immediately before commit; a completed commit cannot be undone by a later cancellation. Lock/contention failures are surfaced for retry, not reported as successful writes.

The current schema is version 2. An empty, unversioned database first receives the original version-1 tables and indexes, then the version-1-to-2 migration, within the same transaction as the requested operation. Existing version-1 databases are validated for integer card review levels 0–5 and review ratings 0–1 (Again/Remembered), then receive four INSERT/UPDATE validation triggers. The version becomes 2 only after every migration step succeeds. No tables are reconstructed, and existing objects, IDs, Unicode text, UTC schedules and history remain intact.

Invalid existing levels/ratings or a failed DDL step roll back every migration change, including earlier triggers and the schema version. Preserve the database, correct the underlying issue using the matching app or a verified backup, and retry; do not clear app data. Unknown/newer versions (3 and later), populated unversioned databases, corrupt files and failed writes are preserved and fail visibly. Older version-1 clients refuse version-2 data rather than resetting it, so use the updated app. There is no encryption promise or cloud sync; app-private storage and a successful commit are not a backup guarantee.

## Automated verification

```sh
dotnet test tests/RecallDeck.Core.Tests -c Release
```

`SqliteStudyStoreTests` derives from the common storage contract and uses unique temporary files. Additional cases seed the original version-1 DDL and check exact fields after upgrade and reopening at version 2, retention of unrelated schema objects, byte-for-byte preservation after invalid prior values or a collision on the last migration trigger, retry after removing the synthetic collision, numeric INSERT/UPDATE enforcement, unknown/corrupt database preservation and a real SQLite trigger that rejects the second review write. Both the schedule and history must roll back, then the same review must succeed on retry. A one-shot internal test seam exercises failures/cancellation immediately before commit; it is not a public application option.

## Android runtime smoke

`tests/RecallDeck.Android.StorageSmoke` is a separate **test-only** Android app with its own application ID and app-private synthetic database. It is not referenced by the shipping app. It runs the same two-phase fixture as the desktop test; it does not claim to test feature UI, assistive technology, lifecycle editing, real quota/disk exhaustion or a store release. Use an isolated disposable emulator/device and a matching x86_64 system image for this test project's `android-x64` runtime.

Build with the pinned .NET SDK and installed Android SDK/JDK (set their documented MSBuild properties when discovery is unavailable):

```sh
dotnet build tests/RecallDeck.Android.StorageSmoke -c Debug
```

Install its generated **Signed.apk** from `tests/RecallDeck.Android.StorageSmoke/bin/Debug/net10.0-android/android-x64/`. With the isolated emulator serial substituted for `emulator-5556`:

```sh
adb -s emulator-5556 install -r <path-to-Signed.apk>
adb -s emulator-5556 shell am start -n io.github.mirora95.recalldeck.storagesmoke/.MainActivity
adb -s emulator-5556 shell run-as io.github.mirora95.recalldeck.storagesmoke cat files/result.txt
adb -s emulator-5556 shell am force-stop io.github.mirora95.recalldeck.storagesmoke
adb -s emulator-5556 shell am start -n io.github.mirora95.recalldeck.storagesmoke/.MainActivity
adb -s emulator-5556 shell run-as io.github.mirora95.recalldeck.storagesmoke cat files/result.txt
```

Wait for phase 1 PASS before the force-stop and phase 2 PASS after relaunch. Phase 1 seeds an explicit version-1 database with Unicode deck/card/review data, upgrades it to version 2 through the adapter, and checks every field after reopening. Phase 2 checks it after process restart, injects a failed native SQL review write, checks rollback, retries, verifies the delete cascade and removes only the guarded synthetic database. A failed phase preserves its data for investigation. Retain results in text and remove temporary validation media.

The version-1-to-2 two-phase flow passed on 2026-11-27 on the existing RecallDeck Android 17/API 37 x86_64 emulator. Phase 1 upgraded the populated version-1 fixture and checked its fields; phase 2 passed after a force-stop and relaunch with a distinct process ID (8904 then 8963). Rollback, retry, cascade and guarded synthetic cleanup passed. The same fixture is exercised by the Windows desktop tests. These results cover the adapter in a Debug test app, not physical devices, Release trimming or the future MAUI learner journeys.
