---
name: quick-frontend-update
description: Make a quick, small tweak to the web frontends (web/booking, web/admin, web/shared) without touching the backend, and log it in a ledger so prompt.md can be brought in line with it later. Use whenever the user asks for a quick fix, demo fix, UI tweak, layout, styling, wording, label or small behaviour change in the booking app or admin site and says things like "quick", "demo", "bypass prompt.md", "just the frontend", "experiment", "tweak" or "same again, commit and edit", even when they don't name this skill. Do not use for changes that need the API, domain, database or spec decisions to change first.
---

# Quick frontend update

A fast path for small UI changes. The spec (`prompt.md`) is deliberately **not** updated now; instead every change is written to a ledger so that `update-prompt-spec` (or the user) can later find exactly what drifted from the spec and why. The ledger is the whole point: a change that isn't logged is invisible to the later sync.

## Scope: frontend only

- Edit only `web/booking`, `web/admin` and `web/shared` (source, styles, tests next to them), plus the ledger.
- Don't touch `src/` (the .NET backend), `tests/` (backend tests), seed data, `prompt.md`, or `docs/review-changes/*`. Backend work happens only when the user explicitly asks for it in this request, and then say so in the ledger.
- Why: the user is iterating on how things look and feel. Backend changes carry data, migration and API-contract consequences that a quick pass can't responsibly make.

## Workflow

1. **Orient cheaply.** Read the files the change touches (grep to find them). Check what the API already provides in `web/*/src/lib/types.ts` and `web/shared` before deciding anything is impossible. Don't read `prompt.md` in full; grep it for the terms involved only if you need to know current intended behaviour.
2. **Record the base.** Run `git rev-parse --short HEAD` and `git status --short` so the ledger can name the commit the change starts from.
3. **Check for backend impact before editing** (see below). Stop and warn if it applies.
4. **Make the change reversibly.** Prefer additive edits, a named constant or flag, and CSS over deletion. If the user says "experiment", "for now" or "don't delete code", hide behind a flag (e.g. `SHOW_X = false`) and skip tests with `it.skipIf(!FLAG)` rather than deleting them, so the change can be switched off or kept without archaeology. Keep one concern per ledger entry.
5. **Check cheaply.** Run the type check and only the affected test file(s) for that app (e.g. `npx tsc --noEmit -p .` and `npx vitest run <path>` from `web/booking`), filtering output to failures and the summary line. Update existing tests the change breaks; add one small test for new behaviour. No full builds, e2e or whole-repo runs unless asked.
6. **Write the ledger entry** (format below). Do it in the same turn as the change.
7. **Git.** Don't commit or branch unless the user asks. When they ask for "a commit for reference", commit everything except unrelated untracked files (e.g. `logs.txt.save`), then fill in the commit hash on the entry. Work on the current branch.
8. **Report briefly:** what changed in user-visible terms, test result (and what wasn't run), backend warnings, and anything in the ledger the user should know.

## Backend impact: warn, don't do

Treat the change as needing the backend if it requires any of: a field or endpoint the API doesn't return, persisting something new (a setting, a boat field, a flag), a new validation or business rule that must hold server-side (pricing, availability, deposits, booking state), email or notification content, auth or permissions, or seed/migration data.

When that happens:
- **Don't edit the backend.** Say plainly: "This needs a backend change: <what and where>."
- Offer the options, with a recommendation, e.g.: (a) a frontend-only stand-in (a constant, a value derived from existing data, a client-side default) clearly marked in the ledger as a stopgap; (b) the minimal backend change described precisely (field, endpoint, DTO, migration) for the user to approve; (c) leave it for the spec route (`update-prompt-spec`).
- Do (a) by default if it fully delivers the visible result and is safe; otherwise ask. A purely client-side stand-in must never enforce a rule that the server also has to enforce.

## Ambiguity: choose, say, and offer

If the request can be read more than one way, don't stall. Pick the reading that is smallest, reversible and frontend-only, implement it, and state the choice in one line with the alternative(s) ("I read 'hide tags' as hiding them everywhere; say if you meant closed cards only"). Use AskUserQuestion only when a wrong guess would be expensive to undo or would cross into the backend. Behaviour you leave in place on purpose (e.g. keeping an old click handler when asked only to remove its styling) goes in the report and the ledger.

## The ledger: `docs/quick-changes/ledger.md`

Append-only, newest at the bottom. Create the file with the header below if it doesn't exist. A later skill reads it to update `prompt.md`, so write each entry in terms of **behaviour a spec would describe**, not just code edits, and list the files so the diff can be recovered: `git diff <base> -- <files>` (or the entry's commit).

```markdown
# Quick frontend changes (not yet in prompt.md)

Each entry is a frontend-only change made without updating `prompt.md`. A spec-sync pass reads the entries with `Status: pending`, applies them to `prompt.md` (or decides to revert them), then sets `Status: synced` and notes the date. `Flag` entries are experiments: ask the user whether to make the flag permanent or remove the code before writing them into the spec.

## QC-<n>: <short title>
- **Date:** YYYY-MM-DD
- **Status:** pending            <!-- pending | synced | reverted -->
- **Request:** <the user's words, abridged>
- **Behaviour now:** <what a user sees/does, in spec language; one to four bullets>
- **Replaces:** <the behaviour it supersedes, or "nothing (additive)">
- **Spec sections likely affected:** <§ numbers or headings in prompt.md, from a quick grep; "unknown" is fine>
- **Files:** <paths>
- **Base / commit:** <base hash> → <commit hash, or "uncommitted">
- **Flag:** <constant name and file, or "none">
- **Backend impact:** none | warned: <what is needed> | done on instruction: <what>
- **Stopgaps:** <client-side stand-ins that should become real backend/spec behaviour, or "none">
- **Tests:** <added / updated / skipped (and why)>
```

Number entries consecutively (QC-1, QC-2, ...). If a later change modifies an earlier one, add a new entry that says which it amends rather than editing the old one, so the history stays recoverable; the sync pass resolves them in order.

## Backtracking from the ledger (for the spec-sync pass)

To see exactly what a pending entry changed: `git diff <base> <commit> -- <files>`, or `git diff <base> -- <files>` while uncommitted. To undo one: restore those files at `<base>` (check later entries don't overlap the same files first). The ledger, not commit messages, is the source of intent.
