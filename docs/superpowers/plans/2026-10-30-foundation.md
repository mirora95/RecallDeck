# Shared foundation implementation plan

Goal: close issues #1–#3 with one coherent foundation PR, without implementing deck or review behavior early.

- [x] Scaffold .NET 10 Core, shared Razor UI, standalone WebAssembly PWA, Android-only MAUI host and tests in an isolated worktree.
- [x] Add a shared landing render test; observe it fail because the shared landing component does not yet exist.
- [x] Implement one honest shared landing page and root router; point both hosts to that assembly and its static assets. Remove generated samples and unused platform targets.
- [x] Build shared/web projects, run tests and build Android with JDK 21 / SDK 36. Document exact commands and actual limits.
- [ ] Add least-privilege web/test and Android build CI, pinned action revisions and setup instructions. Check a hosted run at the final head.
- [x] Inspect desktop/narrow web rendering once, fix concrete issues, retain text evidence and remove temporary validation media.
- [ ] Independently review the complete diff, resolve findings, verify the final head, create PR and merge only after required checks/reviews pass.

Main paths: `src/RecallDeck.Core`, `src/RecallDeck.UI`, `src/RecallDeck.Web`, `src/RecallDeck.Mobile`, `tests/RecallDeck.Core.Tests`, `.github/workflows/build.yml`, `global.json`, README. No storage, accounts, remote deployment or release signing in this slice.
