import Foundation

/// A bounded, no-rotation shelf arrangement for simple garment marker previews.
enum MarkerLayout {
    static let maximumPieces = 250
    static let maximumTotalPoints = 20_000

    enum LayoutError: Error, Equatable {
        case invalidInput
        case tooWide(piece: String)
        case tooManyPieces
        case tooManyPoints
        case invalidGeometry(piece: String)

        var errorDescription: String? {
            switch self {
            case .invalidInput: return "Pastal ölçüleri veya kaynak çizim geçersiz."
            case .tooWide(let piece): return "\(piece) parçası kumaş eninden geniş."
            case .tooManyPieces: return "Pastalda en fazla 250 parça olabilir."
            case .tooManyPoints: return "Pastal toplamı 20.000 nokta sınırını aşıyor."
            case .invalidGeometry(let piece): return "\(piece) parçasının konturu geçersiz."
            }
        }
    }

    /// Places requested copies into horizontal rows. Source shapes are translated only; original
    /// project and piece identifiers remain untouched, and every placed copy receives a new ID.
    static func arrange(_ source: DraftProject, widthMM: Double, gapMM: Double,
                        marginMM: Double, quantities: [UUID: Int]) throws -> DraftProject {
        guard widthMM.isFinite, gapMM.isFinite, marginMM.isFinite,
              widthMM > 0, gapMM >= 0, marginMM >= 0,
              widthMM <= DraftContourGeometry.coordinateLimit,
              gapMM <= DraftContourGeometry.coordinateLimit,
              marginMM <= DraftContourGeometry.coordinateLimit,
              source.pieces.count <= maximumPieces,
              CADLocalDraftStore.isValid(source),
              !quantities.isEmpty,
              quantities.keys.allSatisfy({ id in source.pieces.contains(where: { $0.id == id }) }) else {
            throw LayoutError.invalidInput
        }

        var expanded: [(DraftPiece, Int)] = []
        var copyCount = 0
        var pointCount = 0
        for piece in source.pieces {
            let quantity = quantities[piece.id] ?? 0
            guard quantity >= 0 else { throw LayoutError.invalidInput }
            if quantity == 0 { continue }
            guard piece.points.count >= (piece.closed ? 3 : 2),
                  piece.points.count <= 10_000,
                  !piece.name.isEmpty, piece.name.count <= 128,
                  piece.points.allSatisfy({ $0.x.isFinite && $0.y.isFinite &&
                      abs($0.x) <= DraftContourGeometry.coordinateLimit &&
                      abs($0.y) <= DraftContourGeometry.coordinateLimit }) else {
                throw LayoutError.invalidGeometry(piece: piece.name)
            }
            let width = piece.maxX - piece.minX
            guard width.isFinite, width + 2 * marginMM <= widthMM else {
                throw LayoutError.tooWide(piece: piece.name)
            }
            guard quantity <= maximumPieces - copyCount else { throw LayoutError.tooManyPieces }
            guard piece.points.count <= (maximumTotalPoints - pointCount) / quantity else {
                throw LayoutError.tooManyPoints
            }
            copyCount += quantity
            pointCount += piece.points.count * quantity
            expanded.append((piece, quantity))
        }
        guard copyCount > 0 else { throw LayoutError.invalidInput }
        guard copyCount <= maximumPieces else { throw LayoutError.tooManyPieces }
        guard pointCount <= maximumTotalPoints else { throw LayoutError.tooManyPoints }

        var placed: [DraftPiece] = []
        placed.reserveCapacity(copyCount)
        var x = marginMM
        var y = marginMM
        var rowHeight = 0.0
        for (piece, quantity) in expanded {
            let width = piece.maxX - piece.minX
            let height = piece.maxY - piece.minY
            for copy in 0..<quantity {
                if copy > 0 || !placed.isEmpty {
                    let nextX = x + gapMM
                    if nextX + width + marginMM > widthMM {
                        y += rowHeight + gapMM
                        x = marginMM
                        rowHeight = 0
                    } else {
                        x = nextX
                    }
                }
                let dx = x - piece.minX
                let dy = y - piece.minY
                let points = piece.points.map { DraftPoint(x: $0.x + dx, y: $0.y + dy) }
                guard points.allSatisfy({ $0.x.isFinite && $0.y.isFinite &&
                    abs($0.x) <= DraftContourGeometry.coordinateLimit &&
                    abs($0.y) <= DraftContourGeometry.coordinateLimit }) else {
                    throw LayoutError.invalidInput
                }
                var item = piece
                item.id = UUID()
                item.name = String("\(piece.name) · \(copy + 1)".prefix(128))
                item.points = points
                placed.append(item)
                x += width
                rowHeight = max(rowHeight, height)
            }
        }
        let resultName = String("\(source.name) · Pastal".prefix(128))
        return DraftProject(id: UUID(), name: resultName, pieces: placed)
    }
}
