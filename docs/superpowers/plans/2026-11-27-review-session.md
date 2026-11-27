# Reveal-first review sessions implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Deliver issues #18 and #20 as one coherent reveal/rate journey on both hosts.

**Architecture:** Capture a stable due queue using the delivered ReviewQueueService. Keep session ownership, reveal state and async guards in the review route, with a small presentational ReviewCard component. Persist each accepted rating through the existing atomic IStudyStore.ApplyReviewAsync operation. Preserve original UI language and design tokens.

**Tech stack:** .NET 10, shared Razor, bUnit/xUnit, actual headless Chromium and existing Android WebView tests. No account, server, sync, notifications or AI features.

## Existing approved design

The owner approved the product design and delegated routine choices. Extend the established interface; do not redesign PRODUCT/DESIGN or introduce a second visual world. Initial question only, explicit Reveal answer, then Again or Remembered. Use escaped plain text and semantic buttons. Keep long content in natural document flow with reachable controls at 360 CSS pixels. Give question, answer, progress, error and completion meaningful headings/labels/status semantics. No surprise insertion into the captured queue.

## Task 1: Prove reveal/session behavior before implementation

- [ ] Inspect AGENTS/CONTRIBUTING, product design, current UI patterns, ReviewEvent/ReviewScheduler/ReviewQueueService and both storage duplicate/rollback contracts.
- [ ] Add focused bUnit tests for hidden answer and absent ratings before reveal; keyboard-semantic buttons, escaped 4000-unit plain text, new item resets reveal; no unsafe HTML.
- [ ] Add route tests for loading/read failure/retry, empty queue, ordered captured session, both ratings, accurate reviewed/skipped completion, no new-card insertion, rapid double submit while busy, failure retaining card/answer with safe retry, deleted card skip, route disposal/late completion guards.
- [ ] Run tests RED for intended missing behavior. Avoid tests mirroring implementation or asserting decorative markup.

## Task 2: Implement the smallest complete path

- [ ] Add src/RecallDeck.UI/Components/ReviewCard.razor, controlled by parent state and EventCallbacks; keep ratings unavailable until explicitly revealed, preserve focus/keyboard labels and long-text wrapping.
- [ ] Replace Pages/Review.razor placeholder. Optional deck filter may be a query parameter if useful to isolate synthetic runtime fixtures; validate missing deck and show recovery, never fall back to all cards silently. Link from deck detail if adding filter. Global Review route captures all due cards.
- [ ] Inject/register ReviewQueueService in both shipping hosts and isolated test host; follow scoped clock/store patterns. Retain a stable session item GUID and review event for retry. Disable all conflicting session controls while saving; advance only after successful persistence.
- [ ] Use an up-to-date card snapshot immediately before scheduling so content edits preserve latest scheduling. Confirm card still exists, handle deleted card explicitly and truthfully. Do not catch every exception as deletion or claim a failed write succeeded.
- [ ] If a fresh snapshot already contains this sessionItemId after uncertain completion, reconcile instead of creating another event or double advancing. Existing adapters enforce unique session item.
- [ ] Keep inner exception messages out of UI; retain the visible answer on save failure; Retry uses same logical item. Once advanced, do not rate it again in current queue. Again schedules +10min and does not reinsert now.
- [ ] Update Home/README availability text to describe actual reviews; keep backups pending. Scope CSS to new review UI.

## Task 3: Reproducible actual-runtime checks

- [ ] Extend test-only native failure wrapper to abort one exact owned review via SQLite trigger and remove it safely, with focused real SQLite regression tests. No shipping fault hook or test-only service registration.
- [ ] Add/extend loopback-only headless and fixed isolated-native test CLI for review: create unique owned deck/cards through normal UI, capture exact IDs, reveal and both ratings, rapid activation commits once, controlled cards/reviews store failure preserves schedule/history and allows retry, empty completion, reload/cold restart persists expected histories/schedules. Shipping normal path must also be checked separately on actual shipping APK.
- [ ] Preserve every pre-existing record. Only exact owned fixture IDs can be deleted through normal confirmation after ownership/history validation. No app-data clear, user profile, AVD launch, Windows input/focus or speculative cleanup. No screenshots by default.
- [ ] Run meaningful targeted and full .NET/Node tests, web Release publish, shipping Android compile, package/install own APK with -r and exact package/AVD guards; run actual web + native flows. Parent owns device/browser operations, worker does not.
- [ ] Parent may capture bounded temporary headless/Android-device images for visual review; inspect then delete all validation media before pushing. Never Windows desktop screenshots or control.

## Task 4: Review and delivery

- [ ] Freeze full diff hashes, spec review first then independent quality review; address findings and rerun affected checks. Use fresh bounded Impeccable finish review for rendered UI.
- [ ] Commit reviewed/tested bytes, exact SHA binding, ready PR with separate Closes #18 and Closes #20 lines, attach to chat. All current-head hosted checks and review conditions must pass before guarded merge.
- [ ] Read back merge/mergedBy and issue closure; record truthful board synchronization pending because Projects access is unavailable without user desktop.
