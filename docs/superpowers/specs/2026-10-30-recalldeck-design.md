# RecallDeck product design

## Purpose and scope

Help self-directed learners turn short notes into an achievable daily review queue. The first release serves one learner per installation, in English, with no account or server. Android and browser versions share domain behavior and Razor UI. The owner delegated the project idea, architecture and delivery process on 2026-10-30.

The primary journey is: create a deck, add a question and answer, open the due queue, reveal the answer, rate recall, and return later when the card is due. A learner can export and restore all data.

## Approach and tradeoffs

Use .NET 10, MAUI Blazor Hybrid for Android, standalone Blazor WebAssembly PWA for web, and a shared Razor class library. This supports the requested Android and web clients while sharing UI and logic. Separate native XAML and web interfaces would increase UI duplication. A server-rendered web host would add network and hosting dependencies before sync is needed.

Platform storage is intentionally different: SQLite on Android and IndexedDB in the browser. Share storage contracts and behavioral test cases, not database technology. Select maintained stable packages at implementation time and document their versions and licenses. Do not add a generic repository framework, CQRS, microservices or a backend for this release.

## Domain rules

- Stable UUID identifiers for decks, cards and review events. Plain text only; render user text escaped. No HTML or Markdown execution.
- Deck title: trim, require 1–80 characters. Optional description: at most 500 characters. Card front and back: trim, each 1–4000 characters. Preserve Unicode and internal whitespace.
- A new card is due immediately. At reveal, offer Again or Remembered. A card cannot be rated twice for the same session item.
- Store UTC instants, inject the clock, and use fixed elapsed-day intervals rather than local-midnight calculations. Due means `DueAt <= now`. Queue order is due instant then stable card ID.
- Review levels 0–5. New cards start at 0. Remembered advances one level, capped at 5; levels 1–5 schedule after 1, 3, 7, 14 and 30 days respectively. Again resets to level 0 and schedules after 10 minutes. This is a simple product rule, not a claim of scientific optimization.
- Persist the card's new schedule and its review event atomically. A failed write leaves both unchanged and allows a safe retry. Successful reviews disappear from the current session; no surprise card insertion into an active queue.
- Editing card content preserves review history and scheduling. Deleting a deck requires explicit confirmation showing its card count and removes its cards and history atomically. Cancel performs no write.

## Storage and backup

All learner data stays local. No telemetry or external AI calls. Browser data is origin-specific and subject to clearing/eviction; communicate this and provide backup export. Do not promise encryption or secure storage without an implemented threat model.

Versioned JSON exports include decks, cards, schedule state and review history. Import is replace-all in v1, with preview counts and explicit confirmation. Validate schema version, 10 MiB size limit, field limits, identifiers, references, level bounds and timestamps before writing. Reject duplicate IDs, unknown versions and malformed input. Failed validation or persistence preserves all current data. Export before restore is offered. Storage upgrades preserve existing data and fail visibly without silently resetting it.

## UI and platform behavior

Shared routes cover home/due summary, decks, deck detail/card editing, review, progress and backup. Use semantic controls, visible focus, labels, keyboard navigation, sufficient contrast, reduced-motion support and layouts down to 360 CSS pixels. Rendering long card content must not hide review controls. Empty, loading, storage failure and quota failure states provide specific recovery actions and never claim a failed save succeeded.

The Android host targets Android only for v1. Handle back navigation, keyboard, safe areas and suspend/resume. Web offline support applies after a successful initial load and service-worker installation; verify the published build. A PWA update must not discard unsaved edits without warning. No store release, signing-key setup or paid hosting is authorized by routine delivery.

## Verification and release

Unit tests cover boundaries, fixed-clock scheduling, queue ordering and failed writes. Storage contract tests cover CRUD, transactions, schema upgrades and persistence across reopening. Shared UI tests cover meaningful interactions, not copied implementation details. Browser smoke checks include keyboard/mobile-width flows, reload and offline published assets. Android build and emulator/device smoke evidence are separate; never infer runtime correctness from compilation.

v0.1 is releasable only after both hosts build, automated checks pass, full create→review→restart→backup→restore flows pass on each platform, and known limits are documented. Two-week sprint dates are planning windows, not a promise to release by a fixed date.

## Sources

- [Microsoft: share Razor assets across native and web clients](https://learn.microsoft.com/aspnet/core/blazor/hybrid/class-libraries?view=aspnetcore-10.0)
- [Microsoft: Blazor WebAssembly PWA](https://learn.microsoft.com/aspnet/core/blazor/progressive-web-app?view=aspnetcore-10.0)
- [Microsoft: local databases in MAUI](https://learn.microsoft.com/dotnet/maui/data-cloud/database-sqlite?view=net-maui-10.0)
