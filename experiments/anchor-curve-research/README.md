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
