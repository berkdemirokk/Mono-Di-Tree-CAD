# Back side seam candidate trace

Only the back waist-to-armhole side is investigated; waist seam and dart closure are outside scope. In pinned Breanna 4.10.2 `back.mjs` the seam source constructs `.line(points.waist).line(points.armhole)` and separately stores `backSideSeamLength` as their distance. The adapter looks for the unique line with singleton named endpoint coordinate candidates in the existing synthetic 15-case matrix. This is source-consistent evidence, not proof that runtime operation identity or garment sewing semantics were independently traced: every result remains unresolved.

The matrix bytes are pinned by SHA-256 and privately copied before parsing; the matrix records the expected source hash. This verifies the archived bytes, not the installed source files or all transitive dependencies. Source and matrix are not signed. All options and body/variant identities remain visible. Coordinates are local mm, unplaced; length is binary64 and not a certified bound. No matching to front or skirt, tolerance, fit, export, cutting, production or device authorization is provided. Missing or ambiguous fields fail closed.

Synthetic tests require the existing pinned matrix and do not modify it. In PowerShell:

```powershell
$env:BREANNA_MATRIX_EVIDENCE='C:\Projects\TekstilCAD\work\dress-four-piece-plan-20261105\matrix\matrix-evidence.json'
node --test experiments/breanna-4.10.2/back-side-trace.test.mjs
```
