# Android SQLite storage

The Android host registers `SqliteStudyStore` with `IStudyStore` and uses `Path.Combine(FileSystem.AppDataDirectory, "study.db3")`. The storage adapter is a separate net10.0 library so the same implementation can run against temporary files in desktop contract tests. The Web project does not reference it. Storage registration does not yet connect deck/card/review feature screens.

The selected stable library is **sqlite-net-pcl 1.11.285** (MIT), named in [Microsoft's MAUI SQLite guidance](https://learn.microsoft.com/dotnet/maui/data-cloud/database-sqlite?view=net-maui-10.0). Its current dependencies are SQLitePCLRaw core/provider 3.0.3 (Apache-2.0) and SourceGear.sqlite3 3.53.3 (SQLite public domain). Package versions and license files were checked against the restored NuGet packages. See the [maintainer repository](https://github.com/praeclarum/sqlite-net) and [package metadata](https://www.nuget.org/packages/sqlite-net-pcl/1.11.285).

Each operation runs off the UI thread, opens its own connection and closes it before returning. Calls on one adapter instance are serialized. SQLite transactions protect snapshot reads and writes; foreign-key cascades remove related cards/history, and unique review/session IDs prevent duplicate ratings. Core constructors validate rehydrated values; instants use UTC ticks. A multi-entity write reports success only after commit. Cancellation is checked before work and immediately before commit; a completed commit cannot be undone by a later cancellation. Lock/contention failures are surfaced for retry, not reported as successful writes.

Only an empty, unversioned database is initialized as schema 1. Unknown/newer versions, an unversioned database with existing tables, corrupt files and failed writes are preserved and fail visibly. Existing schema-1 tables are never dropped/recreated on initialization failure. Upgrades between schema versions remain issue #15. There is no encryption promise or cloud sync; app-private storage is not a backup.

## Automated verification

```sh
dotnet test tests/RecallDeck.Core.Tests -c Release
```

`SqliteStudyStoreTests` derives from the common storage contract and uses unique temporary files. Additional cases check reopen/Unicode/UTC values, unknown/corrupt database preservation and a real SQLite trigger that rejects the second review write. Both the schedule and history must roll back, then the same review must succeed on retry. A one-shot internal test seam exercises failures/cancellation immediately before commit; it is not a public application option.

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

Wait for phase 1 PASS before the force-stop and phase 2 PASS after relaunch. Phase 1 writes and reopens Unicode deck/card/review data. Phase 2 checks it after process restart, injects a failed native SQL review write, checks rollback, retries, verifies the delete cascade and removes only the guarded synthetic database. A failed phase preserves its data for investigation. Retain results in text and remove temporary validation media.

This synthetic two-phase flow passed on 2026-11-05 on an isolated Android 17/API 37 x86_64 emulator with 16 KiB memory pages; distinct process IDs confirmed the restart. Desktop contract tests passed on Windows. These results cover the adapter in a Debug test app, not physical devices, Release trimming or the future MAUI learner journeys.
