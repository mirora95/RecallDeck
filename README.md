# RecallDeck

Build a habit of remembering what you learn, one short review at a time.

RecallDeck is an offline-first study app planned for **Android (.NET MAUI Blazor Hybrid)** and the **web (Blazor WebAssembly PWA)**. Create decks, write question-and-answer cards, and review cards when they become due.

**Status: shared foundation.** Android and web hosts use the same informational landing page. Decks, reviews and persistence are not implemented yet. Follow the [issues](https://github.com/mirora95/RecallDeck/issues) and [roadmap](docs/roadmap.md) for progress. There is no public deployment or release download yet.

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
