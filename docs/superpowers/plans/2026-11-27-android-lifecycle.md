# Android back navigation, keyboard and resume (issue 29)

## Goal and scope

Complete the existing Android study journey without changing the product's storage or review rules. Use the existing authorized checkout and an issue-number-first branch from current main. No new checkout/worktree, Windows input, user browser profile, AVD launch, private data, new account/service or public deployment.

The user delegated implementation of all remaining RecallDeck tasks and their routine design choices. Dependencies #16, #20 and #27 are delivered. Project synchronization remains pending because the token lacks Projects scopes and desktop control is prohibited.

## Observed behavior

The dedicated UI host at /decks backgrounds on Android Back instead of returning through routes. Reopening retains the same PID and /decks route. This read-only precheck is recorded outside Git. Previous native captures also show the unchanged header/status-bar overlap. MAUI 10 ContentPage defaults to edge-to-edge and the current host does not explicitly set SafeAreaEdges.

## Approved approach

1. Add a small shared route history owned and disposed by App. Record successful LocationChanged events only. Native MainPage dispatches Back through BlazorWebView.TryDispatchAsync to the scoped route service. Serialize rapid presses. Consume Back while dispatch is pending; only a resolved clean root/no-history result may background the Android task. A failed dispatch keeps the app visible with a retryable message. Prevented navigation never pops history prematurely.
2. Add one small UnsavedChanges Razor component using NavigationLock and visible Stay/Leave confirmation. Deck and card forms provide actual dirty comparisons and busy state. Cover create, rename, edit, explicit cancel and opening an existing card over a new draft. Stay retains exact input; clean/reverted/successfully saved forms do not warn. Failed saves remain dirty. Block route navigation during all writes, including deletion; release the guard before a successful internal redirect. Do not add a generic form framework or dependency.
3. Set the shipping MainPage SafeAreaEdges to All. Link the same production host page/bridge into UiSmoke rather than duplicating behavior. Keep controls in normal scrolling layout; no hardcoded status-bar or keyboard dimensions.
4. Dispatch MAUI Window.Resumed to a small shared async notification with disposal/version guards. Review refreshes an idle empty/completed queue after resume, but never inserts cards into an active captured queue or resets a revealed answer, pending rating or safe retry. Do not add clock hooks. Home due counts belong to the following #21 task and will consume the same refresh signal.

## Test-first implementation tasks

- Red unit/component cases for successful versus prevented history/back, clean/dirty/reverted drafts, Stay/Leave/cancel, save failure/pending writes, edit-over-draft and successful deletion redirects.
- Red resume cases for newly due empty/completed queues, repeated resume, active/revealed/failed/inflight review preservation and disposed components.
- Implement the smallest complete behavior and adapt existing journey assertions only where the new warning intentionally changes user behavior. Preserve exact IDs, ownership and persistence checks.
- Run the full .NET and Node suites, published web regression/PWA checks, relevant Android builds and standalone shipping/UiSmoke packages. Use modest build parallelism. Parent owns emulator execution, final source snapshot, independent spec then quality review, scoped visual review, media cleanup and exact-head CI/merge guards.

## Actual native acceptance

Use only the existing emulator RecallDeck_delivery_20261105 (emulator-5554), supported Playwright Android/WebView and ADB virtual-device controls. No Windows mouse, keyboard, window or focus control. Preserve every shipping baseline record. Destructive fixtures belong only to the dedicated host after an empty baseline has been verified.

Verify route Back, dirty Stay/Leave, failed/pending-write behavior, keyboard-visible reachable editor actions and long review ratings, safe system bars, supported rotation, same-PID background/resume and cold restart. The emulator console advertises a supported rotate command; use it only when safe and restore the initial orientation. Do not change Android rotation settings or permissions.

For a real due boundary without system-clock changes or a ten-minute wait, restore an owned valid backup with DueAt set to actual UTC plus about 90 seconds. Show No cards due, background while other checks run, then resume after the boundary and verify the due card appears. A separate active queue fixture must not gain a newly due card on resume. These are synthetic backup inputs, not a claim of experimentally changing the scheduling algorithm. Retain exact stored fields and no duplicate review events.

Disclose emulator-only coverage and untested physical devices or assistive technology. Compilation alone is not runtime evidence. Remove temporary validation images after inspection and before push; preserve product assets and source images.

## Primary references checked 2026-11-27

- https://learn.microsoft.com/en-us/dotnet/maui/user-interface/safe-area?view=net-maui-10.0
- https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/app-lifecycle?view=net-maui-10.0
- https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/navigation?view=aspnetcore-10.0
- https://learn.microsoft.com/aspnet/core/blazor/hybrid/?view=aspnetcore-10.0

## Delivery

No force pushes, fake collaborators, administrator bypass or unrelated PR merges. Before each write/push/PR/merge verify mirora95, public mirora95/RecallDeck, exact origin, checkout and branch. Commit only reviewed/tested bytes, open a ready PR, wait for all exact-head checks/reviews and resolved threads, then merge with a head-SHA guard. Read back merge actor/commit and issue closure. Keep unverified Project status explicitly pending.
