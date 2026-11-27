# Safe storage schema upgrades implementation plan

> **For agentic workers:** Use superpowers:subagent-driven-development task by task. Obtain separate spec and quality reviews before delivery.

**Goal:** Deliver issue #15 with a real, additive schema-1 to schema-2 transition that preserves stored study records and rolls back failed upgrades.

**Architecture:** Keep the existing IStudyStore contract and SQLite/IndexedDB adapters. Schema 2 enforces the existing integer review-level (0–5) and rating (0–1) rules at the persistence boundary. SQLite adds validation triggers in its existing transaction; IndexedDB validates prior records in the version-change transaction and validates new mutation values. Do not rebuild/delete tables or stores, add services, change models, or introduce a migration framework.

**Tech stack:** Pinned .NET 10, sqlite-net, native IndexedDB, existing xUnit and fake-indexeddb fixtures.

## Task 1: Transactional SQLite upgrade

Files: `src/RecallDeck.Storage.Sqlite/SqliteStudyStore.cs`, `tests/RecallDeck.Core.Tests/Storage/SqliteStudyStoreTests.cs`, `tests/RecallDeck.Storage.Checks/SqliteSmoke.cs`.

- [ ] Add an explicit schema-1 fixture containing a Unicode deck, literal-text card, nonzero UTC schedule and review/session identities. Read through the production adapter, compare every field and inspect `PRAGMA user_version = 2`; reopen and repeat. Observe the version assertion fail before implementation.
- [ ] Add a migration-failure fixture with a preexisting trigger whose name collides with the last migration trigger. Assert the earlier migration triggers and version change are rolled back, all prior rows/objects remain, and removing only that synthetic conflicting trigger permits retry. Add malformed prior level/rating cases and newer-version preservation; version 3 is now the newer fixture.
- [ ] Keep initialization in `RunInTransaction`. Initialize an empty v0 database using the existing v1 DDL, then apply v1→v2. Refuse other versions and populated unknown v0 databases. Before adding triggers reject existing noninteger/out-of-range levels or ratings with a preserve-data recovery message.
- [ ] Add INSERT and UPDATE triggers for each bound. The SQL shape is:

```sql
CREATE TRIGGER ValidateCardLevelInsert BEFORE INSERT ON Cards
WHEN typeof(NEW.Level) != 'integer' OR NEW.Level NOT BETWEEN 0 AND 5
BEGIN SELECT RAISE(ABORT, 'Invalid review level'); END;
```

Repeat for UPDATE and Reviews.Rating in 0/1. Set `PRAGMA user_version = 2` only after all validation and DDL succeed. Retain foreign keys, existing indexes, tables and records. Do not use IF NOT EXISTS to conceal a conflicting migration object.
- [ ] Exercise this exact v1→v2 fixture through the existing test-only native SQLite smoke entry point, retaining two-phase restart/rollback checks. No real learner database is used or reset.
- [ ] Run `dotnet test tests/RecallDeck.Core.Tests -c Release --verbosity minimal` and retain red/green evidence outside the repository.

## Task 2: IndexedDB upgrade and recovery

Files: `src/RecallDeck.Web/wwwroot/study-store.js`, `src/RecallDeck.Web/Storage/IndexedDbStudyStore.cs`, `tests/RecallDeck.Web.Tests/storage-cases.mjs`, `tests/RecallDeck.Core.Tests/Storage/IndexedDbInteropTests.cs`.

- [ ] Add explicit v1 IndexedDB fixtures with all three id-keyed stores and the unique sessionItemId index. Assert preserved Unicode/text/IDs/schedules/history and database.version 2 after a production read. Observe failure before code changes.
- [ ] Add aborted/malformed upgrade fixtures. Verify version remains 1 and original records remain; repair only the synthetic fixture and retry successfully. Refuse unknown layouts and version 3 without resetting them. Check cancellation during upgrade does not write learner records and no connections leak.
- [ ] Open version 2. Create current stores/index only when oldVersion is 0. For oldVersion 1 validate known store key paths and review-session index, and asynchronously inspect existing card levels and review ratings in the upgrade transaction. Any unsupported layout/value or request failure aborts the upgrade; wait for the request error before settling the result.
- [ ] Validate these same bounds before createCard/applyReview writes. Use integer checks, not coercion. No new indexes or stores are needed. Preserve the existing transaction completion and cancellation guarantees.
- [ ] Return a distinct migration error and map it to actionable `IOException` recovery text: preserve existing browser data, use the matching app or verified backup, and do not clear storage. Keep newer-version errors distinct. Test interop mapping.
- [ ] Run `npm test` and the shared .NET suite; also run the existing browser cases against native Chrome through the supported UI.

## Task 3: Verification and documentation

Files: `docs/storage-contract.md`, `docs/sqlite-storage.md`, existing test harness documentation as needed.

- [ ] Document both versions, exact transition, rollback/retry and older-client/newer-data behavior. Preserve the warning that app-private/browser storage is not a backup.
- [ ] Run shared .NET/Node suites, Release web publish, relevant Android compile/test-smoke build and full diff/whitespace checks.
- [ ] Independently review acceptance against issue #15, then review code quality/data-loss/error paths. Resolve blockers and rerun affected checks.
- [ ] Verify native Chrome migration cases and native Android migration fixture. Record missing runtime access accurately; compilation alone cannot satisfy native Android acceptance.
- [ ] Keep temporary validation media out of Git and remove any saved screenshots after inspection. Open one scoped PR to main linked to #15 with exact commands, reviewed/tested SHA and limits. Merge only after all required acceptance/checks pass at that head; otherwise keep it In Review and Open.

## Remaining delivery order

Issue #11/PR42 retains its actual Android UI acceptance gate. #13/#14 wait for delivered #11; #16 waits for #13/#14/#15. Review queue/service work #17/#19 depends on delivered Core/storage tasks and may be planned separately without pretending the UI is delivered. Other issues follow their committed dependencies. No release date, app-store publication or public hosting is authorized by this plan.
