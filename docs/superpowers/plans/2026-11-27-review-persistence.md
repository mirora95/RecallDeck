# Atomic review persistence acceptance (#19)

Dependencies #5, #7, #8 and #17 are delivered. The current SQLite and IndexedDB adapters already implement atomic schedule/history updates and duplicate review/session-item guards. Verify this implementation rather than introducing another persistence layer.

## Scope

- Extend the existing .NET storage contract and shared IndexedDB cases for successful Again and Remembered ratings from a nonzero schedule.
- Prove explicit expected UTC schedules, complete card content preservation and exactly one matching history event.
- Cover duplicate review/session identifiers, injected commit failure followed by the same event retry, cancellation rollback/retry and actual deleted-card rejection for both ratings.
- Keep production code unchanged unless an actual regression is reproduced. Document test coverage honestly; passing acceptance tests of existing behavior are not a red/green bug fix.
- Do not add UI, session state, adapters, schema changes or dependencies.

## Verification

1. Run both .NET adapter contract implementations (in-memory reference and linked production SQLite) and the shared JS suite with fake-indexeddb; disclose native browser/device coverage limits.
2. Run the full .NET Release and Node suites, web Release publish and Android Debug compile.
3. Freeze source hashes and review the full diff. Obtain separate spec then quality agent reviews. Address findings and repeat affected checks.
4. Create a ready PR linked to #19 with exact commands/results and verification limits. Inspect hosted checks at the exact final head before a guarded merge.

No browser, desktop, emulator, clipboard or window controls are permitted. Project status synchronization is pending: the current token has no Projects scopes and the user is using the desktop. Never claim an unsaved board move.
