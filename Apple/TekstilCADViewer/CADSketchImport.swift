import CoreFoundation
import Foundation

/// İçeride üretilen küçük, sürümsüz kabuk (.tcad içe/dışa aktarım için).
/// Tam Windows CAD dosyası değildir: bilinmeyen alanlar, sürüm zarfları, notçalar,
/// pastal, katmanlar, menşe ve makine verileri reddedilir.
enum CADSketchImport {
    private static let maximumBytes = 2 * 1024 * 1024
    private static let maximumPieces = 250
    private static let maximumTotalPoints = 20_000

    private static func number(_ value: Any?) -> Double? {
        guard let value = value as? NSNumber,
              CFGetTypeID(value) != CFBooleanGetTypeID(),
              value.doubleValue.isFinite,
              abs(value.doubleValue) <= 100_000_000 else { return nil }
        return value.doubleValue
    }

    private static func boolean(_ value: Any?) -> Bool? {
        guard let value = value as? NSNumber,
              CFGetTypeID(value) == CFBooleanGetTypeID() else { return nil }
        return value.boolValue
    }

    /// Bir nokta sözlüğünü okur: {"X":..., "Y":...}.
    private static func point(_ raw: [String: Any]) -> DraftPoint? {
        guard Set(raw.keys) == Set(["X", "Y"]),
              let x = number(raw["X"]), let y = number(raw["Y"]) else { return nil }
        return DraftPoint(x: x, y: y)
    }

    /// Bir eğri segmenti sözlüğünü okur (v9 parametrik biçimi).
    private static func curve(_ raw: [String: Any]) -> CurveSegment? {
        guard let kind = raw["$type"] as? String else { return nil }
        switch kind {
        case "line":
            guard Set(raw.keys) == Set(["$type", "Start", "End"]),
                  let start = point(raw["Start"] as? [String: Any] ?? [:]),
                  let end = point(raw["End"] as? [String: Any] ?? [:]) else { return nil }
            return .line(start: start, end: end)
        case "cubicBezier":
            guard Set(raw.keys) == Set(["$type", "P0", "P1", "P2", "P3"]),
                  let p0 = point(raw["P0"] as? [String: Any] ?? [:]),
                  let p1 = point(raw["P1"] as? [String: Any] ?? [:]),
                  let p2 = point(raw["P2"] as? [String: Any] ?? [:]),
                  let p3 = point(raw["P3"] as? [String: Any] ?? [:]) else { return nil }
            return .cubicBezier(p0: p0, p1: p1, p2: p2, p3: p3)
        case "arc":
            guard Set(raw.keys) == Set(["$type", "Center", "Radius", "StartAngle", "SweepAngle"]),
                  let center = point(raw["Center"] as? [String: Any] ?? [:]),
                  let radius = number(raw["Radius"]), radius.isFinite, radius > 0,
                  let startAngle = number(raw["StartAngle"]), startAngle.isFinite,
                  let sweepAngle = number(raw["SweepAngle"]), sweepAngle.isFinite else { return nil }
            return .arc(center: center, radius: radius, startAngle: startAngle, sweepAngle: sweepAngle)
        default:
            return nil
        }
    }

    /// İçe aktarılabilir düzenlenebilir kopyayı yükler. Sürümsüz kabuk
    /// ({"Name","GradingRules":[],"Pieces":[...]}) ve .tcad v9 zarfı
    /// ([9, {"Name","Pieces":[...]}]) kabul edilir. Bilinmeyen alan varsa nil döner.
    static func loadEditableCopy(from data: Data, jsonRoot: Any) -> DraftProject? {
        guard data.count <= maximumBytes else { return nil }

        var root: [String: Any]
        var schemaVersion = 0
        if let array = jsonRoot as? [Any], array.count == 2,
           let number = array[0] as? NSNumber,
           CFGetTypeID(number) != CFBooleanGetTypeID(),
           number.doubleValue.isFinite, number.doubleValue.rounded() == number.doubleValue,
           (1...9).contains(number.intValue), let body = array[1] as? [String: Any] {
            schemaVersion = number.intValue
            root = body
        } else if let body = jsonRoot as? [String: Any] {
            root = body
        } else { return nil }

        guard Set(root.keys).isSubset(of: Set(["Name", "Pieces", "GradingRules"])),
              let name = root["Name"] as? String,
              !name.isEmpty, name.count <= 128,
              name == name.trimmingCharacters(in: .whitespacesAndNewlines),
              let grading = root["GradingRules"] as? [Any] ?? [],
              grading.isEmpty,
              let rawPieces = root["Pieces"] as? [[String: Any]],
              !rawPieces.isEmpty, rawPieces.count <= maximumPieces else { return nil }

        var ids = Set<UUID>()
        var pieces: [DraftPiece] = []
        var totalPoints = 0

        for raw in rawPieces {
            let requiredKeys: Set<String> = Set(["Id", "Name", "Closed"])
            guard requiredKeys.isSubset(of: Set(raw.keys)),
                  Set(raw.keys).isSubset(of: Set(["Id", "Name", "Closed", "Points", "Curves"])),
                  let rawID = raw["Id"] as? String,
                  let id = UUID(uuidString: rawID),
                  id != UUID(uuidString: "00000000-0000-0000-0000-000000000000")!,
                  ids.insert(id).inserted,
                  let pieceName = raw["Name"] as? String,
                  !pieceName.isEmpty, pieceName.count <= 128,
                  pieceName == pieceName.trimmingCharacters(in: .whitespacesAndNewlines),
                  let closed = boolean(raw["Closed"]) else { return nil }

            var curves: [CurveSegment]?
            var points: [DraftPoint] = []

            if let rawCurves = raw["Curves"] as? [[String: Any]], !rawCurves.isEmpty {
                guard rawCurves.count <= 2_000 else { return nil }
                let parsed = rawCurves.compactMap { curve($0) }
                guard parsed.count == rawCurves.count else { return nil }
                curves = parsed
                var sampled: [DraftPoint] = []
                for (index, segment) in curves!.enumerated() {
                    let pts = segment.sampled()
                    if index > 0, let last = sampled.last, let first = pts.first, last == first {
                        sampled.append(contentsOf: pts.dropFirst())
                    } else {
                        sampled.append(contentsOf: pts)
                    }
                }
                if closed, let first = sampled.first, let last = sampled.last, last != first {
                    sampled.append(first)
                }
                points = sampled
            } else if let rawPoints = raw["Points"] as? [[String: Any]] {
                guard rawPoints.count >= (closed ? 3 : 2),
                      rawPoints.count <= 10_000 else { return nil }
                for rawPoint in rawPoints {
                    guard let p = point(rawPoint) else { return nil }
                    points.append(p)
                }
            } else {
                return nil
            }

            totalPoints += points.count
            guard totalPoints <= maximumTotalPoints else { return nil }
            pieces.append(DraftPiece(id: id, name: pieceName, closed: closed, points: points, curves: curves))
        }

        guard CADFileIO.hasKeyCount(expectedKeyCount(pieces: pieces, schemaVersion: schemaVersion), in: data) else { return nil }
        return DraftProject(name: name, pieces: pieces)
    }

