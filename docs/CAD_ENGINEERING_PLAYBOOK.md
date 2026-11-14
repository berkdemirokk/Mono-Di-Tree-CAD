# Mono Di Tree CAD — Engineering Playbook (evidence & release gates)

Date: 2026-11-14. Applies to the publicly accessible codebase, not proprietary CAD software.

## Product objective

A first-time user should be able to create a small 2D contour, enter exact millimetre dimensions, save, reopen and inspect it without training. This must coexist with **strict production boundaries**: a visually attractive CAD draft is **not** a verified garment pattern or cutting instruction.

Target quality is demonstrated by **task success + geometry preservation + file compatibility**, not by number of buttons or a $1,000 price claim.

## Research distilled into implementable engineering rules

1. **Apple Human Interface Guidelines — Gestures and Accessibility.** Touch means select, drag means move, pinch means zoom. Every essential gesture has a labelled non-gesture alternative. Control hit areas target at least 44x44 points in the beginner flow, subject to dynamic type and VoiceOver testing.
   - https://developer.apple.com/design/human-interface-guidelines/gestures
   - https://developer.apple.com/design/human-interface-guidelines/accessibility
   - https://developer.apple.com/documentation/swiftui/spatialtapgesture
2. **CAD dimensional intent (Shapr3D's publicly documented approach).** Shape drawing and constraint/dimension entry are distinct; dragging provides immediate visual feedback, but the stored geometry must be determined by world-unit coordinates, not by a screenshot scale. We implement our own algorithms, not third-party code.
   - https://www.shapr3d.com/product/cad-constraints
3. **Robust polygon operations (Clipper2 public documentation).** Boolean/offsetting must preserve orientation, account for holes, reject invalid topology and document rounding. Avoid promising that any single offset operation works for every clothing seam. The source already uses Clipper2 with its Boost licence notice.
   - https://github.com/AngusJohnson/Clipper2
   - https://angusj.com/clipper2/Docs/FAQ.htm
4. **Editable documents.** Drawing history, saved project identity, explicit versioning and interrupted-write recovery take precedence over animation and 3D. A newer file must not silently lose unsupported factory metadata when opened in an older/mobile viewer.
   - https://developer.apple.com/documentation/swiftui/documentgroup

## Invariants that every change should protect

- **World units:** points are millimetres. Device screen pixels may only affect visual transforms/hit testing, not authoritative persisted geometry.
- **Identity:** each piece has a nonempty stable UUID. Duplicates receive new UUIDs. A new project must round-trip between SwiftUI and the Windows C# loader.
- **Topology:** open contours have >=2 points, closed >=3, finite coordinates, bounded point counts. New tap-composed closed contours reject zero area and self-intersections. These checks are a **draft entry guard**, not a full production topology validator.
- **Transforms:** translate, scale, mirror and rotate must preserve contour point order and closed/open status; undo restores the exact former coordinates.
- **Persistence:** atomic local recovery, file-format version gates, no silent rewrite of imported industrial files. A successful Share Sheet preview alone does not prove the destination contains a valid CAD file.
- **Safety:** unsupported markers, grainlines, grading/nesting traces, cutting protocols or dimensions trigger warnings and/or explicit refusal, never invented output.
- **Licensing:** build without paid runtime APIs. Track open-source licences/notice files and avoid copying assets/source of proprietary CAD competitors.

## Current honest capability boundary (PR #10)

**Implemented / under validation:** read-only .tcad viewer; starter editor for newly created contours; editable rectangle/line/contour path; exact XY/width/height/line-length input; 90° rotation and small transforms; tap selection, drag, snapping; open/closed freeform tap composer; undo/redo; atomic 2 MB local draft recovery; .tcad export/share. This is **not** a full commercial CAD system.

**Not implemented:** lossless imported industrial project editing; true parametric curve handles and constraint solver; freehand Bézier editing; industrial DXF/AAMA/ASTM acceptance; comprehensive seam allowance/dart/grading/nesting workflows; offline real-machine protocol; signed shipping installer; real-device and factory operator certification.

## Release checks (not optional)

1. Clean Windows CI + all non-skipped tests. Isolate thread-pool-sensitive concurrency tests rather than relaxing correctness assertions.
2. iPhone simulator build + UI tests on the exact PR head SHA; investigate each failure. CI green on Windows alone is not acceptance.
3. Device: app start, tap/drag, min-target sizes, dynamic type, VoiceOver and a second launch after a force quit; no crash or unwanted draft overwrite.
4. Test-file corpus: a Swift-created .tcad round-trips through the C# parser; no ID/order/coordinate loss. Versions 6–8 remain viewer-only until semantic preservation exists.
5. Factory-specific acceptance: authorised genuine DXF files, dimensions, seam lengths, material/grain constraints and physical output must be independently signed off before marketing 'production ready'.
6. First-time user study: at least 5 untrained users should be able to create a 100x150 mm rectangle, change a dimension, save and reopen with no coaching. Log task failures and iterate, not just ratings.
7. App Store: signing, entitlements, metadata, privacy, accessibility and TestFlight on a real account; no publishing automatically from a draft PR.

## Next short iterations

- Fix simulator CI on the current head and the beginner smoke-tests, especially restored draft vs new example routing.
- Create an on-canvas polyline node editor with precise snap and auto-safety checks; then make a document-based versioned storage layer.
- Add curves as editable geometry in the data model **before** extending exporters or users' factory content.
- Add genuine pattern/grading/nesting scenarios with dimensional ground truth and acceptance thresholds.
