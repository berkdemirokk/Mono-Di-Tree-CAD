# Synthetic PNG/JPEG orientation observation

The two synthetic color-quadrant images have the same 64 × 32 raster and EXIF orientation 6. The PNG uses an eXIf chunk; the JPEG uses APP1 EXIF. They contain no user photo.

On Windows, a headless probe running the repository `ReferencePhotoDecoder.DecodeWithGeometry` against these fixtures returned JPEG orientation 6, preview 32 × 64; PNG orientation 1, preview 64 × 32. Both input buffers remained unchanged. Windows checks JPEG orientation explicitly; the PNG branch does not read orientation metadata. This is evidence for this Windows environment, not all decoders.

The iPhone `PhotoObservation.thumbnail` requests ImageIO thumbnail orientation transforms for both PNG and JPEG. Whether the deployed iOS ImageIO version recognizes this PNG eXIf chunk has not been verified. `testSyntheticJPEGAndPNGOrientationPreviewObservation` records PNG metadata and dimensions in an XCTest attachment; run it in Xcode on target iOS versions before claiming parity. A rotated PNG on iPhone would disagree with this Windows preview. No input image is rewritten by this observation.
