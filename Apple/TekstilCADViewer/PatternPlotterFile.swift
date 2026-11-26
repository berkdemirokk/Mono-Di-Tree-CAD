import Foundation

/// File-only HP-GL pen geometry, using the same absolute axes and units as Windows HpglFormat.
enum PatternPlotterFile {
    private struct PlotError: LocalizedError {
        let message: String
        var errorDescription: String? { message }
    }

    private struct PlotPoint {
        let x: Int32
        let y: Int32
        var pair: String { "\(x),\(y)" }
        var millimetres: DraftPoint { DraftPoint(x: Double(x) / 40, y: Double(y) / 40) }
    }

    static func encode(_ project: DraftProject) throws -> Data {
        guard !project.pieces.isEmpty, CADLocalDraftStore.isValid(project) else {
            throw PlotError(message: "Plotter için geçerli bir kalıp gerekli.")
        }
        var text = "IN;SP1;PA;\n"
        for piece in project.pieces {
            if let issue = DraftContourGeometry.validationError(points: piece.points, closed: piece.closed) {
                throw PlotError(message: "\(piece.name): \(issue)")
            }
            let points = try piece.points.map { point -> PlotPoint in
                guard let x = Int32(exactly: (point.x * 40).rounded(.toNearestOrEven)),
                      let y = Int32(exactly: (point.y * 40).rounded(.toNearestOrEven)) else {
                    throw PlotError(message: "Koordinat HP-GL sınırını aşıyor.")
                }
                return PlotPoint(x: x, y: y)
            }
            if let issue = DraftContourGeometry.validationError(points: points.map(\.millimetres), closed: piece.closed) {
                throw PlotError(message: "\(piece.name), 0,025 mm plotter adımında çizilemiyor. \(issue)")
            }
            let first = points[0]
            text += "PU\(first.pair);"
            var penDown = "PD"
            var path = Array(points.dropFirst())
            if piece.closed { path.append(first) }
            for position in path {
                let pair = position.pair
                if penDown.utf8.count + 1 + pair.utf8.count + 1 > 128 {
                    text += penDown + ";"
                    penDown = "PD"
                }
                if penDown != "PD" { penDown += "," }
                penDown += pair
            }
            text += penDown + ";PU;\n"
        }
        text += "SP0;\n"
        return Data(text.utf8)
    }

    static func export(_ project: DraftProject) throws -> URL {
        let bytes = try encode(project)
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let filename = CADSketchImport.exportFileName(forProjectNamed: project.name)
        let url = directory.appendingPathComponent(filename).deletingPathExtension().appendingPathExtension("plt")
        try bytes.write(to: url, options: .atomic)
        return url
    }
}
