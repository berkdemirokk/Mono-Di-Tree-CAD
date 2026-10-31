import SwiftUI
import UniformTypeIdentifiers

@main
struct TekstilCADViewerApp: App {
    var body: some Scene {
        WindowGroup { ContentView() }
    }
}

private struct ContentView: View {
    @State private var showingImporter = false
    @State private var project: Drawing?
    @State private var error: String?
    @State private var selectedPiece: Int?

    var body: some View {
        VStack(spacing: 12) {
            HStack {
                Button(".tcad aç") { showingImporter = true }
                    .accessibilityHint("Önizlemek için bir proje dosyası seçin")
                Spacer()
                if let project { Text(project.name).font(.headline).lineLimit(1) }
            }
            Text("Yalnızca kontur önizlemesi. Düzenleme, kaydetme, serileme, pastal, iplik yönü ve üretim çıktıları desteklenmez.")
                .font(.caption).foregroundStyle(.secondary)
            if let project {
                if project.pieces.contains(where: { $0.points.count > 1 }) {
                    Picker("Gösterilen parça", selection: $selectedPiece) {
                        Text("Tüm parçalar").tag(nil as Int?)
                        ForEach(project.pieces.indices.filter { project.pieces[$0].points.count > 1 }, id: \.self) { index in
                            Text("Parça \(index + 1)").tag(Optional(index))
                        }
                    }
                    .pickerStyle(.menu)
                    DrawingView(pieces: selectedPiece.map { [project.pieces[$0]] } ?? project.pieces)
                        .background(Color.secondary.opacity(0.08))
                        .accessibilityLabel(selectedPiece.map { "Parça \($0 + 1) kontur önizlemesi" } ?? "\(project.pieces.count) parçalı kontur önizlemesi")
                } else {
                    Text("Bu projede görüntülenebilir kontur yok.")
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(Color.secondary.opacity(0.08))
                }
                Text("\(project.pieces.count) parça • Ölçekli üretim çıktısı değildir")
                    .font(.caption).foregroundStyle(.secondary)
            } else {
                Spacer()
                Text("Önizlemek için bir .tcad dosyası açın.").foregroundStyle(.secondary)
                Spacer()
            }
        }
        .padding()
        .frame(minWidth: 300, minHeight: 300)
        .fileImporter(isPresented: $showingImporter, allowedContentTypes: [.item]) { result in
            do {
                let url = try result.get()
                guard url.pathExtension.lowercased() == "tcad" else { throw ViewerError.message("Lütfen bir .tcad dosyası seçin.") }
                let access = url.startAccessingSecurityScopedResource()
                defer { if access { url.stopAccessingSecurityScopedResource() } }
                let loaded = try Drawing.load(url)
                selectedPiece = nil
                project = loaded
            } catch { self.error = error.localizedDescription }
        }
        .alert("Dosya açılamadı", isPresented: Binding(get: { error != nil }, set: { if !$0 { error = nil } })) {
            Button("Tamam", role: .cancel) { error = nil }
        } message: { Text(error ?? "") }
    }
}

private enum ViewerError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case let .message(text) = self { return text }; return nil }
}

private struct Drawing {
    struct Point { let x: Double; let y: Double }
    struct Piece { let points: [Point]; let closed: Bool }
    let name: String
    let pieces: [Piece]

    static func load(_ url: URL) throws -> Drawing {
        let values = try url.resourceValues(forKeys: [.fileSizeKey])
        if let size = values.fileSize, size > 64 * 1024 * 1024 { throw ViewerError.message("Dosya 64 MB sınırını aşıyor.") }
        let data = try Data(contentsOf: url)
        guard data.count <= 64 * 1024 * 1024 else { throw ViewerError.message("Dosya 64 MB sınırını aşıyor.") }
        let root = try JSONSerialization.jsonObject(with: data)
        let object: [String: Any]
        if let array = root as? [Any], array.count == 2,
           let version = array[0] as? Int, (1...5).contains(version), let body = array[1] as? [String: Any] {
            object = body
        } else if let body = root as? [String: Any] { object = body }
        else { throw ViewerError.message("Desteklenmeyen proje biçimi.") }
        guard let name = object["Name"] as? String, let rawPieces = object["Pieces"] as? [[String: Any]], rawPieces.count <= 10_000 else {
            throw ViewerError.message("Proje adı veya parça listesi eksik ya da sınır dışında.")
        }
        var total = 0
        var pieces: [Piece] = []
        for raw in rawPieces {
            guard let rawPoints = raw["Points"] as? [[String: Any]], rawPoints.count <= 10_000,
                  let closed = raw["Closed"] as? Bool else { throw ViewerError.message("Parça konturu eksik veya sınır dışında.") }
            total += rawPoints.count
            guard total <= 200_000 else { throw ViewerError.message("Toplam nokta sınırı aşıldı.") }
            let points: [Point] = try rawPoints.map { point in
                guard let x = point["X"] as? Double, let y = point["Y"] as? Double,
                      x.isFinite, y.isFinite, abs(x) < 1e100, abs(y) < 1e100 else {
                    throw ViewerError.message("Geçersiz veya gösterilemeyen koordinat.")
                }
                return Point(x: x, y: y)
            }
            pieces.append(Piece(points: points, closed: closed))
        }
        return Drawing(name: name, pieces: pieces)
    }
}

private struct DrawingView: View {
    let pieces: [Drawing.Piece]
    var body: some View {
        Canvas { context, size in
            let points = pieces.flatMap(\.points)
            guard let first = points.first else { return }
            var minX = first.x, maxX = first.x, minY = first.y, maxY = first.y
            for p in points { minX = min(minX, p.x); maxX = max(maxX, p.x); minY = min(minY, p.y); maxY = max(maxY, p.y) }
            let width = max(maxX - minX, 1), height = max(maxY - minY, 1)
            let scale = min(max(Double(size.width) - 32, 1) / width, max(Double(size.height) - 32, 1) / height)
            let offsetX = (Double(size.width) - width * scale) / 2
            let offsetY = (Double(size.height) - height * scale) / 2
            func position(_ p: Drawing.Point) -> CGPoint {
                CGPoint(x: offsetX + (p.x - minX) * scale, y: Double(size.height) - offsetY - (p.y - minY) * scale)
            }
            for piece in pieces {
                guard let first = piece.points.first else { continue }
                var path = Path()
                path.move(to: position(first))
                for point in piece.points.dropFirst() { path.addLine(to: position(point)) }
                if piece.closed && piece.points.count > 1 { path.closeSubpath() }
                context.stroke(path, with: .color(.accentColor), lineWidth: 1.5)
            }
        }
    }
}
