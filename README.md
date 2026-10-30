# RecallDeck

Build a habit of remembering what you learn, one short review at a time.

RecallDeck is an offline-first study app planned for **Android (.NET MAUI Blazor Hybrid)** and the **web (Blazor WebAssembly PWA)**. Create decks, write question-and-answer cards, and review cards when they become due.

**Status: planning and repository setup.** The application is not implemented yet. Follow the [issues](https://github.com/mirora95/RecallDeck/issues) and [roadmap](docs/roadmap.md) for progress. No download or working demo is available yet.

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

These are planned project names. Build and run commands will be added when the scaffold is delivered and verified. See the [product design](docs/superpowers/specs/2026-10-30-recalldeck-design.md).

## Delivery

Work follows two-week sprints. Issues carry acceptance criteria, dependencies and verification steps. Changes use small pull requests, automated checks and independent review before a guarded merge. A daily session aims to deliver two or three ready tasks; quality and dependencies determine actual throughput.

- [Roadmap and sprint goals](docs/roadmap.md)
- [Contribution and verification rules](CONTRIBUTING.md)
- [Agent delivery rules](AGENTS.md)
- [Security reporting](SECURITY.md)

## License

[MIT](LICENSE), copyright 2026 Orasta Mirdadoeva.
