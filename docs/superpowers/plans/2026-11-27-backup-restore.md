# Recoverable backup and restore implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Deliver issues #25, #26 and #27 as one coherent export, validated preview and confirmed atomic replace-all journey on both hosts. Close each only after its acceptance passes.
**Architecture:** Reuse StudyBackupCodec for version 1 validation. Add one atomic replace-all storage operation and a small platform-independent preview/restore service. Connect shared Backup UI to supported browser download/file selection and Android document save/open through a small host abstraction.
**Tech stack:** .NET 10, shared Razor, real SQLite/IndexedDB, bUnit/xUnit/Node and supported browser/Android file APIs.

## Approved constraints

- JSON is versioned and limited to 10 MiB. Preserve identifiers, related records, Unicode and UTC instants. Validation, cancellation or failed persistence preserves all current data. Only explicit confirmation permits replacement. Offer a working current-data export first.
- Inherit the established warm-green Operate world, serif headings, system body and ruled sections. Use semantic controls, at least 48px touch targets, escaped text, narrow wrapping and clear recovery states. No accounts, sync, server, broad storage permissions or paid integration.
- Implement Task 1 and review it before Task 2. Related issues share the eventual coherent PR; do not close #25 at a backend-only checkpoint. Dependencies #7, #8, #23 and #24 are delivered.

## Task 1: Atomic storage and preview contracts

- [ ] Read repository instructions, current design, issue acceptance, codec and adapter contracts.
- [ ] Write failing tests for full valid replacement, empty backup, malformed/null/duplicate/dangling records rejected before destruction, cancellation, induced failure after destructive work, rollback, retry and reopen across memory, SQLite and IndexedDB.
- [ ] Add IStudyStore.ReplaceAllAsync with the existing transaction, cancellation and fault patterns. Validate the complete immutable snapshot first. Keep the database schema/version unchanged.
- [ ] SQLite replaces related rows within one transaction. IndexedDB clears/adds all related stores in one readwrite transaction whose abort restores previous data. Preserve uniqueness and references.
- [ ] Add a small validated preview and restore service using the existing codec. Preview never writes; cancel never calls storage; explicit confirm alone calls replacement. Expose validated current-data export bytes for the later working UI offer. Avoid speculative revision locks or reset behavior.
- [ ] Update existing test wrappers/fakes for the interface without changing unrelated behavior. No production test hooks or new InternalsVisibleTo. Extend native StorageSmoke for replacement, reopen and rollback evidence where useful.
- [ ] Run intended RED/GREEN targeted tests, full .NET/Node suites and relevant builds. Freeze every changed-file hash and report results. Parent handles actual runtime and independent spec then quality reviews before Task 2. Keep all three issues open at this checkpoint.

## Task 2: Shared UI and host file transport

- [ ] Begin after Task 1's review checkpoint. Keep platform file interfaces at the shared UI/host boundary; Core remains platform-independent.
- [ ] Replace Backup placeholder with working dated export and bounded file selection. Validate before preview. Show deck/card/review counts and explicit ALL-CURRENT-DATA replacement wording. Offer working export before confirmation, distinct Cancel and Replace all actions, busy guards, retained preview on failure and safe retry. Warn that a valid empty backup removes all current study data.
- [ ] Browser transport uses supported file input and Blob/object URL download with eventual revocation. A download request cannot prove disk-save completion. Explain origin-local storage and clearing/eviction risk.
- [ ] Android uses supported document creation/opening or proven MAUI equivalents with JSON MIME and URI streams. User-selected documents require no broad storage/media permission. Cancel/failure is reported accurately. Do not require private paths, external apps or accounts. Save is sufficient; share is optional.
- [ ] Reuse the same production file service in the isolated native host. Shipping app tests preserve pre-existing data and can verify export/cancel; destructive import acceptance uses only the dedicated app's owned synthetic fixtures.
- [ ] Add meaningful component/transport tests for invalid/oversized files, preview, cancel, busy, confirmation, failure/retry and late disposal. Keep test-only native failure injection isolated and verify actual transaction rollback.
- [ ] Update factual availability in README, PRODUCT and Home when behavior works. Do not repair document schema or change visual identity.

## Task 3: Runtime, review and delivery

- [ ] Parent verifies actual published headless web export, new data, file preview/cancel, confirmation, restored reload, invalid/oversized rejection and preservation. Use fresh contexts, loopback only and no user profile.
- [ ] Parent verifies actual Android document save/open/cancel, preview, confirmed restore and restart in the dedicated app. Verify shipping export/cancel separately. Use the already running exact authorized device; no Windows input/focus/clipboard, device launch, settings changes or permission broadening.
- [ ] Synthetic files use unique owned names. Preserve pre-existing records/files. Cleanup only exact owned fixtures through supported actions. Remove temporary validation images after inspection before pushing; keep text evidence and product assets.
- [ ] Review full diff and frozen hashes. Obtain independent spec then quality reviews, bounded rendered finish review and documentation reconciliation. Fix findings, rerun affected verification and bind reviews to exact committed bytes.
- [ ] Create a ready PR with separate closing lines for #25, #26 and #27; attach it to this chat. All hosted checks/reviews must pass before guarded merge. Read back merge/account/issue closure; record Project synchronization honestly when supported access is unavailable.

## Primary references checked by parent on 2026-11-27

- https://developer.android.com/training/data-storage/shared/documents-files
- https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/data/share?view=net-maui-10.0
- https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-picker?view=net-maui-10.0
- https://developer.mozilla.org/en-US/docs/Web/API/URL/createObjectURL_static

The Android document guide states that user-selected Storage Access Framework documents need no broad system permissions. Do not copy media-permission examples for JSON documents.
