# Android navigation and resume

The shipping app and isolated Android UI host use the same MainPage XAML, native Back bridge and Window.Resumed wiring. MainPage applies MAUI 10 SafeAreaEdges.All so system bars, cutouts and keyboard insets bound the scrolling WebView. No status-bar or keyboard height is hardcoded.

Android Back dispatches to the scoped shared route history. Only completed route changes enter history; a prevented dirty navigation cannot pop a route. Rapid native presses are serialized while dispatch is unresolved. A resolved root backgrounds the Android task. If dispatch fails, the page remains visible with a retryable message.

Deck creation, deck rename, card creation and card editing compare current input with the original text. Route changes warn with Stay and Leave and discard. Stay keeps exact text and returns focus to the editor. Canceling a changed rename/edit or replacing a new-card draft with an existing card also needs confirmation. Canceling an unchanged rename keeps any unrelated create draft. Reverted and successfully saved input does not warn; failed saves remain dirty. Navigation waits for in-flight writes, including deletion. Successful deck deletion redirects only after persistence succeeds.

Window.Resumed sends a coalesced async notification to the current Review page. An empty or completed review reloads due cards against the current UTC clock. An active captured queue, revealed answer, pending rating or failed rating retry remains unchanged. Resume only reads: it never rates a card or duplicates a persistence operation. Disposed pages unsubscribe and ignore late results. Home due counts belong to issue 21.

## Verification boundary

Component tests cover route history, actual NavigationLock prevention, draft comparisons, Stay/discard, failed and pending writes, successful deletion redirects, newly due empty/completed queues, active/revealed/failed/in-flight review preservation and disposal/coalescing. The published card journey also exercises a dirty route attempt and Stay before explicitly discarding an edit.

Native acceptance is separate: the parent delivery session checks real Back, keyboard-visible editor actions and ratings, safe system bars, supported rotation, same-process background/resume, a real future-due boundary and cold restart on the existing authorized emulator. Record its actual result at the final tested source hash; compilation alone does not prove these behaviors. Physical-device and TalkBack coverage are not implied. No clock hooks or device settings changes are required.
