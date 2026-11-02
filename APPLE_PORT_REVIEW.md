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

## Follow-up: future-version rejection test source

After the initial port commit `61e998245752846f6e8be070b3475dc79c500457`, the two Apple test-file changes from `1875f8f22b113d0acb6ff7cbfeb5f314addefe7b` (direct child of `db996ea`) were applied. The complete Apple tree now matches `1875f8f` rather than `db996ea`. The change adds v8/v9/v10 inputs to the existing UI reader-rejection test and a synthetic load-state test that checks the previous model/source snapshot and temporary input files remain unchanged. The latter exercises error-completion state directly; it does not itself invoke the real parser. No application or schema code changed. Both source tests remain unexecuted here because no Apple toolchain is available. The two files passed a targeted credential-pattern scan and `git diff --check` before committing.

## Follow-up: desktop Stitch change

The three-file change from `9ef61a5a3ed045143dd09fde95cc0e7a92a74029` was applied without conflicts after the Apple test-port commit `573dc9eb0f0c67f05809c4fdbefa03f39e4b67a0`. Its parent is exactly the pinned desktop base `d8a1ea6`; it adds no intervening dependency or seam branch. Scope: `DesignRequestWindow.cs`, `DesktopVerification.cs`, and `KNOWN_ISSUES.md`. The desktop source/tests/workflows and known-issues file now exactly match `9ef61a5`, while the Apple subtree remains exactly `1875f8f`. The earlier unchanged-desktop statement describes the initial Apple-only port, not this follow-up.

The change tracks the selected model text, asks before changing a model inferred from loaded notes or appending after ambiguous manual edits, prevents duplicate quick-add notes, adds a conversation shortcut, and extends desktop verification source. It does not add persistent model identity or change the project/request schema. Confirmed ambiguous selections can intentionally retain multiple model descriptions; the source commit documents that limit.

The source patch and ancestor chain were reviewed, the three-file scope was checked, and targeted credential scanning plus `git diff --check` completed without findings. No build or test was run in this isolated worktree by this agent: AGENTS.md reserves builds/tests for the coordinating root unless explicitly delegated. Tests recorded in the source commit's known-issues text are historical source-branch evidence, not a new integration run. No paid CI, signing, upload or visible UI action was used. Apple toolchain validation and v8/v9 project interoperability remain unverified.

## Follow-up: compact Atelier cards

The two-file patch from `e5ed480fe159a1df70aeb1066fa73cc737a9648e`, direct child of `9ef61a5`, was applied without conflict after integration commit `bd670cf7be4f258a5e75d1a8ad932a794ba12574`. It updates only `DesignRequestWindow.cs` and `DesktopVerification.cs`: compact windows collapse supplementary headings/descriptions and reduce artwork height to expose a complete model choice; corresponding verification source is updated. The desktop source/tests/workflows and known-issues snapshot now matches `e5ed480`; Apple remains exactly `1875f8f`. Scope, credential-pattern, whitespace and source-tree equality checks passed. Builds/tests and visible UI checks were not run by this agent; the coordinating root owns those checks. No seam changes, other branches, paid CI or WearCAD data were involved.

## Follow-up: reject foreign physical-contour data

The five-file patch from `57b935026d7e3754dd152b5ff9f6f8c16119ba86`, direct child of `e5ed480`, was applied conflict-free after `6ef6fa4994ef2d0d8c3be63472167d91d88baca3`. It changes the two desktop UI/verification files, `ProjectStorage.cs`, `PhotoTraceProvenanceTests.cs`, and `KNOWN_ISSUES.md`. Compact model artwork increases from 45 to 70 pixels. The project reader rejects a piece's `PhysicalFeatures` field, including null and unversioned input, rather than silently dropping data belonging to a different experimental schema. Synthetic test sources cover v8 rejection with/without photo history, null/unversioned rejection, source-file preservation, and preservation of the currently open document on rejection. This is a rejection guard, not a seam merge, converter, new version allocation or resolution of schema interoperability.

Desktop source/tests/workflows/issues now match `57b9350`; Apple remains exactly `1875f8f`. Scope, sensitive-pattern and whitespace checks passed before commit. Build/tests were not run by this agent; root is the build/test coordinator. No paid CI or visible UI operation was performed.
