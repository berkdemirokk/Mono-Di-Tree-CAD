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
    @State private var showingPieces = false
    @State private var project: Drawing?
    @State private var error: String?
    @State private var selectedPiece: Int?
    @State private var drawingRevision = UUID()
    @State private var loadRequest = UUID()
    @State private var isLoading = false

    var body: some View {
        VStack(spacing: 12) {
            HStack {
                Button(".tcad aç") { loadRequest = UUID(); isLoading = false; showingImporter = true }
                    .accessibilityHint("Önizlemek için bir proje dosyası seçin")
                Spacer()
                if let project { Text(project.name).font(.headline).lineLimit(1) }
            }
            Text("Yalnızca kontur önizlemesi. Düzenleme, kaydetme, serileme, pastal, iplik yönü ve üretim çıktıları desteklenmez.")
                .font(.caption).foregroundStyle(.secondary)
            if isLoading { ProgressView("Proje açılıyor…") }
            if let project {
                if project.pieces.contains(where: { $0.points.count > 1 }) {
                    Button { showingPieces = true } label: {
                        HStack {
                            VStack(alignment: .leading, spacing: 3) {
                                Text("Gösterilen parça").font(.caption).foregroundStyle(.secondary)
                                Text(selectedPiece.map { project.pieces[$0].displayName(index: $0) } ?? "Tüm parçalar")
                                    .font(.headline).lineLimit(1)
                            }
                            Spacer()
                            Image(systemName: "chevron.up.chevron.down").foregroundStyle(.secondary)
                        }
                        .frame(maxWidth: .infinity, minHeight: 52, alignment: .leading)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityHint("Parçaları aramak veya seçmek için açın")
                    .sheet(isPresented: $showingPieces) {
                        PiecePicker(pieces: project.pieces, selection: $selectedPiece)
                    }
                    if let selectedPiece {
                        PieceDetails(piece: project.pieces[selectedPiece])
                    }
                    DrawingView(
                        pieces: selectedPiece.map { [project.pieces[$0]] } ?? project.pieces,
                        label: selectedPiece.map { "\(project.pieces[$0].displayName(index: $0)) kontur önizlemesi" } ?? "\(project.pieces.count) parçalı kontur önizlemesi"
                    )
                        .id("\(drawingRevision)-\(selectedPiece.map(String.init) ?? "all")")
                        .background(Color.secondary.opacity(0.08))
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
                let request = UUID()
                loadRequest = request
                isLoading = true
                DispatchQueue.global(qos: .userInitiated).async {
                    let access = url.startAccessingSecurityScopedResource()
                    let loaded = Result { try Drawing.load(url) }
                    if access { url.stopAccessingSecurityScopedResource() }
                    DispatchQueue.main.async {
                        guard loadRequest == request else { return }
                        isLoading = false
                        switch loaded {
                        case .success(let drawing):
                            showingPieces = false
                            selectedPiece = nil
                            drawingRevision = UUID()
                            project = drawing
                        case .failure(let error):
                            self.error = error.localizedDescription
                        }
                    }
                }
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
    struct Piece {
        let name: String?
        let points: [Point]
        let closed: Bool

        func displayName(index: Int) -> String {
            guard let name, !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                return "Parça \(index + 1)"
            }
            return name
        }
    }
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
            pieces.append(Piece(name: raw["Name"] as? String, points: points, closed: closed))
        }
        return Drawing(name: name, pieces: pieces)
    }
}

private struct PiecePicker: View {
    let pieces: [Drawing.Piece]
    @Binding var selection: Int?
    @Environment(\.dismiss) private var dismiss
    @State private var search = ""

    private var matchingIndices: [Int] {
        let query = search.trimmingCharacters(in: .whitespacesAndNewlines)
        return pieces.indices.filter { index in
            pieces[index].points.count > 1 &&
            (query.isEmpty || pieces[index].displayName(index: index).localizedCaseInsensitiveContains(query))
        }
    }

    var body: some View {
        NavigationStack {
            List {
                selectionRow(title: "Tüm parçalar", index: nil)
                ForEach(matchingIndices, id: \.self) { index in
                    selectionRow(title: pieces[index].displayName(index: index), index: index)
                }
                if matchingIndices.isEmpty && !search.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    Text("Eşleşen parça bulunamadı.")
                        .foregroundStyle(.secondary)
                }
            }
            .searchable(text: $search, prompt: "Parça ara")
            .navigationTitle("Parça seç")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Kapat") { dismiss() }
                }
            }
        }
    }

    private func selectionRow(title: String, index: Int?) -> some View {
        Button {
            selection = index
            dismiss()
        } label: {
            HStack {
                Text(title).foregroundStyle(.primary)
                Spacer()
                if selection == index {
                    Image(systemName: "checkmark").foregroundStyle(Color.accentColor)
                }
            }
            .frame(maxWidth: .infinity, minHeight: 52, alignment: .leading)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(selection == index ? .isSelected : [])
    }
}
private struct PieceDetails: View {
    let piece: Drawing.Piece

