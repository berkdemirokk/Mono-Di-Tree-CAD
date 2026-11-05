# Synthetic bodice response reader

Isolated research parser for `bodice-review/server.mjs` calculate responses: exactly two uniquely named front/back parts, local millimeter coordinates, explicit false approval, ordered move/line/cubic/close operations, strict object fields and finite bounded numbers. Input is copied before parsing; bytes, JSON depth, per-piece operation count and coordinate magnitude are bounded. No process, network, renderer, app menu, real user measurement or output integration is present. No production or export authorization is inferred.

The parser does not verify curve topology, nonintersection, physical units or fit, source/worker provenance, or that an untrusted producer told the truth. Syntactic close does not establish a valid garment contour. The returned pieces and segments are read-only snapshots. These limits must be maintained by any separate visual consumer.

Test command: `dotnet test experiments/bodice-wpf-reader/tests/BodiceWpfReader.Tests.csproj`.
