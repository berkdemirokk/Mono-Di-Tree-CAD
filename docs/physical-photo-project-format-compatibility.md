# Physical contour and photo history format compatibility

Status: isolated prototype only. This worktree includes a version-10 reader/writer prototype; it has not been integrated into either active branch or approved for release. Do not deploy either branch's version 8 writer as a combined format.

Two independently developed formats use version 8: the physical-contour branch requires at least one `Pieces[].PhysicalFeatures` object, while desktop commit `d8a1ea6` requires a nonempty root `PhotoTraceHistory`. Desktop version 9 adds algorithm-2 photo plane calibration. Version 10 is a candidate for the combined format, subject to checking every maintained reader, writer, and any other reserved versions before implementation.

## Reader contract

- Preserve the existing semantics of versions 1–7. For version 8, reject duplicate JSON properties before inspecting its shape. A physical candidate has at least one physical-feature field and no photo-history field; a photo candidate has photo history and no physical-feature fields. Explicit null, empty, malformed, mixed, or missing discriminants are errors, not evidence for another variant.
- The discriminant selects a complete validator, not an acceptance shortcut. Require every serialized field, validate GUIDs, uniqueness, ownership, exact source-contour association, V geometry and resource limits for physical features; apply the complete `d8a1ea6` raw and model validators, algorithm rules and limits for photo history. Reject unknown fields and failed validation. Never discard either field to make deserialization succeed.
- Photo history is historical evidence: the legacy photo model permits its piece to have been deleted later. Do not turn a nonempty historical piece identity into a requirement that the piece still exists in the current visible project.
- Version 9 retains its existing photo-plane meaning and full checks. A version-9 document carrying physical features is rejected unless a separately documented historical producer proves such a format exists.
- A new combined version must explicitly define which optional combinations are valid, require at least one feature justifying that version, and validate each present feature. Preserve photo algorithm/plane invariants, rotation traces, other older fields, project-wide physical identity uniqueness, and the existing size and cancellation limits. Reject unknown fields and invalid mixtures.
- Readers limited to versions 1–7, including the identified iPhone reader, must reject the new versions without replacing, rewriting, or silently stripping data. Verify that behavior against the actual reader rather than assuming it.

## Writer and migration contract

Only the integrated writer may emit the new, verified version. It must write to a new destination or use the existing compare-and-swap atomic-save protocol; no migration or replacement occurs during open. Before replacing a destination, validate the full in-memory model, serialize, reopen the exact bytes with the integrated reader, and compare all persisted fields. A loaded historical v8/v9 project remains unchanged on disk unless the user explicitly saves; the new writer must never emit ambiguous v8. Rollout requires checking that legacy readers fail closed on the selected version.

## Required synthetic regression fixtures

1. One complete physical v8, one complete photo v8, one complete photo-plane v9, and combined v10 with physical and photo information; include v10 variants with each supported optional combination and rotation trace.
2. Each fixture opens without loss, preserves byte-exact source on read, and survives save/reopen with model equality and preserved identity ownership. Saving to an existing path must retain the old bytes on any failure or version conflict.
3. Reject duplicate properties, both v8 discriminants, neither discriminant, null or empty discriminants, missing nested fields, unknown fields, wrong versions, invalid source geometry, duplicate or empty identities, owner mismatches, broken photo source links, invalid plane records, and resource-limit violations.
4. Exercise both directions against the actual historical branch readers: a physical v8 must not be accepted by the photo reader while dropping physical features, and a photo v8 must not be accepted by the physical reader while dropping photo history. Both old readers must reject the new version. Test actual iPhone reader rejection and unchanged original bytes.
5. Include mixed known/unknown fields at every nesting level, corrupt UTF encodings, cancellation, concurrent modification, and combined feature mutations with undo/redo. Use fabricated data only; do not use user projects, photographs, equipment, or paid services.
6. Test the actual photo v9 reader with complete historical v9 fixtures. Test the actual old physical reader rejects v9 and leaves the input bytes unchanged; likewise verify that unsupported old readers never overwrite a rejected file.
7. Prove opening every supported historical fixture performs no writes. Exercise migration to a different target, the same target, and an already-existing distinct target under the existing overwrite/version rules. Cover cancellation and failures before and during atomic replacement. A downgrade must preserve every represented field or be refused before writing; check original and destination bytes in each failure case.
8. Reject a v8 object containing both discriminants even if each variant is independently valid. Cover duplicate keys, nulls, empty values and unknown properties at every nested level, separately and in combinations.
9. Use independent, hand-reviewed fixture oracles for every persisted field, geometry coordinate, identity, ownership relationship and photo link. Assert expected values directly before and after migration; a serializer followed by its own reader is insufficient because matching bugs can hide symmetric data loss.

## Implementation boundary

Integrating this design requires both branches' actual models and validators together in a separate worktree. Neither existing branch alone can faithfully read or validate the other's data. Do not merge or invent a partial photo model as a compatibility shim. Review and test the integrated result before selecting a version or enabling its writer.

## Prototype verification

The prototype imports the frozen photo calibration and rectification implementations and their history validator from `d8a1ea6`, retains the physical model from `8f2df87`, and always writes version 10 whenever either feature is present. It accepts the two unambiguous version-8 variants and the historical photo-plane version 9. Its version-10 writer rereads its output and compares both models before an atomic save. Export formats that cannot carry photo history or physical identities are rejected. Existing project saving does not implement a new migration-consent UI; that is a release blocker, and the prototype must not be merged into the user-facing application as-is.

For an independent historical-reader run, prepare two separate detached worktrees at `8f2df87` and `d8a1ea6`. In each, copy the two files from this worktree's `tests/HistoricalProjectReader` into `work/schema-reader`, then build `work/schema-reader/SchemaReader.csproj` with .NET 10. Set `TEKSTILCAD_OLD_PHYSICAL_READER` and `TEKSTILCAD_OLD_PHOTO_READER` to the respective built `SchemaReader.dll` paths before running `PhysicalPhotoCompatibilityTests`. The cross-reader test is explicitly skipped when those variables are absent; a skipped test does not establish compatibility. Only synthetic temporary files may be passed to these harnesses.

The Apple/iPhone reader was not executed in this Windows prototype. Its version checks and unchanged-source behavior remain separate release gates. Version 10 is reserved only within the two reviewed source histories, not asserted to be globally unused across all unpublished branches or deployed formats.