    private var dimensions: (width: Double, height: Double)? {
        guard let first = piece.points.first else { return nil }
        var minX = first.x, maxX = first.x, minY = first.y, maxY = first.y
        for point in piece.points.dropFirst() {
            minX = min(minX, point.x); maxX = max(maxX, point.x)
            minY = min(minY, point.y); maxY = max(maxY, point.y)
        }
        return (maxX - minX, maxY - minY)
    }

    var body: some View {
        if let dimensions {
            VStack(alignment: .leading, spacing: 3) {
                Text("Kontur bilgisi").font(.subheadline.bold())
                Text("\(piece.points.count) nokta • \(piece.closed ? "Kapalı" : "Açık") kontur")
                Text("En: \(dimensions.width.formatted(.number.precision(.fractionLength(0...2)))) • Boy: \(dimensions.height.formatted(.number.precision(.fractionLength(0...2))))")
                Text("Boyutlar dosyadaki koordinat birimindedir.")
                    .foregroundStyle(.secondary)
            }
            .font(.caption)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(8)
            .background(Color.secondary.opacity(0.08))
            .accessibilityElement(children: .combine)
        }
    }
}
private struct DrawingView: View {
    let pieces: [Drawing.Piece]
    let label: String
    @State private var zoom: CGFloat = 1
    @State private var offset: CGSize = .zero
    @GestureState private var pinch: CGFloat = 1
    @GestureState private var drag: CGSize = .zero

    private func limitedZoom(_ value: CGFloat) -> CGFloat {
        min(max(value.isFinite ? value : 1, 0.5), 10)
    }

    private func limitedOffset(_ value: CGSize, size: CGSize, scale: CGFloat) -> CGSize {
        let limitX = size.width * max(scale, 1)
        let limitY = size.height * max(scale, 1)
        return CGSize(width: min(max(value.width, -limitX), limitX),
                      height: min(max(value.height, -limitY), limitY))
    }

    var body: some View {
        GeometryReader { geometry in
            let currentZoom = limitedZoom(zoom * pinch)
            let currentOffset = limitedOffset(
                CGSize(width: offset.width + drag.width, height: offset.height + drag.height),
                size: geometry.size, scale: currentZoom
            )
            ZStack(alignment: .topTrailing) {
                drawing
                    .scaleEffect(currentZoom)
                    .offset(currentOffset)
                    .frame(width: geometry.size.width, height: geometry.size.height)
                    .accessibilityLabel(label)
                    .accessibilityHint("Yakınlaştırmak için iki parmağınızı kullanın; çizimi taşımak için sürükleyin.")
                    .accessibilityValue("Yüzde \(Int(currentZoom * 100))")
                    .accessibilityAdjustableAction { direction in
                        switch direction {
                        case .increment: zoom = limitedZoom(zoom * 1.25)
                        case .decrement: zoom = limitedZoom(zoom / 1.25)
                        @unknown default: break
                        }
                        offset = limitedOffset(offset, size: geometry.size, scale: zoom)
                    }
                Button("Görünümü sıfırla") {
                    zoom = 1
                    offset = .zero
                }
                .font(.caption)
                .padding(8)
                .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 8))
                .padding(8)
            }
            .frame(width: geometry.size.width, height: geometry.size.height)
            .contentShape(Rectangle())
            .clipped()
            .gesture(
                MagnificationGesture()
                    .updating($pinch) { value, state, _ in state = value }
                    .onEnded { value in
                        zoom = limitedZoom(zoom * value)
                        offset = limitedOffset(offset, size: geometry.size, scale: zoom)
                    }
                    .simultaneously(with: DragGesture()
                        .updating($drag) { value, state, _ in state = value.translation }
                        .onEnded { value in
                            offset = limitedOffset(
                                CGSize(width: offset.width + value.translation.width, height: offset.height + value.translation.height),
                                size: geometry.size, scale: zoom
                            )
                        })
            )
        }
    }

    private var drawing: some View {
        Canvas { context, size in
            let points = pieces.filter { $0.points.count > 1 }.flatMap(\.points)
            guard let first = points.first else { return }
            var minX = first.x, maxX = first.x, minY = first.y, maxY = first.y
            for p in points { minX = min(minX, p.x); maxX = max(maxX, p.x); minY = min(minY, p.y); maxY = max(maxY, p.y) }
            let width = maxX - minX, height = maxY - minY
            guard width > 0 || height > 0 else { return }
            let horizontalScale = width > 0 ? max(Double(size.width) - 32, 1) / max(width, 1e-100) : .infinity
            let verticalScale = height > 0 ? max(Double(size.height) - 32, 1) / max(height, 1e-100) : .infinity
            let scale = min(horizontalScale, verticalScale)
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