    /// Anahtar ayırıcı (`:`) sayısı. Sürümsüz kabuk için 3 üst anahtar (Name, GradingRules, Pieces),
    /// v9 için 2 üst anahtar (Name, Pieces) kullanılır.
    private static func expectedKeyCount(pieces: [DraftPiece], schemaVersion: Int) -> Int {
        var count = schemaVersion == 0 ? 3 : 2
        for piece in pieces {
            count += 3 // Id, Name, Closed
            if let curves = piece.curves, !curves.isEmpty {
                count += 1 // Curves
                for curve in curves {
                    count += 1 // $type
                    switch curve {
                    case .line(let s, let e): count += 4 // Start.X, Start.Y, End.X, End.Y
                    case .cubicBezier(let p0, let p1, let p2, let p3): count += 8
                    case .arc(let c, _, let sa, let sw): count += 6 // Center.X, Center.Y, Radius, StartAngle, SweepAngle (+$type counted)
                    }
                }
            } else {
                for p in piece.points { count += 2 } // X, Y
            }
        }
        return count
    }

    /// Düzenleme için .tcad dışa aktarır. Parametrik eğriler v9 zarfında korunur;
    /// sıfır eğri varsa polyline-only sürümsüz kabuk yazılır.
    static func encode(_ document: DraftProject) throws -> Data {
        guard !document.pieces.isEmpty, CADLocalDraftStore.isValid(document) else { throw SketchStoreError.invalid }

        let hasCurves = document.pieces.contains { $0.curves != nil && !$0.curves!.isEmpty }

        if hasCurves {
            // v9 parametrik zarf: [9, {"Name", "Pieces": [...]}]
            let pieces: [[String: Any]] = document.pieces.map { piece in
                var raw: [String: Any] = [
                    "Id": piece.id.uuidString,
                    "Name": piece.name,
                    "Closed": piece.closed
                ]
                if let curves = piece.curves, !curves.isEmpty {
                    raw["Curves"] = curves.map { segmentToDictionary($0) }
                } else {
                    raw["Points"] = piece.points.map { ["X": $0.x, "Y": $0.y] }
                }
                return raw
            }
            let root: [String: Any] = ["Name": document.name, "Pieces": pieces]
            let envelope: [Any] = [9, root]
            let data = try JSONSerialization.data(withJSONObject: envelope, options: [.sortedKeys])
            guard data.count <= maximumBytes else { throw SketchStoreError.oversized }
            return data
        } else {
            // Sürümsüz polyline kabuk (eski uyumluluk).
            let pieces: [[String: Any]] = document.pieces.map { piece in
                ["Id": piece.id.uuidString, "Name": piece.name, "Closed": piece.closed,
                 "Points": piece.points.map { ["X": $0.x, "Y": $0.y] }]
            }
            let root: [String: Any] = ["Name": document.name, "GradingRules": [], "Pieces": pieces]
            let data = try JSONSerialization.data(withJSONObject: root, options: [.sortedKeys])
            guard data.count <= maximumBytes else { throw SketchStoreError.oversized }
            return data
        }
    }

    private static func segmentToDictionary(_ segment: CurveSegment) -> [String: Any] {
        switch segment {
        case .line(let start, let end):
            return ["$type": "line", "Start": ["X": start.x, "Y": start.y], "End": ["X": end.x, "Y": end.y]]
        case .cubicBezier(let p0, let p1, let p2, let p3):
            return ["$type": "cubicBezier",
                    "P0": ["X": p0.x, "Y": p0.y],
                    "P1": ["X": p1.x, "Y": p1.y],
                    "P2": ["X": p2.x, "Y": p2.y],
                    "P3": ["X": p3.x, "Y": p3.y]]
        case .arc(let center, let radius, let startAngle, let sweepAngle):
            return ["$type": "arc",
                    "Center": ["X": center.x, "Y": center.y],
                    "Radius": radius,
                    "StartAngle": startAngle,
                    "SweepAngle": sweepAngle]
        }
    }
}
