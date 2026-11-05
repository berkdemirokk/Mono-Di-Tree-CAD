# Synthetic anchor–curve diagnostic

This isolated Node module accepts the local millimetre edge records produced by
the existing `dress-four-piece-plan-20261105/edge-diagnostic.mjs` without copying
or modifying that research. It examines line, close and cubic control polygons
with bounded de Casteljau subdivision. Callers must explicitly supply threshold
in mm, parameter interval width, maximum depth and total node budget.
Input is capped at 1,024 uniquely identified edges, 128 UTF-16 units per ID,
and absolute coordinates and thresholds of at most 10¹² mm. The entire input is
validated before search. Candidate count is bounded by the total node budget;
if it is exhausted, the current edge and every unexamined edge are reported.

The result is always `unresolved`: returned parameter intervals are possible
locations, not proved matches or a unique nearest point. Zero-length edges,
exhausted budget and numerical failure are retained as unresolved. A node whose
floating-point control box is farther than the threshold plus a roundoff margin
is pruned; because the margin is not a proved outward error bound, even absence
of candidates is not a formal exclusion. The values do not identify a sewing
role, notch direction, pattern, fit, production or export suitability. Inputs
must be stable during the call; no customer data, network or files are needed.

Tests are intended for `node --test anchor-diagnostic.test.mjs`. They are
synthetic checks, not a substitute for a rational/interval oracle or a source
fixture integration. An optional test reads the existing synthetic edge evidence
path from `ANCHOR_SYNTHETIC_FIXTURE`; it does not write the fixture. Future work
should independently enclose binary64 arithmetic and separately
review multiple minima, tangencies and sewing semantics before any CAD path.
The optional check requires the known SHA-256 fixture, exactly two panels,
25 edges and three anchors, and checks the recorded endpoint coordinates and
candidate IDs; a different fixture fails rather than inheriting those claims.
Each diagnostic result records the explicit depth and total node limits.

`dyadic-box-oracle.mjs` is a separate exact-arithmetic research checker. It
decodes each finite binary64 into an integer multiple of 2⁻¹⁰⁷⁴, subdivides
control polygons with BigInt dyadic arithmetic, and excludes an entire edge
only when every visited control box is strictly farther than the encoded
threshold. Equality, zero-length edges and budget/depth exhaustion stay
unresolved. An unresolved box does not prove contact. The exact claim concerns
only supplied binary64 local coordinates and the mathematical polynomial or
line they encode; it does not cover upstream rounding, source identity, notch
semantics, physical tolerances or suitability for production.

Work is limited to 1,024 edges, 50,000 total nodes, depth 32 and coordinates or
thresholds within ±10¹² mm. BigInt operation cost grows with dyadic depth and
binary64 exponent; these are resource limits, not a latency guarantee. It
holds a bounded subdivision stack and output and returns untouched later edges
as unexamined if the shared node budget ends. Optional fixture testing requires
the same pinned synthetic hash described above.

`matrix-oracle-report.mjs` separately reads only the pinned synthetic 15-case
matrix and checks the oracle file hash before generating a new file with exclusive
creation. It imports the exact verified bytes as a data module, so later changes to
the file path do not alter that run's oracle. Example: `node matrix-oracle-report.mjs input.json new-output.json 0.01 8 1000`.
All three limits are mandatory. The output records 15 cases, 30 panels and 45
anchor results, source and oracle hashes, local-space part/edge identities, and
both excluded and unresolved edges; each result must contain every source edge
exactly once. Set `ANCHOR_MATRIX_FIXTURE` to the original
matrix file for the optional fixture test. The exact test oracle remains a
geometric exclusion check for encoded values, never a semantic identity or fit
certificate. Each anchor gets its own node budget; total runtime scales with
45 times that budget, so large limits can be expensive.
