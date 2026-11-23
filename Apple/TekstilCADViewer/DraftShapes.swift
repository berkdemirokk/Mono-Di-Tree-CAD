import Foundation

/// Ready-made shapes for the workshop's "add" buttons. Pure functions, so they can be tested
/// without a view. Coordinates are in mm and CAD Y grows downwards.
enum DraftShapes {
    /// Corners of a circle. It looks round at every zoom the drawing allows, and it stays far
    /// below the 250-point limit of the point editor, so its corners can still be edited.
    static let circleCorners = 72

    /// A circle as a closed polygon that exactly fills the square from (`left`, `bottom`) with
    /// side `diameter`, so its width and height read as the diameter. Clockwise, starting
    /// at the right-most point. Empty when the input cannot form a circle.
    static func circle(left: Double, bottom: Double, diameter: Double,
                       corners: Int = circleCorners) -> [DraftPoint] {
        guard corners >= 3, diameter.isFinite, diameter > 0,
              left.isFinite, bottom.isFinite else { return [] }
        let radius = diameter / 2
        let cx = left + radius, cy = bottom + radius
        return (0..<corners).map { index in
            let angle = Double(index) * 2 * Double.pi / Double(corners)
            return DraftPoint(x: tidy(cx + radius * cos(angle)), y: tidy(cy + radius * sin(angle)))
        }
    }

    /// A triangle with a flat lower edge and its upper tip in the middle, filling the box from
    /// (`left`, `bottom`) with the given width and height. Empty when the box has no area.
    static func triangle(left: Double, bottom: Double, width: Double, height: Double) -> [DraftPoint] {
        guard width.isFinite, height.isFinite, width > 0, height > 0,
              left.isFinite, bottom.isFinite else { return [] }
        return [
            DraftPoint(x: left, y: bottom + height),
            DraftPoint(x: left + width, y: bottom + height),
            DraftPoint(x: left + width / 2, y: bottom)
        ]
    }

    /// Rounds away floating-point noise (a millionth of a millimetre), so the corners read as
    /// clean numbers in the point editor and the box of the circle is exact.
    private static func tidy(_ value: Double) -> Double {
        (value * 1_000_000).rounded() / 1_000_000
    }
}
