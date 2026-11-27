# Stable due-card queue implementation plan

> **For agentic workers:** Use superpowers:subagent-driven-development; obtain separate spec and quality reviews before delivery.

**Goal:** Deliver issue #17 as a small platform-independent Core query, without introducing review UI or depending on unmerged card UI.

**Architecture:** Add ReviewQueueService using the existing IStudyStore and injected TimeProvider. Capture one coherent snapshot and one clock instant. Return an immutable read-only array of existing immutable Card values. Keep existing persistence adapters and scheduling rules unchanged.

**Tech stack:** Existing .NET10 Core and xUnit test fixture. No packages, migrations or host/UI changes.

## Task 1: Fixed-clock contract tests

File: `tests/RecallDeck.Core.Tests/ReviewQueueServiceTests.cs`.

- [ ] Write tests before production code. Seed two existing decks with past, equal-now and future cards. Verify DueAt<=capture instant, sort by due instant then Guid ID, with insertion order deliberately different. Use stable known GUIDs and a TimeProvider test double.
- [ ] Verify null deck filter returns all due cards; one existing deck returns only its due cards; an empty existing deck returns empty; unknown/empty requested ID fails with KeyNotFoundException rather than silently becoming all decks.
- [ ] Capture a queue, then create/edit/delete stored cards. Assert captured membership, order and immutable card contents remain unchanged, while a new capture sees later state. Attempt list mutation and assert it is read-only.
- [ ] Verify precanceled calls do not read storage, read failure propagates, null dependencies are rejected and only one snapshot/clock read occurs for each capture. Do not mirror implementation internals beyond these behavioral contracts.
- [ ] Run focused tests and retain the expected red failure before adding the service.

## Task 2: Minimal capture query

File: `src/RecallDeck.Core/ReviewQueueService.cs`.

```csharp
public sealed class ReviewQueueService
{
    public ReviewQueueService(IStudyStore store, TimeProvider clock);
    public Task<IReadOnlyList<Card>> CaptureAsync(Guid? deckId = null, CancellationToken cancellationToken = default);
}
```

- [ ] Validate dependencies. Throw on cancellation before work. Read exactly one StudySnapshot, validate a requested deck against its Decks, capture clock.GetUtcNow once and document the capture-instant semantics.
- [ ] Filter `card.Schedule.DueAt <= now` and the optional existing deck. Sort `OrderBy(card => card.Schedule.DueAt).ThenBy(card => card.Id)`. Return `Array.AsReadOnly(...ToArray())` so later store operations cannot insert surprise cards into an existing queue.
- [ ] Do not add a session/rating state machine, session-item IDs, new persistence operations, DI registrations with no current consumer, counts/history/backup UI or a query framework. Those belong to later dependency tasks.
- [ ] Run focused green then full shared .NET/Node suites, web publish and Android compile. Review the full diff and frozen hashes. Obtain independent spec then quality review; fix actual blockers test-first.

## Task 3: Delivery and evidence

- [ ] Record exact commands/results and reviewer identities honestly. No Android learner UI feature is introduced by this pure Core query; fixed-clock contracts are its acceptance. Do not reuse compilation as evidence for the separate pending PR44 learner journey.
- [ ] Remove temporary validation media before push. Open one ready PR to main, Closes #17, with exact tested/reviewed head and meaningful evidence. Inspect CI and all review threads. Merge only with exact SHA guard and no bypass if all requirements pass.
- [ ] Project In Progress synchronization is pending: the gh token lacks Projects scopes and the user prohibits all browser/desktop controls. Record actual status and automatic PR/issue workflow results only when verified. Do not broaden credentials or claim unsaved moves.
