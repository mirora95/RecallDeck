# Study storage behavior

`RecallDeck.Core.Storage.IStudyStore` defines the behavior that Android SQLite and browser IndexedDB adapters must implement. It has no platform or database dependency. The in-memory implementation is **test-only**. The web host registers `IndexedDbStudyStore`; shared feature screens and Android persistence remain separate tasks.

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

Run `dotnet test tests/RecallDeck.Core.Tests -c Release`. The Core .NET suite uses the existing packages; browser test dependencies are described below.

## Browser adapter

The web adapter uses native IndexedDB database `RecallDeck`, schema version 1, with `decks`, `cards` and `reviews` object stores and a unique review-session index. All operations read a coherent snapshot in a transaction spanning these stores; a mutation queues only its affected record writes/deletes. This deliberately scans the small local dataset; add indexes/query operations when measured collection sizes warrant them. Concurrent connections serialize mutations without replacing a stale whole-database snapshot. Success is reported by the transaction's `complete` event. Aborted transactions retain every previously committed collection.

The .NET adapter validates deserialized data through the immutable Core constructors. It translates missing/duplicate/prohibited-update results to the contract exceptions, and storage/quota/version failures to actionable `IOException` messages. It never silently falls back to localStorage or recreates an existing database. Version 1 only initializes a new database; future migration behavior remains issue #15.

This is a WebAssembly-only adapter: an in-process JS reference lets cancellation abort a pending operation synchronously. It then awaits the actual transaction result rather than canceling only the interop wait. Cancellation after commit cannot roll it back or report that it failed. Connections and interop references close after each operation. Browser data still depends on the origin, browser settings, quota and eviction; commit completion is not a permanent backup guarantee.

## Browser adapter verification

`npm ci --ignore-scripts` then `npm test` runs the JS behavioral cases with test-only `fake-indexeddb`. They cover CRUD, identities, snapshot isolation, duplicate/orphan rejection, schedule/history atomicity, cascades, concurrent connections, rollback/retry, cancellation, unavailable/quota storage and refusal to reset a newer schema. Linked-source .NET tests check Core deserialization, contract exceptions and interop cancellation/lifetime. They do not substitute for native browser tests.

For native IndexedDB, serve the repository root on **loopback only**, with JavaScript MIME types for `.mjs` and `.js`. For example with Python 3:

```sh
python -c "import http.server,mimetypes; mimetypes.add_type('text/javascript','.mjs'); http.server.test(HandlerClass=http.server.SimpleHTTPRequestHandler,port=5194,bind='127.0.0.1')"
```

Open `http://127.0.0.1:5194/tests/RecallDeck.Web.Tests/browser.html`. All cases must show PASS. Each case deletes only its unique synthetic test database and fails if a connection blocks cleanup. Quota/unavailable results are injected; these are not real disk exhaustion or permission experiments.

To check the actual C# adapter and Blazor interop, use the separate test host:

```sh
dotnet run --project tests/RecallDeck.Web.Interop --no-launch-profile --urls http://127.0.0.1:5193
```

Open `http://127.0.0.1:5193`, click **Run checks**, reload, then click **Run checks** again. The first phase saves/edits a synthetic Unicode deck/card and review; the second verifies persistence and removes only that guarded fixture. Use a dedicated test origin, never a learner's installation; unexpected existing data stops cleanup. Its build copies the production JS module into an ignored test asset. The test host and browser fixture are not included in the shipping app. Chrome coverage does not establish Safari, Android runtime, quota exhaustion, power-loss or eviction behavior.

## Primary references

The Android adapter and its independent native runtime smoke are described in [SQLite storage](sqlite-storage.md).

- [CancellationToken.ThrowIfCancellationRequested](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken.throwifcancellationrequested?view=net-10.0)
- [Array.AsReadOnly](https://learn.microsoft.com/en-us/dotnet/api/system.array.asreadonly?view=net-10.0): a wrapper is not a defensive copy; StudySnapshot copies each input before wrapping it.
- [Using IndexedDB](https://developer.mozilla.org/en-US/docs/Web/API/IndexedDB_API/Using_IndexedDB)
- [Blazor JavaScript interop](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/call-javascript-from-dotnet?view=aspnetcore-10.0)
- [fake-indexeddb](https://github.com/dumbmatter/fakeIndexedDB), test-only implementation and Apache-2.0 license
