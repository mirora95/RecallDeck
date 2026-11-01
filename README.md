# RecallDeck

Build a habit of remembering what you learn, one short review at a time.

RecallDeck is an offline-first study app planned for **Android (.NET MAUI Blazor Hybrid)** and the **web (Blazor WebAssembly PWA)**. Create decks, write question-and-answer cards, and review cards when they become due.

**Status: shared foundation and tested Core operations.** Android and web hosts use the same informational landing page. Core validation, UTC scheduling, deck operations and the storage contract are implemented; deck/review screens and platform persistence are not available yet. Follow the [issues](https://github.com/mirora95/RecallDeck/issues) and [roadmap](docs/roadmap.md) for progress. There is no public deployment or release download yet.

## First release

- Create and organize decks and plain-text cards.
- Reveal an answer before choosing **Again** or **Remembered**.
- Use a transparent, deterministic review schedule.
- Keep learning data on the device, with versioned JSON backup and restore.
- Use the same accessible, responsive interface on Android and in a browser.

Accounts, cloud sync, AI-generated cards and paid integrations are outside the first release. Browser storage may be cleared or evicted; exportable backups are part of the release criteria.

## Architecture

| Project | Responsibility |
| --- | --- |
| `RecallDeck.Core` | Models, validation, review scheduling and application operations |
| `RecallDeck.UI` | Shared Razor components and styles |
| `RecallDeck.Web` | Standalone Blazor WebAssembly PWA with IndexedDB storage |
| `RecallDeck.Mobile` | Android MAUI Blazor Hybrid host with SQLite storage |
| `RecallDeck.Core.Tests` | Deterministic domain and application tests |

Both hosts reference the same UI assembly; UI references Core, and Core has no platform dependencies. See the [product design](docs/superpowers/specs/2026-10-30-recalldeck-design.md).

## Core rules

Immutable decks/cards retain explicit nonempty UUIDs. Required title/front/back text is trimmed; internal Unicode, whitespace and literal markup are preserved. Limits use UTF-16 string length: title 80, description 500, each card side 4000. Description is optional and retained unchanged. Card models carry a deck ID; verifying that a stored deck exists belongs to application operations.

`ReviewScheduler` takes a `TimeProvider`. New cards are due immediately at level 0. Remembered advances to levels 1–5 with 1/3/7/14/30 elapsed days from the review instant; level 5 stays capped. Again resets to level 0 and ten minutes later. Due instants are UTC; scheduling returns a new value without changing the old card. Platform review persistence and the session flow remain later tasks.

`DeckService` creates, renames and lists decks through `IStudyStore`, retaining validation field names and propagating failed saves. The [storage contract](docs/storage-contract.md) defines immutable snapshots, atomic writes and reusable adapter tests. Its current in-memory implementation is test-only; it does not provide shipping persistence.

## Run and verify

Install the .NET **10.0.401 SDK** pinned in `global.json`. From the repository root:

```sh
dotnet test tests/RecallDeck.Core.Tests -c Release
dotnet build src/RecallDeck.Web -c Release
dotnet publish src/RecallDeck.Web -c Release -o artifacts/web
dotnet run --project src/RecallDeck.Web --no-launch-profile --urls http://127.0.0.1:5189
```

Open `http://127.0.0.1:5189` for the development host. The published PWA files are in `artifacts/web/wwwroot`; use a static server with correct WebAssembly MIME types to test them. Service-worker caching is enabled only in published builds. Full offline user flows are a later milestone, not a verified feature of this scaffold.

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
