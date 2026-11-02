# Isolated Apple source port — 2026-11-02

This is an integration candidate, not a release or a cross-platform compatibility approval.

## Pinned inputs and scope

- Desktop base: `d8a1ea60b393a7939548829d5f52069fe7a1102d`.
- Apple source: `db996eaf5ffac8d57c797ef15e53873a6a449750`, which includes `f81500c410f39036cb3bd504771150736a5a5acf`.
- Branch: `integration/apple-port-d8a1ea6-20261102-a06`.
- Worktree: `C:\Projects\TekstilCAD\work\apple-port-d8a1ea6-20261102-a06`.
- The complete tracked `Apple/` tree is taken from the pinned Apple source: 71 changed paths compared with the desktop base. This includes app helpers, request UI, synthetic fixtures, tests, SQLite prototype, icon, and Apple documentation. Taking the full subtree preserves its internal source dependencies.
- Desktop `src/`, desktop tests, root schema definitions and existing workflows are unchanged. No seam branch was merged. No existing active worktree, WearCAD installation/data, user project, credentials, signing setup or cloud service was used or modified.

## Source-level checks

- Staged Apple tree exactly equals `db996ea:Apple`; no accidental mixed-version Swift files.
- All source/resource paths named by `Apple/Tests/ci-overlay.yml` exist.
- `Apple/project.yml` is unchanged between the desktop base and Apple source. Its directory source declaration includes the added app Swift files.
- The existing Apple CI/TestFlight workflow files are also unchanged between these inputs. No workflow was started and nothing was pushed or published.
- SQLite prototype has no remote Swift package dependency; its SQLite module uses the platform library. The prototype is independent of the app directory source declaration.
- `git diff --cached --check` reports one inherited extra blank line at the end of `Apple/Tests/Fixtures/PORTABLE-REQUEST-README.md`. It is preserved to keep the source subtree exact; no other whitespace diagnostic was reported.

## Compatibility boundary — unresolved

The desktop base accepts `.tcad` project envelope versions 1 through 9 (`src/TekstilCAD.FileFormats/ProjectStorage.cs`). The ported Apple reader accepts version digits 1 through 7 (`Apple/TekstilCADViewer/TekstilCADViewerApp.swift`, `envelopeVersion`). Consequently, desktop v8/v9 files must not be advertised as Apple-readable. This port deliberately does not widen the Apple version gate, remap v8, or merge seam changes. The v8 schema conflict must be resolved independently with explicit schema ownership and migration tests before any such integration.

Both sources use the separate `tekstilcad.design-request` v1 document contract with prompt, measurements and optional reference. This source inspection alone does not establish equivalent parsing, semantic measurements, image behavior or round-trip compatibility. Bundled fixtures are synthetic and remain available for future verification.

## Validation not performed

Swift, Xcode and XcodeGen were not available in this Windows session. No Mac/iOS compile, Swift tests, simulator test, signing, actual phone test, file-provider test, performance test or native rendering verification was performed. Desktop builds/tests were not run by this agent under the repository build-coordination rule. The presence of test files is not a passed test result.

Before integration approval, run the Apple test scheme on an authorized Mac with the pinned candidate, run desktop synthetic request interoperability checks, and resolve the project-version boundary. Do not infer production readiness from a successful source-tree port.
