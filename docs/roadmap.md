# Roadmap

## Sprint 1 — foundation and reliable local data

**2026-10-30 through 2026-11-12**, Asia/Tashkent.

Goal: build both hosts and create, edit and persist decks/cards through the shared UI. Establish core tests, web CI and Android build CI before feature work is merged. Review capacity at sprint end; unfinished issues carry forward explicitly.

## Sprint 2 — useful review and recoverable backups

**2026-11-13 through 2026-11-26**, Asia/Tashkent.

Goal: complete the reveal/rate review loop, local progress and versioned backup/restore. Verify offline web and Android lifecycle behavior, then assess v0.1 readiness. Pull eligible next-sprint tasks early only after current-sprint dependencies are complete.

## Working agreement

- GitHub issues are the task source of truth. Each has observable acceptance criteria, dependencies, priority, size and verification.
- GitHub Project status: Todo → In Progress → In Review → Done. Use a `blocked` label with a concrete reason when progress requires external input. Todo issues are ready only when dependencies and acceptance criteria are clear.
- Aim for two tasks per daily session, with a third only if ready and safely verifiable. Never split trivial work just to meet a count.
- At most one implementation branch is actively being changed by a session. A cohesive PR may close two or three related issues. Otherwise use separate scoped PRs.
- Revisit existing PRs and failed/pending checks before starting another branch. Unfinished work does not become Done at day-end.
- At each sprint boundary, record completed scope, carried work, defects and one useful process adjustment; create the next dated milestone and groom issues without fabricating velocity.

## Later, after a usable v0.1

Decide from actual usage whether to add import formats, richer organization, localization or optional sync. Accounts, AI generation, notifications and store publication require separate scoped decisions.
