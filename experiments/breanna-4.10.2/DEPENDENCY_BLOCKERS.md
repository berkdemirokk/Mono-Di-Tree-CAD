# Offline dependency blocker report

Research only; no download, process launch, product integration or personal data. The runner reads the checked-in 20-package pnpm lock and historical registry/tarball observations, plus an explicitly selected installed package root. It separately records lock integrity, historical assertions, installed per-file SHA-256 manifest and detected notice filenames/hashes; a missing/extra/version-mismatched/linked or unreadable package cannot establish provenance. The existing bounded two-scan manifest implementation is reused. Historical boolean `match` is an unsigned observation, not a fresh tarball hash check. The installed tree is not compared to an authenticated archive. Additional filesystem races, Windows reparse behavior and module resolution are not proven.

Optional runtime executable and notice are separately hashed; a notice file existing does not establish that it covers that executable. Package notice discovery does not establish ownership, completeness, attribution or license compatibility. Every item remains unresolved and execution, distribution and personal-data authorization are always false. The JSON can expose local file paths; do not feed sensitive files to this research utility. No actual Node binary is run.

From this worktree root, synthetic tests: `node --test experiments/breanna-4.10.2/dependency-blockers.test.mjs`. Example read-only inspection of the existing local research packages (optional runtime and notice can be omitted):

```powershell
node experiments/breanna-4.10.2/run-dependency-blockers.mjs C:\Projects\TekstilCAD\work\dress-research-c494587\experiments\breanna-4.10.2\node_modules
```
