# Backup format version 1

`RecallDeck.Core.Backup.StudyBackupCodec` is a pure Core API:

- `Export(StudySnapshot snapshot)` returns UTF-8 JSON bytes.
- `ValidateImport(ReadOnlyMemory<byte> utf8Json)` returns a detached immutable `StudySnapshot` after all validation passes.
- `MaximumBytes` is 10 * 1024 * 1024 (10 MiB). The limit counts encoded bytes before parsing, including JSON whitespace. Exactly the limit is accepted; one byte more is rejected. Export rejects oversized or inconsistent snapshots rather than truncating them.

The API has no storage, filesystem or host dependencies. It does not download files, preview a restore, replace learner data or change the Backup page. Those flows need separate UI and persistence work. Success and failure leave the input bytes and existing snapshots unchanged. Successful exports pass the same import validator.

## JSON structure

The smallest valid backup is:

```json
{"schemaVersion":1,"decks":[],"cards":[],"reviews":[]}
```

A populated backup uses these exact case-sensitive fields:

```json
{
  "schemaVersion": 1,
  "decks": [
    {
      "id": "00000000-0000-0000-0000-000000000001",
      "title": "Vocabulary",
      "description": "Optional notes"
    }
  ],
  "cards": [
    {
      "id": "00000000-0000-0000-0000-000000000002",
      "deckId": "00000000-0000-0000-0000-000000000001",
      "front": "Question",
      "back": "Answer",
      "schedule": {
        "level": 0,
        "dueAt": "2026-11-27T12:00:00.1234567+00:00"
      }
    }
  ],
  "reviews": [
    {
      "id": "00000000-0000-0000-0000-000000000003",
      "cardId": "00000000-0000-0000-0000-000000000002",
      "sessionItemId": "00000000-0000-0000-0000-000000000004",
      "rating": 1,
      "reviewedAt": "2026-11-27T12:00:00Z"
    }
  ]
}
```

Every shown field is required, including `description` (use an empty string for no description). Collections may be empty. Null collections, rows, schedules and values are rejected. Unknown fields, duplicate JSON properties, unsupported versions, comments, trailing commas, malformed JSON and invalid Unicode/UTF-8 are rejected. Parsing permits at most 16 nested JSON containers. Integer fields reject quoted numbers, fractions and exponent notation.

All identifiers are nonempty UUIDs in the hyphenated `D` format. IDs must be unique within each entity collection. Review `sessionItemId` values must also be unique across all reviews. Each `card.deckId` must identify a deck, and each `review.cardId` must identify a card.

Deck titles are trimmed and require 1–80 UTF-16 code units. Card front/back values are trimmed and require 1–4000 each. Descriptions preserve whitespace and allow 0–500. Unicode, internal whitespace, literal markup and composed/decomposed text remain unchanged. Export rejects unpaired UTF-16 surrogates instead of replacing source text. No normalization or silent truncation is performed.

Schedule levels are integers 0–5. Ratings are `0` (Again) or `1` (Remembered). Timestamps use `yyyy-MM-ddTHH:mm:ss` with an optional fraction of 1–7 digits and an explicit `Z` or `+00:00` suffix. Offsetless, nonzero-offset and negative-zero-offset values are rejected before domain constructors run. All UTC ticks, including the minimum and maximum representable instants, are preserved. There are no additional card/review chronology constraints.

Export reads only the listed learner-data fields using explicit JSON operations. It does not inspect device paths, credentials or other application state. Learner text is exported as supplied, including any sensitive information the learner put in those fields. JSON escaping can change how characters are represented in the file; their text values round trip unchanged. Arrays retain their supplied order.

## Errors and integration

Invalid backups or inconsistent exports throw `ArgumentException` with a useful collection/field path, such as `cards[0].schedule.dueAt`, and a reason. Syntax/encoding/size errors identify `backup`; schema-version errors identify `schemaVersion`. Messages do not echo learner field values or unknown property names. Null snapshot arguments throw `ArgumentNullException`.

A caller can validate bytes and inspect the resulting counts before asking for restore confirmation. This API intentionally has no persistence operation; validation alone cannot modify a study store. File selection, download, confirmation and atomic replace-all persistence remain outside this codec.
