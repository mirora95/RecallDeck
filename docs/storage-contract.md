# Study storage behavior

`RecallDeck.Core.Storage.IStudyStore` defines the behavior that Android SQLite and browser IndexedDB adapters must implement. It has no platform or database dependency. The in-memory implementation is **test-only**; no host currently persists learning data or registers a storage adapter.

## Operations

| Operation | Required behavior |
| --- | --- |
| ReadSnapshotAsync | Return a coherent, immutable snapshot of decks, cards and review events. Later writes must not alter a previously returned snapshot. |
| CreateDeckAsync / CreateCardAsync | Add a new identity; reject duplicates. A card must reference an existing deck. |
| UpdateDeckAsync | Replace the fields of an existing deck while retaining its ID. Never upsert a missing ID. |
| UpdateCardAsync | Update content only. Preserve ID, deck and schedule; review scheduling goes through ApplyReviewAsync. Never upsert. |
| DeleteCardAsync | Remove an existing card and its review history atomically. |
| DeleteDeckAsync | Remove an existing deck, its cards and their history atomically. Confirmation belongs to the application/UI operation, not the adapter. |
| ApplyReviewAsync | Update the existing card's schedule and append one review event in the same transaction. Preserve its content/deck. Reject duplicate review IDs or session-item IDs. |

Inputs are validated immutable Core values. `ReviewEvent` carries a nonempty review ID, card ID and session-item ID, a defined rating and UTC review instant. The application review operation will calculate the schedule using ReviewScheduler; this storage contract does not calculate intervals or run a review session.

Unknown deck/card references throw `KeyNotFoundException`. Duplicate identities, duplicate session items or prohibited content-update changes throw `InvalidOperationException`. Model validation uses `ArgumentException` with a field parameter name. I/O/storage exceptions and `OperationCanceledException` propagate; do not convert them into successful saves or clear data as recovery.

## Transaction and cancellation boundary

Every write is all-or-nothing across the affected collections. A failed transaction leaves the previously committed state unchanged. Honor cancellation before publishing a write, including a cancellation received while preparing that transaction. Once a commit has completed, cancellation is not a rollback mechanism; report the actual outcome. An adapter must use its database's transaction primitives, not separate successful writes to the card and history tables/stores.

A snapshot copies its input collections into read-only lists, and the contained Core values are immutable. Read ordering is not guaranteed by the adapter. DeckService returns a separate read-only list sorted by ordinal title then stable ID. Create and rename return only after the storage operation succeeds; rename retains the existing description. Missing-ID, validation, cancellation and storage errors remain observable to the caller.

## Reusable verification

`tests/RecallDeck.Core.Tests/Storage/StudyStoreContractTests.cs` is an inherited xUnit fixture. Each adapter test class supplies an isolated store and a test-only way to inject a failure or cancellation immediately before transaction commit. Run the same cases for each adapter; use a temporary SQLite file or isolated IndexedDB database rather than learner data. Add provider-specific reopen, quota, unavailable-storage and migration checks in those adapter tasks.

The current `InMemoryStudyStoreTests` factory runs these cases against a staged in-memory reference implementation. It copies state under a lock, applies the mutation to that candidate, invokes a one-shot test fault hook, checks cancellation, then publishes a new snapshot. Failed writes therefore cannot leak partially changed lists. This proves the fixture and application behavior; it does not prove disk/browser durability, multi-process coordination or platform lifecycle behavior.

Run `dotnet test tests/RecallDeck.Core.Tests -c Release`. No new packages are required.

## Primary references

- [CancellationToken.ThrowIfCancellationRequested](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken.throwifcancellationrequested?view=net-10.0)
- [Array.AsReadOnly](https://learn.microsoft.com/en-us/dotnet/api/system.array.asreadonly?view=net-10.0): a wrapper is not a defensive copy; StudySnapshot copies each input before wrapping it.
