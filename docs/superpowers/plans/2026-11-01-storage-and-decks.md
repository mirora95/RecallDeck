# Storage contract and deck operations

Delegated delivery for issues #6 and #10, within the committed product design.

## Design

Use an explicit IStudyStore contract: read a detached immutable snapshot, create/update/delete decks and cards, and atomically apply one review event plus its schedule. No generic repository or database library is introduced. Review events have stable IDs, a card ID, a unique session-item ID, rating and UTC review instant. Card content updates preserve deck/schedule; only an atomic review changes schedule. Deletes cascade to related records. Unknown IDs, orphan records and duplicate identities fail without mutations.

The test-only in-memory implementation stages a complete next state under a lock and publishes it only after the injected failure/cancellation boundary. An inherited contract test fixture is reusable by future SQLite/IndexedDB adapters through a store factory and a test-only pre-commit fault hook. This is behavioral evidence, not disk persistence or a shipping storage adapter.

After #6 passes, implement DeckService using existing Deck validation and IStudyStore. Create returns only after successful persistence; rename preserves ID/description; list is ordered by ordinal title then ID. ArgumentException parameter names provide field feedback; missing-ID, cancellation and storage exceptions propagate. Do not register services in a host without an actual adapter.

## Sequence and checks

- Add contract tests, review-event/snapshot cases and a missing-behavior fixture; run them red.
- Implement the minimal contract/value types and staged in-memory test fixture; run green, then mark #6 implementation verified.
- Move #10 In Progress, write failing deck-operation tests, implement DeckService and rerun the complete suite.
- Run Release tests, web build/publish and Android compile. Review the full immutable diff, request independent review, resolve findings and inspect hosted checks at the final head.
- Create a ready PR closing #6/#10, save In Review board states, merge with an exact-head guard after checks/reviews, and verify mergedBy, issue closure and saved Done statuses.

## Limits

No SQLite/IndexedDB implementation, UI, release deployment, device runtime or backup restore is claimed. Existing PR33 Android-permissions feedback remains an Android-scoped follow-up. Android compilation does not substitute for later device acceptance. No temporary validation images may be saved or pushed.
