# Contributing

Choose an issue whose dependencies are complete. Describe the expected behavior and smallest solution before coding. Keep unrelated cleanup out of the pull request.

## Workflow

1. Move the issue to In Progress and assign yourself. Use a branch such as `12/card-validation` from current `main` in an isolated checkout.
2. Add meaningful regression or contract tests where behavior can fail, implement the acceptance criteria, and run the relevant checks.
3. Review the full diff. Remove temporary validation screenshots/photos and exclude build outputs, secrets and local learner data.
4. Open a ready PR to `main`, link issues using `Closes #N`, describe behavior, tests and verification limits, and move issues to In Review.
5. Obtain an independent code review. An agent review may be recorded honestly as such; it is not a GitHub approval by a different human. Resolve blocking findings and rerun affected tests.
6. Merge only the exact verified head after current required checks and reviews pass. Never bypass protections. Mark Done after the merge is read back and linked issues are closed.

## Definition of ready

Acceptance criteria are observable, dependencies are closed and delivered, scope fits a small PR, and verification is feasible. If not, refine or block the issue instead of guessing.

## Definition of done

Acceptance criteria pass, relevant tests/builds pass at the reviewed head, documentation describes actual behavior, no unresolved blocking review findings remain, CI is green, and the PR is merged into the intended base. A feature requiring runtime verification is incomplete until that verification exists. Document emulator/device coverage explicitly.

Use the README's test/web publish and Android compile commands and inspect both CI jobs at the PR head. Do not advertise unexecuted commands as verified. AI-assisted contributions are welcome with accurate descriptions and human/agent review attribution. Never invent co-authors, usage results or test evidence.
