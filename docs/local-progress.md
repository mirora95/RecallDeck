# Due summaries and local review history

Home shows cards due now in total and in each deck. Due means the stored UTC due instant is at or before the injected current UTC instant. One refresh uses one coherent storage snapshot and one clock instant; all deck rows describe that same read. Decks sort by title ignoring case, then ID. When nothing is due, Home shows the earliest future due time, or explains that no cards are scheduled and offers a deck/card destination.

Progress counts Again and Remembered across every review event currently retained on this device. It lists at most the most recent 20 actual events, ordered by UTC review time descending, then event ID ascending for ties. Each row shows the current question, deck, saved rating and review time. These are saved actions, not scores, streaks or evidence of learning effectiveness. Editing a question changes its displayed history text. Deleting a card or deck removes its linked events and reduces these totals. Restoring a backup replaces the current history. Defensive unavailable labels avoid broken deck links if a malformed snapshot contains an orphan event.

Both pages show loading and recoverable read failures without claiming a failed read is zero. Try again rereads saved data; it never resets or writes study data. Navigating back from Review recreates the page and reads the saved rating/schedule. Android resume also rereads through the shared lifecycle signal. Pending reads coalesce; disposed pages ignore late results and unsubscribe. The explicit Refresh button updates a page left open as time passes. Browser tab visibility does not trigger a separate background listener or timer in this version.

Storage and HTML `datetime` values remain UTC. Visible dates use `TimeProvider.LocalTimeZone`, `TimeZoneInfo.ConvertTime`, an explicit English date format and the offset at that instant, for example `28 Nov 2026, 00:30 (UTC+05:00)`. Counts change when refreshed, not continuously while the page is open.

## Verification

`dotnet test tests/RecallDeck.Core.Tests -c Release` covers the inclusive due boundary, multiple decks, empty/future schedules, stable order, one read/clock, retained totals versus the 20-row limit, real deletion, orphan fallback, escaped text, non-UTC date/day conversion without timestamp mutation, loading/failure/retry, returning from an actual saved review, native resume callbacks and disposed/coalesced read races without writes.

For a published web or Android runtime check, create an owned deck with two cards, verify the Home total and per-deck count, then reveal and rate one Again and the other Remembered. Home should show no cards due and the ten-minute next time; Progress should show two actual events with one of each rating and local dates carrying the real device offset. Reload/restart and confirm persistence, then delete only the owned fixture and check that its linked history disappears. Leave every pre-existing record unchanged. Check both desktop and 360px web widths; an Android build is not runtime evidence.

Useful semantic selectors are `#due-total`, `.due-deck-row`, `.deck-due-count`, `time#next-review`, `#again-total`, `#remembered-total`, `#recent-reviews .history-row`, `.history-question`, `.history-deck`, `.history-rating`, `time.history-time`, `#refresh-overview` and `#retry-overview`.
