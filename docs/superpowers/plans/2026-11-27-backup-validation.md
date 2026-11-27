# Versioned backup format and validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Execute sequentially with spec and quality review before publishing.

**Goal:** Deliver #23 and #24 as one coherent pure Core backup codec; serialization round trips through the strict import validator. Start only after #15 and #19 are delivered.

**Architecture:** A small public codec exports a StudySnapshot to UTF-8 JSON bytes and validates UTF-8 import bytes into a detached StudySnapshot. No storage, filesystem, UI, host DI, permission or replace-all API. Use the existing .NET 10 System.Text.Json and immutable domain types, no packages. Define a version-1 camelCase envelope containing schemaVersion, decks, cards with schedules, and reviews. Use DTOs/source generation or explicit JSON handling where needed for trimming safety; do not serialize arbitrary object graphs or app state.

**Tech Stack:** .NET10, xUnit, System.Text.Json.

## Task 1: Define and test version1 format (#23)

Files: create a small Core/Backup codec and focused tests under RecallDeck.Core.Tests/Backup; document the format in docs/backup-format.md.

- [ ] Write exact empty envelope and populated round-trip tests first; run to prove missing behavior fails.
- [ ] Preserve all identifiers, Unicode including internal whitespace/emoji, all schedule levels0..5, both ratings, UTC ticks including fractional ticks and boundary dates. Assert exact fields, not merely counts.
- [ ] Implement minimal export and parsing using explicit allowed data fields only. Export contains no device paths, credentials or unrelated state. Ensure successful exports are importable; reject invalid/inconsistent snapshots and exports beyond the v1 size limit instead of silently truncating.

## Task 2: Validate before any write (#24)

- [ ] Before parsing, reject UTF-8 byte lengths greater than 10*1024*1024. Accept a valid JSON document padded with whitespace to the exact limit; reject limit+1 and demonstrate byte-vs-character behavior.
- [ ] Reject malformed/empty JSON, invalid UTF-8, null/missing root collections or rows, missing required members, unknown version, duplicate JSON properties and unknown fields rather than silently losing data. Keep strict depth/number behavior.
- [ ] Reject invalid/empty IDs, duplicate IDs in each entity collection, duplicate review session-item IDs, dangling card->deck or review->card references.
- [ ] Enforce existing domain field rules (trim required title/front/back consistently, retain internal Unicode/whitespace,80/500/4000 limits), integral level0..5 and ratings Again/Remembered only. No extra product constraints on chronology.
- [ ] Validate timestamps BEFORE domain constructors normalize offsets: require explicit UTC Z or +00:00 and valid ISO instants; reject offsetless and nonzero-offset input even on a UTC machine. Preserve ticks exactly. Missing dates must not silently become MinValue.
- [ ] Return specific usable validation errors with field/collection context, without echoing full user data. No IStudyStore dependency and no writes on validation success or failure; failure leaves the supplied bytes/current snapshots unchanged.
- [ ] Cover each malformed, boundary and adversarial rejection; do not build UI/import persistence or speculative services.

## Task 3: Verify and deliver

- [ ] Full .NET Release tests and npm test; Release web publish and Android Debug compile. No native input/runtime claim for a pure Core API.
- [ ] Full diff, whitespace, source hashes, independent spec then quality agent review; fix findings and rerun affected checks.
- [ ] Ready PR closing23/24 only with accurate API scope and exact evidence, current-head hosted checks, exact-head guarded merge and readback. Feature download/restore UI remains later work.

No mouse, keyboard, window, browser, clipboard or emulator/device controls. Project synchronization/readback remains pending because token lacks Projects scopes and user retains desktop control. Preserve existing PR44 and learner data.

Primary sources: https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries (duplicate properties/strict options); https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/missing-members (unknown-member rejection).
