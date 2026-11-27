# Accessibility and narrow-screen audit

Issue [#30](https://github.com/mirora95/RecallDeck/issues/30) audits the shared shipping UI. The existing warm reading surface, navigation and workflows remain in use. Standalone buttons and links now provide at least a 48×48 CSS-pixel activation area; sentence links keep their inline text layout.

## Reproduce the browser checks

Use the pinned Playwright Core and published host described in [testing.md](testing.md):

```sh
npm ci --ignore-scripts
npx --no-install playwright-core install --with-deps chromium --only-shell
dotnet publish src/RecallDeck.Web -c Release -o artifacts/audit/web
python scripts/serve-published.py artifacts/audit/web/wwwroot --port 5189
```

In another terminal:

```sh
npm run test:accessibility -- http://127.0.0.1:5189
npm run test:ui -- web http://127.0.0.1:5189
```

The accessibility driver accepts one HTTP loopback origin, blocks requests to other origins, creates a fresh headless browser context and waits for the published service worker. It never opens a user profile or sends operating-system input. Synthetic study data exists only in that temporary browser context, which closes after the run. Reports contain measurements and check results as JSON; no screenshots or photos are created. CI runs the driver against the published shipping web host.

## Tested checklist

- Home, Decks, Review, Progress and Backup empty states at 360 and 1280 CSS pixels, each with reduced motion enabled and disabled.
- Tab and reverse Tab follow semantic document order; each keyboard target has a visible outline. Enter on the skip link focuses the main landmark.
- Enter/Space create a deck and card, open a deck, cancel deletion, reveal an answer, save a rating and cancel a backup preview. Reveal focuses Answer; review completion focuses the summary heading.
- Real computed foreground/background colors meet text contrast thresholds of 4.5:1, or 3:1 for large text. Disabled controls are excluded from this contrast check.
- Visible form controls have associated labels and valid description references. Invalid deck/card fields expose `aria-invalid`, linked hints and inline alerts.
- Long unbroken Unicode deck titles/descriptions and 4000-UTF-16-unit question/answer content wrap in deck/detail/review/history/Home states. Relevant controls remain within the viewport width.
- Standalone controls meet the 48×48 CSS-pixel product target. Sentence links are recorded separately under the inline exception; they are not expanded into block controls.
- Invalid backup selection provides an alert; an empty backup preview names the destructive effect and supports explicit cancellation.
- A controlled IndexedDB-open delay exposes the real deck loading state; a controlled unavailable-storage failure exposes recovery. The original browser method is restored before keyboard retry reads the saved deck.
- Both motion preferences report no active app animations. This app currently uses no authored interaction animation, so no motion suppression rule is needed.
- The existing card journey separately checks escaped HTML-like Unicode, injected failed save and retry, edit/reload, unsaved navigation, canceled/confirmed card and deck deletion, and preservation of unrelated records. Shared component tests cover additional loading, busy, failure and backup states.

The regression test first failed against the prior published UI: brand 32px high, navigation about 44.8px, create/edit/delete buttons about 46.8px, and standalone return/deck links about 21–30px. The shared CSS and standalone link classes correct those measured targets. Keyboard order, labels, contrast and wrapping passed before the target fix.

## Scope and limits

This is a focused regression audit, not WCAG certification or an automated proof of accessibility. [WCAG 2.5.8](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html) specifies 24 CSS pixels with exceptions, including inline links. The larger product target follows [Android's 48dp touch-target recommendation](https://developer.android.com/guide/topics/ui/accessibility/apps); browser CSS measurements alone do not establish native dp bounds. Native accessibility bounds and key actions require a separately recorded Android run.

Contrast checks cover the app's opaque solid backgrounds and rendered text. They do not simulate vision conditions, inspect screen-reader speech, certify every visual state or test a different theme. The browser matrix covers the named sizes, not every device, zoom or operating-system font scale. TalkBack, other assistive technology and physical-device testing are not claimed by this driver. Android compilation is separate from runtime evidence. PWA offline/update behavior remains covered by [pwa.md](pwa.md), rather than by this accessibility driver.

References used for this checklist: [focus order](https://www.w3.org/WAI/WCAG22/Understanding/focus-order.html), [text contrast](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html), and [interaction animation](https://www.w3.org/WAI/WCAG22/Understanding/animation-from-interactions.html).
