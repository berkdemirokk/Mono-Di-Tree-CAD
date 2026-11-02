# Isolated legacy project migration review

Status: pre-release prototype in a separate worktree. It is not merged into the Windows/iPhone integration branch. Production version-10 writes are deliberately disabled until the format number is formally allocated across all relevant products and deployments. Neither user approval nor an environment variable can enable the writer; an internal scoped override exists solely for synthetic tests.

## Version scan, 2026-11-02

The local scan examined 264 distinct commit heads from named refs and worktrees and 275 worktree source trees. The only version-10 project-format matches found were this prototype and its immediate schema prototype. Exact scan output is in the worktree's ignored `work/version10-*-scan.json` files. This does not establish uniqueness in unavailable, unpublished, deleted, or external repositories, deployed applications, or independently defined formats. An independent allocation decision and iPhone compatibility review remain release blockers.

## Opening and migration

The reader records the format number from the same byte snapshot that was parsed and fingerprinted. Historical version-8 physical and photo variants, and version-9 photo-plane projects, can open without changing their source files. When saving such a document as version 10, the UI requires an explicit migration decision, a different destination that does not already exist, and a final confirmation showing both source and target. The candidate is validated, serialized and read back before final confirmation. A change to the open document, pending work, or source file invalidates the approval.

The existing destination is never replaced by this flow. A missing-target version token makes a target created after selection fail closed. The source is reopened without write/delete sharing and its version is checked immediately before committing the destination. Failure or cancellation leaves the source and any pre-existing target unchanged; if the destination appears during the race, the newly appeared file remains untouched. The resulting file becomes the open project only after a successful write. Normal direct Save As and safe-copy entry points cannot bypass migration confirmation.

Because version allocation is not confirmed, normal version-10 saving and automatic recovery serialization fail closed. The open-project status and any recovery-failure warning explicitly say that saving and automatic recovery are unavailable; the warning advises keeping the session open and saving to a new file only after allocation is confirmed. A blocked recovery attempt does not mark a recovery file as written or delete an existing one. Consequently edits to a feature-bearing project cannot currently be durably saved by this prototype. This limitation must be resolved before release; do not infer that a test-only override constitutes authorization.

## Verification and remaining gates

`--verify-legacy-migration <absolute-work-report.json>` uses synthetic v8 physical, v8 photo and v9 photo-plane files. It drives the actual WPF Save button with scoped test-only choices, including denied allocation, both cancellation points, existing/same targets, source/document changes, injected errors, target races, bypass attempts and successful lossless new-file migration. The test override is active only in the isolated verification process. `LegacyFormatGateTests` checks that the default writer and recovery paths refuse version 10.

Independent review must examine the change and the captured version scan, confirm the final format allocation, test the real supported Windows and iPhone readers, and review migration language and accessibility. Full application integration, iPhone execution, all deployed versions, and resilience against noncooperating filesystem writers are not proven by this prototype. No actual project files or machine interfaces are required for the tests.
