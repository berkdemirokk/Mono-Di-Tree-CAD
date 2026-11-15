import CoreFoundation
import Foundation

/// Imports ONLY the tiny, unversioned, metadata-free contour envelope produced
/// by the beginner iPhone sketch exporter. Never use this to edit a full Windows
/// CAD file: unknown fields, version envelopes, annotations, grading, layers,
/// notches, provenance and machine data are all rejected.
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

    static func loadEditableCopy(from data: Data, jsonRoot: Any) -> DraftProject? {
        guard data.count <= maximumBytes,
              let root = jsonRoot as? [String: Any],
              Set(root.keys) == Set(["Name", "Pieces", "GradingRules"]),
              let name = root["Name"] as? String,
              !name.isEmpty, name.count <= 128,
              name == name.trimmingCharacters(in: .whitespacesAndNewlines),
              let grading = root["GradingRules"] as? [Any], grading.isEmpty,
              let rawPieces = root["Pieces"] as? [[String: Any]],
              !rawPieces.isEmpty, rawPieces.count <= maximumPieces else { return nil }

        var ids = Set<UUID>()
        var pieces: [DraftPiece] = []
        var totalPoints = 0
        for raw in rawPieces {
            guard Set(raw.keys) == Set(["Id", "Name", "Closed", "Points"]),
                  let rawID = raw["Id"] as? String,
                  let id = UUID(uuidString: rawID),
                  id != UUID(uuidString: "00000000-0000-0000-0000-000000000000")!,
                  ids.insert(id).inserted,
                  let pieceName = raw["Name"] as? String,
                  !pieceName.isEmpty, pieceName.count <= 128,
                  pieceName == pieceName.trimmingCharacters(in: .whitespacesAndNewlines),
                  let closed = boolean(raw["Closed"]),
                  let rawPoints = raw["Points"] as? [[String: Any]],
                  rawPoints.count >= (closed ? 3 : 2),
                  rawPoints.count <= 10_000 else { return nil }

            totalPoints += rawPoints.count
            guard totalPoints <= maximumTotalPoints else { return nil }
            var points: [DraftPoint] = []
            for rawPoint in rawPoints {
                guard Set(rawPoint.keys) == Set(["X", "Y"]),
                      let x = number(rawPoint["X"]), let y = number(rawPoint["Y"]) else {
                    return nil
                }
                points.append(DraftPoint(x: x, y: y))
            }
            pieces.append(DraftPiece(id: id, name: pieceName, closed: closed, points: points))
        }
        guard CADFileIO.hasKeyCount(3 + 4 * pieces.count + 2 * totalPoints, in: data) else { return nil }
        return DraftProject(name: name, pieces: pieces)
    }

    static func encode(_ document: DraftProject) throws -> Data {
        guard !document.pieces.isEmpty, CADLocalDraftStore.isValid(document) else { throw SketchStoreError.invalid }
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
