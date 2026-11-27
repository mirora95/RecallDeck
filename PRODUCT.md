# RecallDeck

An offline study app for self-directed learners on Android and the web. The primary task is creating question/answer cards and returning for short scheduled reviews. No accounts, telemetry or cloud service in the first release.

The current foundation has shared Home, Decks, Review, Progress and Backup routes. The shared Decks page lists, creates and renames local decks with labeled forms, inline validation, loading and empty states, retained input after a failed save, retry and guarded submissions. Card editing, reviews, progress and backups remain planned; expose no inert controls pretending to be working features. The authoritative behavior and release rules are in the product design under `docs/superpowers/specs`.
