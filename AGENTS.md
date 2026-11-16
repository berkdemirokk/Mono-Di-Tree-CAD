
## WearCAD protection — explicit user requirement
WearCAD is read-only reference material. Never modify, delete, rename, overwrite or save any WearCAD installation, configuration or user-data file. Do not use WearCAD UI actions that save, export, change settings or send production jobs. Do not write to C:\WearCadfe, C:\Hpgl, C:\Nester, C:\Nester1 or C:\NST. Perform all development and tests under C:\Projects\TekstilCAD. If fixtures are needed, read originals only and use independent copies in the TekstilCAD workspace. Never overwrite originals.

## Build coordination and UI
Only the root agent runs builds/tests unless explicitly delegated, to avoid shared bin/obj file locks. User requested background work: do not launch, focus or automate visible windows without a new request. WearCAD remains read-only. Record unverified UI behavior honestly.

## Branch, merge and acceptance rules
Applies to every assistant (Claude, Codex or other). Plan and journey table: `docs/BITIRME_PLANI.md`.
- One trunk: `main`. Nobody pushes to `main`. Every change is a pull request from a short-lived branch cut from the current `origin/main`. No stacked PRs (a PR's base is always `main`) and no long-lived integration branches.
- Before starting: `git fetch` and `gh pr list`. If an open PR touches the same files, wait or coordinate; never open a second PR for the same files.
- One work item at a time while the finishing plan runs. Only items listed in `docs/BITIRME_PLANI.md` are worked on (feature freeze); new ideas go to its "after first release" list.
- Acceptance: for a user-visible change, the acceptance check is written first, from the user's step in the journey table of `docs/BITIRME_PLANI.md`, by a different agent/session than the one that writes the code. The check must drive the real window path end to end, not a detached control. A check is never weakened to make it pass.
- Review: a second agent reviews the diff before the PR is marked ready.
- Only the owner merges, tags and releases. Assistants open draft PRs whose description has a "Doğrulandı" and a "Doğrulanmadı" section.
