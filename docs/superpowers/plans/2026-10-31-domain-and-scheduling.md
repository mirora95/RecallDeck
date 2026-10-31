# Domain models and scheduling implementation plan

Delegated daily delivery: issues #4 and #5, governed by the committed product design.

## Design

Use immutable Deck and Card values with explicit nonempty Guid identifiers. Card stores a nonempty DeckId; checking existence belongs to storage/application operations (#12), not a platform-aware model. Validate title/front/back after trimming, preserve all internal text, and retain optional description unchanged (null becomes empty). Length bounds use .NET UTF-16 string length, matching ordinary .NET/HTML form limits; Unicode is never normalized, interpreted or stripped.

Represent the level and UTC due instant in a validated immutable ReviewSchedule. ReviewScheduler receives TimeProvider explicitly. It creates cards with a fresh ID, level 0 and the clock instant, and calculates a new schedule without mutating the old one. Remembered advances/caps at 5 (1/3/7/14/30 elapsed days); Again resets to 0 plus ten minutes. Reject undefined ratings and invalid levels. Do not implement persistence, review-event submission or UI in this PR.

## Tasks

- [x] Add model tests in tests/RecallDeck.Core.Tests/ModelTests.cs: trim, null/blank, exact/over bounds, Unicode/internal whitespace/plain HTML text, IDs and stable relationship. Verify red before implementation.
- [x] Implement src/RecallDeck.Core/{PlainText,Deck,Card}.cs and validated ReviewSchedule.cs. Run tests.
- [x] Add scheduling tests in tests/RecallDeck.Core.Tests/ReviewSchedulerTests.cs: new-card instant/IDs, both ratings from every level, cap, exact instants/offset normalization, invalid levels/ratings and no mutation. Verify red.
- [x] Implement src/RecallDeck.Core/{ReviewRating,ReviewScheduler}.cs. Run all tests.
- [ ] Test Release, build/publish web and compile Android with documented SDK/JDK paths. Review full diff and independently review immutable head.
- [ ] Create ready PR to main closing #4/#5, move board In Review, inspect final-head CI/reviews, guarded merge, read back closure and Done statuses.

## Verification limits and prior work

No UI change, persistence or Android runtime behavior is claimed. There is no emulator/device. Main foundation CI 41071225386 succeeded. Late Copilot feedback on PR #33 questions network manifest permissions; evaluate with Android runtime evidence in a later platform-scoped task, keep this Core-only PR separate.

## Primary references

- [TimeProvider and deterministic clocks](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview)
- [DateTimeOffset elapsed-day addition and range errors](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.adddays?view=net-10.0)
