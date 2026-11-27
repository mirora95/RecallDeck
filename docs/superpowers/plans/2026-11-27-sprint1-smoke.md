# Sprint 1 cross-platform persistence checks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Keep one implementation worker and obtain spec then quality review.

**Goal:** Complete #16 with repeatable normal and failed-save UI checks, preserving real learner data and Windows mouse/keyboard/window focus.

**Architecture:** Keep all behavior changes in test infrastructure. Add a headless Playwright Core CLI that drives the actual published web app and an isolated Android MAUI test host using the production shared Razor UI and production SQLite adapter. The separate Android test package owns its database; inject one real transactional failure in that test database only. The shipping app and interfaces stay unchanged.

**Tech Stack:** .NET10/MAUI/Blazor, existing SQLite packages, Playwright Core1.65.1 test-only (Apache2.0), Node22+, Python/static loopback host, existing authenticated ADB emulator. No AVD launch or Windows UI controls.

## Task 1: Repeatable UI driver

Files: scripts/check-card-journey.mjs, package.json/package-lock.json, docs/testing.md (or focused equivalent), .github/workflows/build.yml.

- [ ] Add exact-pinned test-only playwright-core1.65.1. Keep npm test for existing storage cases; add an explicit UI command. Never introduce browser automation into the shipped app.
- [ ] Port the proven local work/recalldeck-card-journey.mjs behavior into a portable CLI with safe arguments. Web targets must be loopback only, always headless and a fresh context; optional executable path supports the installed headless shell. Never use a user browser profile or allow a headed flag.
- [ ] Android mode requires an explicit existing serial and expected AVD name; only the dedicated io.github.mirora95.recalldeck.uismoke package is allowed. Never launch an AVD, clear data, reset databases, inspect unrelated apps or change device/browser permissions.
- [ ] Use uniquely named fixtures, capture their exact IDs, and delete only those through normal confirmed UI. Preserve pre-existing identities; stop on unexpected/uncertain ownership. Keep results as text and do not create screenshots by default.
- [ ] Cover create deck/cards, both4000-character Unicode/HTML values with escaping/no execution, edit, reload or native force-stop/relaunch with changed PIDs, canceled edits/card/deck deletion and confirmed cleanup. Check wrapping/reachable controls and explicit keyboard activation. Await business-state completion, not only element existence: save/edit rendering is asynchronous, especially native WebView IPC.
- [ ] In web mode inject a controlled cards-store IndexedDB quota exception in the owned context, assert zero rows and retained input/friendly error, restore the method and retry successfully. Clearly distinguish injected failure from real disk exhaustion.

## Task 2: Native failed-save integration without learner-data access

Files: tests/RecallDeck.Android.UiSmoke (small MAUI Android host), tests/RecallDeck.Storage.Checks/UiFailureStudyStore.cs, a linked-source focused test and Core.Tests project entry if useful.

- [ ] Reuse production RecallDeck.UI.App and production SqliteStudyStore, existing MAUI/package versions and linked original icon/splash assets. Own application ID and app-private database; no shipping host changes or hidden production hook.
- [ ] A test-only IStudyStore wrapper delegates all operations. Only the first CreateCard whose front starts with a documented synthetic marker gets a temporary SQLite BEFORE INSERT trigger for that exact validated card GUID; RAISE(ABORT) exercises real native rollback/IOException. Drop the trigger in finally and allow retry. Use the existing native SQLite smoke trigger pattern, not a new public/internal production test API.
- [ ] Test the helper against real desktop SQLite: failure leaves snapshot unchanged; retry commits once; ordinary inputs do not inject failure. This is acceptance infrastructure for existing behavior; do not manufacture a production red/green bug.
- [ ] Enable WebView developer tools in this test host only so the supported Android test API can drive actual shared UI. Register the same services/clock. No accounts, telemetry, broad permissions, signing secrets or external service.
- [ ] Android UI scenario asserts friendly failed-save error, no leaked injected detail, retained exact Unicode input, no card committed, successful retry, actual cold restart and normal cleanup. Shipping app normal flow was already verified in PR44; this isolated host adds failure-path integration, not a claim that the shipping binary contains fault injection.

## Task 3: CI, evidence and delivery

- [ ] CI installs the managed Chromium headless shell via the pinned local playwright-core CLI (no unpinned npx fallback) and runs the web journey against the actual published app on loopback. Compile the isolated Android UI host alongside existing Android builds; native runtime remains an explicitly recorded local gate.
- [ ] Run full .NET Release and npm suites, web publish, Android shipping/test-host builds. Parent runs actual published web plus existing-emulator UI journey, and can rerun the separate native SQLite phase1/phase2 rollback/restart smoke.
- [ ] Record exact runtime/tool versions, commands, hashes, parent/independent-review attribution and remaining limits. Pre-existing Android safe-area overlap belongs to #29. No physical-device/TalkBack/complete lifecycle claim.
- [ ] Full diff/whitespace/hashes; independent spec then quality reviews; ready PR closing #16; exact tested/reviewed head guard and current hosted checks before merge. Project synchronization/readback is pending because token lacks Projects scopes; never invent saved moves.

Parent owns ADB/device/browsers/runtime, source-control commits and GitHub writes. Implementation worker uses source edits/background unit/build tools only. Remove any temporary validation media before push, preserving original assets and user images.
