# Shared card editing and confirmed deletion implementation plan

> **For agentic workers:** Use superpowers:subagent-driven-development for this coherent deck-detail task, followed by separate spec and quality reviews.

**Goal:** Deliver issues #13 and #14 as one useful deck-detail journey after merged #11/#12, preserving the current storage transactions and schedules.

**Architecture:** Keep shared Razor UI and existing Core services/adapters. Link each deck title to `/decks/{DeckId:guid}`. Read deck metadata and its cards from one coherent snapshot; present missing/loading/retry states. Add only the smallest Core query/delete surface needed by that page, using existing IStudyStore operations. Do not add packages, database versions, accounts, review UI or backup scope.

**Tech stack:** Pinned .NET10, Razor components, bUnit/xUnit, native IndexedDB and SQLite.

## Task 1: Core detail query and existing atomic deletion

Files: `src/RecallDeck.Core/DeckService.cs`, a small immutable deck-detail result if needed, `tests/RecallDeck.Core.Tests/DeckServiceTests.cs`, existing shared storage contract tests.

- [ ] Write meaningful failing tests for one-deck card filtering and a detached immutable result, a missing deck, cancellation and deletion delegation. Use existing immutable Card/Deck values and read-only arrays. Verify that card content editing preserves identity, deck, schedule and review history through existing CardService/storage tests rather than duplicating the implementation.
- [ ] Implement a single-snapshot detail query on DeckService and a DeleteAsync method delegating to existing atomic DeleteDeckAsync. Return deck plus its read-only card list; throw KeyNotFoundException for a missing deck. Keep cancellation semantics and never reconstruct or replace a whole store snapshot.
- [ ] Confirm shared adapter contracts already exercise deck/card/history cascades and rollback/retry; add only genuinely missing acceptance coverage. No new persistence mechanism.

## Task 2: Shared deck detail and plain-text editing

Files: `src/RecallDeck.UI/Pages/DeckDetail.razor`, `src/RecallDeck.UI/Pages/Decks.razor`, `src/RecallDeck.UI/wwwroot/app.css`, new `tests/RecallDeck.Core.Tests/DeckDetailTests.cs`, `NavigationTests.cs` when needed, both host DI files.

- [ ] Start with failing bUnit cases: initial loading/read failure retry, missing/deleted deck, empty card list, create and edit with labeled Front/Back fields, blank/over4000 validation, Unicode/literal HTML rendering, safe long wrapping, failed save retaining typed values and retry, duplicate submit guard, canceled edit no write. Seed an existing schedule/history and assert edit preserves it. Exercise route parameter changes so stale prior-deck data cannot be written to a newly selected deck.
- [ ] Add the shared route and deck-title links. Render all learner text through escaped Razor expressions, with pre-wrap and overflow-wrap for card text, widths constrained by the existing responsive layout. Keep controls usable at 360 CSS pixels. Do not use MarkupString.
- [ ] Use one clear create/edit form (or a focused child component if necessary), front/back textareas with maxlength4000, required labels and linked inline error messages. Bind oninput. Disable conflicting submissions while saving; check a saving guard before awaits. Clear fields only after successful create; update the existing list from returned saved values. Failed writes keep text and show practical retry/preserve-data advice without internal exception details. Cancel editing writes nothing.
- [ ] Register CardService and ReviewScheduler with TimeProvider.System in Web and MAUI hosts, retaining platform stores. Use the existing scheduler so a new card is immediately due. Keep docs honest about reviews/backups still pending.

## Task 3: Explicit card and deck deletion

Files: shared deck-detail page, relevant UI/service/storage tests.

- [ ] Write failing tests for card deletion cancel/confirm, deck deletion cancel/confirm with zero and populated card counts, retained unrelated data, and transaction failure/retry preserving deck/cards/history. Read underlying store after cancellation to prove no write.
- [ ] Card Delete opens an explicit confirmation identifying the card; only Confirm delete invokes CardService.DeleteAsync. Cancel returns to the card unchanged. Retain confirmation/error state for retry after a failed atomic delete.
- [ ] Deck Delete opens a confirmation naming the deck and current card count, explaining that cards and review history are removed. Confirmation alone calls DeckService.DeleteAsync, and navigation back to /decks occurs only after success. Cancel performs no store mutation. A failure leaves UI/data intact and permits retry. Do not pretend an already deleted or changed record saved successfully; handle missing data visibly.
- [ ] Maintain labels, visible focus and a persistent status region, with focus moved to the editor/confirmation or useful heading after action. Keep deletion confirmation within the page using semantic controls; avoid unsupported native dialogs and unnecessary modal complexity.

## Task 4: Verify and deliver the full journey

- [ ] Run targeted red/green UI/Core tests, then full `dotnet test tests/RecallDeck.Core.Tests -c Release --verbosity minimal`, `npm test`, Release web publish, Android Debug compile and standalone Debug APK build with the existing SDK/JDK21. Review the full diff and whitespace.
- [ ] Parent performs actual native web and Android create -> edit -> process restart/reload -> cancel/delete with Unicode and long literal text, plus explicit deck cancellation/confirmation and related-record preservation. Compilation alone does not complete Android acceptance. Use owned synthetic fixtures and never reset learner data.
- [ ] Obtain spec review first, then independent quality review. Fix blocking findings test-first and rerun affected checks/reviews. Keep all temporary validation media out of Git and remove saved validation screenshots before push.
- [ ] Open one ready PR to main, Closes #13 and #14, with exact tested/reviewed SHA, commands/results, native evidence and limits. Move both issues In Review. Merge with exact-head guard only after CI/review/acceptance passes; read back mergedBy mirora95, issues closed and saved Project Done.

## Next dependencies

Only after #13/#14/#15 are delivered, run issue #16 full cross-platform foundation acceptance. Then review queue #17, reveal #18, atomic review #19 and session #20; continue the committed remaining dependencies through progress, backup, offline, lifecycle and final recovery/readiness tasks. The all-tasks user request authorizes continued implementation, not false closure or skipped gates.
