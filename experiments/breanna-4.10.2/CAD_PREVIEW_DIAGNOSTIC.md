# Synthetic CAD geometry inspection

This isolated probe reads a validated synthetic worker response, preserves the three piece identities and move, line, cubic and close ordering, and applies an explicitly supplied affine matrix to coordinates in millimetres. It does not parse an SVG transform chain. Its bounds enclose the control points and therefore the cubic curves; they are conservative, not exact extrema.

The numeric checks independently evaluate cubic Bernstein polynomials and a known midpoint under a rotation, scaling and translation. Invalid units, singular/nonfinite matrices, duplicate identities, open paths and unknown commands are rejected. The actual synthetic source contains a zero-length line; it is retained for inspection, not approved as valid topology. Closure, self-intersection, degeneracy, sewing meaning and cutting boundaries remain unverified. The gate to a CAD preview always rejects the result. No PatternPiece, project persistence, nesting, export or UI integration is provided. The diagnostic loss summary does not reproduce the complete upstream loss manifest.

Run through `run-parallel-tests.ps1` with the local dotnet executable; this uses only synthetic input and does not transmit data.
