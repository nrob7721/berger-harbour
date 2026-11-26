---
name: update-prompt-spec
description: Update the specification in prompt.md from a review-changes document (docs/review-changes/*.md), the pending quick frontend changes in docs/quick-changes/ledger.md, or a described change, by grilling the user on every open decision first. Use whenever the user wants to apply review changes, feedback, new requirements, a design decision, or "the quick changes" / "the ledger" to prompt.md / "the prompt" / "the spec", even if they don't name this skill.
---

# Update prompt.md

`prompt.md` is the full specification an agent builds the system from. This skill changes **only that spec**. Implementation happens separately, later.

## Rules

- **Only edit `prompt.md`** (and, when syncing the quick-changes ledger, the `Status` lines of the entries you handled). Don't touch source code, tests, config or other docs. Reading code is fine when a fact is needed (e.g. what the current behaviour actually is). Use cheap reads and greps only; never run tests, builds, installs or other expensive commands.
- **No history.** Write every change as if it had been decided that way from the start. Never write "changed from", "previously", "now", "v1.2", "new" or similar, and never keep the old behaviour alongside the new one. Remove anything the change replaces.
- **Ignore any "To consider" section** in a review doc. Those items aren't decided yet.
- **The current implementation doesn't constrain the spec.** Nothing is live, so a full rewrite of a section is acceptable when it gives a cleaner spec.
- Keep the existing style of `prompt.md`: terse, decisive, numbered decision log in §4, cross-references by section (§n), Australian English, `dd/MM/yyyy` dates.

## Workflow

1. **Read** the source of the changes (usually `docs/review-changes/<version>-review-changes.md`) and all of `prompt.md`. It's long, so read it in pages.
   - **Quick-changes ledger.** Also read `docs/quick-changes/ledger.md` when it exists and has entries with `Status: pending`, or when the user mentions quick changes. These are frontend tweaks already in the code but not in the spec (made by `quick-frontend-update`). Treat each pending entry as an agreed-and-built change to bring the spec in line with, not as a proposal, and handle them in order (a later entry may amend an earlier one). To see what an entry really did, use its files and hashes: `git diff <base> <commit> -- <files>` (or `git diff <base> -- <files>` if uncommitted); the diff is the source of truth where the entry's wording is thin. Skip entries marked `synced` or `reverted`.
2. **Grill first.** For ledger entries this is shorter: the user already decided the behaviour, so grill only the open points listed below. Invoke the `mattpocock-skills:grill-me` skill and work through every change with the user in rounds, before any edit. Cover:
   - ambiguities and edge cases in each change;
   - how it interacts with existing rules: decisions, invariants, API, emails, tests, seed data;
   - conflicts with other changes;
   - terminology (ubiquitous language), and where a rename has to reach.

   For ledger entries, put to the user:
   - every entry with a `Flag` (an experiment): make it permanent in the spec, or leave it out and plan to revert the code?
   - every `Stopgaps` item: should it become real backend/spec behaviour (a field, an endpoint, a stored value), or stay a frontend detail?
   - any `Backend impact: warned` entry: the spec will have to describe the backend side too;
   - entries that amend each other, and anything that contradicts a numbered decision in §4.

   Gather facts from the repo yourself, then put only the decisions to the user. Don't edit until they confirm a shared understanding.
3. **Edit `prompt.md`** with targeted edits, section by section. Every agreed decision must land everywhere it applies: glossary (§3), decision log (§4), domain model, API tables, notifications/placeholders, UI sections, seed data, tests (§15) and deliverables (§16).
4. **Sweep for leftovers.** Grep `prompt.md` for renamed or removed terms, endpoints and labels, and fix any stragglers.
5. **Close the ledger entries.** For each entry written into the spec set `Status: synced` (add the date); for one the user chose to revert, set `Status: reverted` and tell them the revert command (`git diff`/`git checkout <base> -- <files>`); don't revert code yourself, as this skill changes only the spec. Leave entries you didn't handle `pending`.
6. **Report briefly**:
   - what changed, by section;
   - any detail you had to decide that wasn't settled in the grilling, with the reason;
   - an offer to rename the review doc to `<name>-done.md` (the repo's convention) and to commit. Do neither without a yes.
