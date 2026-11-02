import Foundation

/// Bounded conservative box check for a historical layout, not a topology or
/// manufacturing proof. Exceeding the candidate-pair budget fails closed.
enum ExperimentalV7Layout {
    enum Failure: Error { case invalid, limit, cancelled }
    private struct Box { let minX: Double, minY: Double, maxX: Double, maxY: Double }
    private static let maximumPairs = 2_000_000

    static func verify(_ pieces: [Drawing.Piece], width: Double, length: Double, gap: Double, margin: Double,
                       cancelled: () -> Bool = { false }) throws {
        guard !pieces.isEmpty, pieces.count <= 10_000, width.isFinite, length.isFinite, gap.isFinite, margin.isFinite,
              width > 0, width <= 100_000_000, length > 0, length <= 100_000_000,
              gap >= 0, margin >= 0 else { throw Failure.invalid }
        var boxes: [Box] = []; boxes.reserveCapacity(pieces.count)
        for piece in pieces {
            if cancelled() { throw Failure.cancelled }
            guard piece.closed, let bounds = piece.contourBounds, bounds.minX < bounds.maxX, bounds.minY < bounds.maxY,
                  bounds.minX >= margin, bounds.minY >= margin,
                  ExperimentalV7Geometry.differenceAtLeast(width, bounds.maxX, margin),
                  ExperimentalV7Geometry.differenceAtLeast(length, bounds.maxY, margin) else { throw Failure.invalid }
            boxes.append(Box(minX: bounds.minX, minY: bounds.minY, maxX: bounds.maxX, maxY: bounds.maxY))
        }
        if cancelled() { throw Failure.cancelled }
        func minValue(_ box: Box, _ x: Bool) -> Double { x ? box.minX : box.minY }
        func maxValue(_ box: Box, _ x: Bool) -> Double { x ? box.maxX : box.maxY }
        func order(_ x: Bool) -> [Int] { boxes.indices.sorted { minValue(boxes[$0], x) < minValue(boxes[$1], x) } }
        func candidates(_ sorted: [Int], _ x: Bool) throws -> Int {
            let ends = boxes.map { maxValue($0, x) }.sorted()
            var expired = 0, count = 0
            for (i, index) in sorted.enumerated() {
                if cancelled() { throw Failure.cancelled }
                while expired < i && ExperimentalV7Geometry.differenceAtLeast(minValue(boxes[index], x), ends[expired], gap) { expired += 1 }
                count += i - expired
                if count > maximumPairs { return count }
            }
            return count
        }
        let xOrder = order(true), yOrder = order(false)
        let xCount = try candidates(xOrder, true), yCount = try candidates(yOrder, false)
        guard min(xCount, yCount) <= maximumPairs else { throw Failure.limit }
        let useX = xCount <= yCount, sorted = xCount <= yCount ? xOrder : yOrder
        var active: [Int] = []
        for index in sorted {
            if cancelled() { throw Failure.cancelled }
            let current = boxes[index]
            var next: [Int] = []; next.reserveCapacity(active.count + 1)
            for otherIndex in active {
                if cancelled() { throw Failure.cancelled }
                let other = boxes[otherIndex]
                if ExperimentalV7Geometry.differenceAtLeast(minValue(current, useX), maxValue(other, useX), gap) { continue }
                let separated = useX
                    ? ExperimentalV7Geometry.differenceAtLeast(current.minY, other.maxY, gap) || ExperimentalV7Geometry.differenceAtLeast(other.minY, current.maxY, gap)
                    : ExperimentalV7Geometry.differenceAtLeast(current.minX, other.maxX, gap) || ExperimentalV7Geometry.differenceAtLeast(other.minX, current.maxX, gap)
                guard separated else { throw Failure.invalid }
                next.append(otherIndex)
            }
            next.append(index); active = next
        }
    }
}
