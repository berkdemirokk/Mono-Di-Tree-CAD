import Foundation
import CoreFoundation

/// Detached experimental conversion of extracted UTF-8 pieces into preview data.
/// Does not establish v7 history validity or signed-zero byte fidelity and is never used by production UI.
enum ExperimentalV7Contours {
    enum Failure: Error { case invalid, limit, cancelled }
    private static func number(_ value: Any?) -> Double? {
        guard let n = value as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID() else { return nil }
        return n.doubleValue
    }
    private static func boolean(_ value: Any?) -> Bool? {
        guard let n = value as? NSNumber, CFGetTypeID(n) == CFBooleanGetTypeID() else { return nil }
        return n.boolValue
    }
    private static func point(_ value: Any?) -> Drawing.Point? {
        guard let object = value as? [String: Any], Set(object.keys).isSubset(of: ["X", "Y", "IsFinite"]),
              let x = number(object["X"]), let y = number(object["Y"]), x.isFinite, y.isFinite,
              abs(x) <= 100_000_000, abs(y) <= 100_000_000,
              object["IsFinite"] == nil || boolean(object["IsFinite"]) == true else { return nil }
        return Drawing.Point(x: x, y: y)
    }
    static func decode(_ input: [Data], cancelled: () -> Bool = { false }) throws -> [Drawing.Piece] {
        guard input.count <= 10_000 else { throw Failure.limit }
        var result: [Drawing.Piece] = [], ids = Set<UUID>(), total = 0, markTotal = 0, bytes = 0
        for data in input {
            if cancelled() { throw Failure.cancelled }
            bytes += data.count
            guard bytes <= 16 * 1024 * 1024 else { throw Failure.limit }
            var scanner = ExperimentalJSONStream(maximumBytes: 512 * 1024, strictIntegerKeys: true)
            try scanner.feed(data, cancelled: cancelled); try scanner.finish(cancelled: cancelled)
            guard data.count <= 512 * 1024,
                  let raw = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  Set(raw.keys).isSubset(of: ["Id", "Name", "Points", "Closed", "Metadata", "Grainline", "Notches"]),
                  let textID = raw["Id"] as? String, let id = UUID(uuidString: textID),
                  id.uuidString != "00000000-0000-0000-0000-000000000000", ids.insert(id).inserted,
                  let name = raw["Name"] as? String, name.utf16.count <= 1024,
                  let closed = boolean(raw["Closed"]), let rawPoints = raw["Points"] as? [[String: Any]],
                  rawPoints.count >= (closed ? 3 : 2), rawPoints.count <= 10_000 else { throw Failure.invalid }
            total += rawPoints.count
            guard total <= 200_000 else { throw Failure.limit }
            var points: [Drawing.Point] = []; points.reserveCapacity(rawPoints.count)
            for (index, rawPoint) in rawPoints.enumerated() {
                if index % 256 == 0 && cancelled() { throw Failure.cancelled }
                guard let parsed = point(rawPoint) else { throw Failure.invalid }
                points.append(parsed)
            }
            var marks: [Int] = [], seen = Set<Int>()
            if let value = raw["Notches"] {
                guard let list = value as? [[String: Any]], !list.isEmpty, list.count <= 10_000 else { throw Failure.invalid }
                markTotal += list.count; guard markTotal <= 200_000 else { throw Failure.limit }
                for (offset, mark) in list.enumerated() {
                    if offset % 256 == 0 && cancelled() { throw Failure.cancelled }
                    guard Set(mark.keys) == Set(["VertexIndex", "Position"]),
                          let n = mark["VertexIndex"] as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(),
                          n.doubleValue >= 0, n.doubleValue < Double(points.count), let position = point(mark["Position"]) else { throw Failure.invalid }
                    let index = n.intValue
                    guard seen.insert(index).inserted, points[index].x == position.x, points[index].y == position.y,
                          !(closed && points.count > 1 && points.first!.x == points.last!.x && points.first!.y == points.last!.y && index == points.count - 1) else { throw Failure.invalid }
                    marks.append(index)
                }
            }
            var role: String?, size: String?, material: String?, quantity: Int?
            if let value = raw["Metadata"], !(value is NSNull) {
                guard let fields = value as? [String: Any],
                      Set(fields.keys).isSubset(of: ["Role", "Size", "Material", "Quantity"]) else { throw Failure.invalid }
                if let v = fields["Role"], !(v is NSNull) {
                    guard let text = v as? String, ["Kesim", "Yardımcı"].contains(text) else { throw Failure.invalid }; role = text
                }
                func text(_ value: Any?) throws -> String? {
                    guard let value, !(value is NSNull) else { return nil }
                    guard let s = value as? String, !s.isEmpty, s.utf16.count <= 128,
                          s == s.trimmingCharacters(in: .whitespacesAndNewlines),
                          s.rangeOfCharacter(from: .controlCharacters) == nil else { throw Failure.invalid }
                    return s
                }
                size = try text(fields["Size"]); material = try text(fields["Material"])
                if let value = fields["Quantity"], !(value is NSNull) {
                    guard let n = number(value), n.isFinite, n.rounded() == n, n >= 1, n <= 100_000 else { throw Failure.invalid }
                    quantity = Int(n)
                }
                guard role != nil || size != nil || material != nil || quantity != nil else { throw Failure.invalid }
            }
            var grain: Drawing.Grain? = nil
            if let value = raw["Grainline"], !(value is NSNull) {
                guard let item = value as? [String: Any], Set(item.keys) == Set(["Start", "End", "NeedsReview"]),
                      let start = point(item["Start"]), let end = point(item["End"]),
                      (start.x != end.x || start.y != end.y), let review = boolean(item["NeedsReview"]) else { throw Failure.invalid }
                grain = Drawing.Grain(start: start, end: end, needsReview: review)
            }
            let minimum = ContourMeasure.hasMinimumContourPoints(points, closed: closed)
            result.append(Drawing.Piece(name: name, id: id, points: points, closed: closed,
                contourLength: minimum ? ContourMeasure.length(points, closed: closed) : nil,
                isCollapsed: ContourMeasure.isCollapsed(points), contourBounds: points.count > 1 ? ContourBounds(points: points) : nil,
                hasMinimumContourPoints: minimum, role: role, roleUnreadable: false, size: size, material: material,
                textMetadataUnreadable: false, quantity: quantity, quantityUnreadable: false, metadataUnreadable: false,
                metadataEmpty: false, grainStatus: grain.map { $0.needsReview ? "İplik yönü: Kontrol bekliyor" : "İplik yönü: Kayıtlı" } ?? "İplik yönü: Belirtilmemiş",
                grain: grain, notches: marks))
        }
        if cancelled() { throw Failure.cancelled }
        return result
    }
}
