# RecallDeck

Build a habit of remembering what you learn, one short review at a time.

RecallDeck is an offline-first study app planned for **Android (.NET MAUI Blazor Hybrid)** and the **web (Blazor WebAssembly PWA)**. Create decks, write question-and-answer cards, and review cards when they become due.

**Status: shared deck/card management, reveal-first reviews and JSON backup/restore with local storage.** Android and web hosts share an accessible shell with Home, Decks, deck detail, Review, Progress and Backup routes. Feature pages clearly say what is not available yet; navigation includes a skip link and visible keyboard focus. Unexpected page errors offer retry without exposing exception details. Core validation, UTC scheduling, deck/card operations and the storage contract are implemented. The shared Decks page lists, creates and renames decks using transactional IndexedDB on the web and app-private SQLite on Android. Open a deck title to create, edit or explicitly confirm deletion of its cards. Deck deletion shows its current card count and atomically removes cards and history. Labeled forms show inline validation, preserve failed input for retry and prevent duplicate submissions while saving. Review opens a captured due queue, reveals each answer only on request, then offers Again or Remembered. Saved ratings leave this session; failed ratings retain the answer for a safe retry. Completion reports reviewed and skipped cards. Backup exports dated versioned JSON, checks selected files up to 10 MiB, previews counts and requires explicit confirmation to replace all current study data. Export is offered before restore; cancel writes nothing and failed restore retains its preview for retry. Follow the [issues](https://github.com/mirora95/RecallDeck/issues) and [roadmap](docs/roadmap.md) for progress. There is no public deployment or release download yet.

## First release

- Create and organize decks and plain-text cards.
- Reveal an answer before choosing **Again** or **Remembered**.
- Use a transparent, deterministic review schedule.
- Keep learning data on the device, with versioned JSON backup and restore.
- Use the same accessible, responsive interface on Android and in a browser.

Accounts, cloud sync, AI-generated cards and paid integrations are outside the first release. Browser storage may be cleared or evicted; exportable backups are part of the release criteria.

Android route Back and unsaved-edit confirmation, keyboard/system safe areas and read-only resume refresh are described in [Android lifecycle](docs/android-lifecycle.md). Active review queues keep their captured cards and revealed/pending state across resume. Emulator runtime evidence remains a separate release gate from compilation.

## Architecture

| Project | Responsibility |
| --- | --- |
| `RecallDeck.Core` | Models, validation, review scheduling and application operations |
| `RecallDeck.Storage.Sqlite` | Transactional SQLite adapter used by Android and storage tests |
| `RecallDeck.UI` | Shared Razor components and styles |
| `RecallDeck.Web` | Standalone Blazor WebAssembly PWA with IndexedDB storage |
| `RecallDeck.Mobile` | Android MAUI Blazor Hybrid host with SQLite storage |
| `RecallDeck.Core.Tests` | Domain, application and shared UI interaction tests |

Both hosts reference the same UI assembly; UI references Core, and Core has no platform dependencies. See the [product design](docs/superpowers/specs/2026-10-30-recalldeck-design.md).

## Core rules

Immutable decks/cards retain explicit nonempty UUIDs. Required title/front/back text is trimmed; internal Unicode, whitespace and literal markup are preserved. Limits use UTF-16 string length: title 80, description 500, each card side 4000. Description is optional and retained unchanged. Card models carry a deck ID; verifying that a stored deck exists belongs to application operations.

`ReviewScheduler` takes a `TimeProvider`. New cards are due immediately at level 0. Remembered advances to levels 1–5 with 1/3/7/14/30 elapsed days from the review instant; level 5 stays capped. Again resets to level 0 and ten minutes later. Due instants are UTC; scheduling returns a new value without changing the old card. Both adapters persist ratings and schedules atomically. The shared review route captures a fixed due queue and uses a stable session-item ID for safe retries; a fresh read reconciles an uncertain commit. Missing cards are skipped without counting them as reviewed. Deck detail links to a review filtered to that deck; the global Review route includes all due cards.

`DeckService` creates, renames and lists decks through `IStudyStore`, reads a deck and its cards from one snapshot, and delegates atomic deletion to the adapter. `CardService` creates cards due immediately and edits text without changing scheduling or history. The [storage contract](docs/storage-contract.md) defines immutable snapshots, atomic writes and adapter checks. The web adapter persists locally in IndexedDB, reports storage/quota failures without resetting data, and finishes writes only after transaction completion. The in-memory implementation remains test-only. The shared deck pages use these services in both hosts; successful saves update the list without a second storage read. All user content renders as escaped plain text; long card text preserves line breaks and wraps.


## Back up and restore

Open **Backup → Export current data** to save a dated `recalldeck-YYYY-MM-DD.json` file (UTC date). It includes decks, cards, schedules and review history. Browser export requests a download; check the browser's downloads because the app cannot confirm a disk save. Android opens the system document save picker and reports success only after the selected URI stream has been written and closed. Canceling that picker saves no backup. Export failures show a retryable error without changing study data.

Choose **Choose backup file** to select a RecallDeck JSON document. The transport reads no more than 10 MiB plus one sentinel byte; the complete versioned content is validated before preview. Review the deck/card/review counts, export current data if needed, then choose **Replace all current data** once to replace every deck, card, schedule and review. A valid empty backup removes all current study data and has an extra warning. **Cancel restore** makes no storage write. A failed restore keeps the checked preview for an explicit retry.

Web data belongs to the current origin and browser profile. Clearing site data or browser eviction can remove it, so keep backups outside the app. Android uses `ACTION_CREATE_DOCUMENT` / `ACTION_OPEN_DOCUMENT` with JSON MIME and user-selected URI streams. No broad media/storage permission, account or external sharing app is required. These workflows are implemented; final browser/device runtime evidence is recorded separately from automated tests and compilation. See the [backup format](docs/backup-format.md).

## Run and verify

The [Sprint 1 web/Android persistence journey](docs/testing.md) provides repeatable headless UI checks, controlled failed-save scenarios and an isolated Android test host.

The [Android SQLite adapter and runtime smoke instructions](docs/sqlite-storage.md) cover native persistence, process restart and rollback. The shipping MAUI host registers the adapter with an app-private database path; the shared deck, card and review pages use this registration. Home due summaries and Progress remain later work. Backup uses browser file selection/download and Android system document save/open; both hosts share validation, preview and confirmed atomic replacement.

Install the .NET **10.0.401 SDK** pinned in `global.json`. From the repository root:

```sh
dotnet test tests/RecallDeck.Core.Tests -c Release
npm ci --ignore-scripts
npm test
dotnet build src/RecallDeck.Web -c Release
dotnet publish src/RecallDeck.Web -c Release -o artifacts/web
dotnet run --project src/RecallDeck.Web --no-launch-profile --urls http://127.0.0.1:5189
```

Open `http://127.0.0.1:5189` for the development host. The published PWA files are in `artifacts/web/wwwroot`; use a static server with correct WebAssembly MIME types to test them. Service-worker caching is enabled only in published builds. After a first online load and successful service-worker installation, the published web app supports offline deck/card management, review and local backup export. A new version waits while any RecallDeck tab is open; the app asks you to save work, close all its tabs and reopen. It never forces an update or reload. See [published PWA behavior and checks](docs/pwa.md) for installation, storage/privacy limits and the repeatable two-version test.

The Node storage suite needs Node 22 or later and uses `fake-indexeddb` 6.2.7 (Apache-2.0), a **test-only** implementation. Native Chrome and real .NET→JavaScript roundtrip/reload checks are described in the [storage verification instructions](docs/storage-contract.md#browser-adapter-verification). No JavaScript runtime dependency is shipped.

For Android, install the `maui-android` workload, JDK 21, Android platform 36 and build-tools 36.0.0. Then compile without packaging or release signing:

```sh
dotnet workload install maui-android --skip-manifest-update
dotnet build src/RecallDeck.Mobile -c Debug -p:AndroidBuildApplicationPackage=false
```

See [Android toolchain and coverage](docs/android-toolchain.md) for SDK/JDK path overrides. CI runs the shared render test, publishes web assets and compiles Android on pull requests and `main`. Compilation does not verify an Android emulator/device; runtime and full user-flow checks remain separate release requirements.

## Delivery

Work follows two-week sprints. Issues carry acceptance criteria, dependencies and verification steps. Changes use small pull requests, automated checks and independent review before a guarded merge. A daily session aims to deliver two or three ready tasks; quality and dependencies determine actual throughput.

- [Roadmap and sprint goals](docs/roadmap.md)
- [Contribution and verification rules](CONTRIBUTING.md)
- [Agent delivery rules](AGENTS.md)
- [Security reporting](SECURITY.md)

## License

[MIT](LICENSE), copyright 2026 Orasta Mirdadoeva.
