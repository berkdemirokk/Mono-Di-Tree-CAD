import CoreFoundation
import SwiftUI
import UniformTypeIdentifiers

@main
struct TekstilCADViewerApp: App {
    var body: some Scene {
        #if os(macOS)
        Window("Mono Di Tree CAD", id: "workspace") { ContentView() }
        #else
        WindowGroup { ContentView() }
        #endif
    }
}

private struct ContentView: View {
    private struct EditorRoute: Identifiable {
        let id = UUID()
        let example: Bool
        let document: DraftProject?

        init(example: Bool = false, document: DraftProject? = nil) {
            self.example = example
            self.document = document
        }
    }

    @State private var showingImporter = false
    @State private var showingPieces = false
    @State private var editorRoute: EditorRoute?
    @State private var hasLocalDraft = false
    @State private var recentProjects: [CADProjectLibrary.Entry] = []
    @State private var didStart = false
    @State private var recentRefresh = UUID()

    private func refreshProjects() {
        do { hasLocalDraft = try CADLocalDraftStore.restore() != nil }
        catch { self.error = "Taslak okunamadı. \(error.localizedDescription)" }
        recentRefresh = UUID()
    }

    @MainActor
    private func readRecentProjects() async {
        let result = await Task.detached(priority: .userInitiated) {
            Result { try CADProjectLibrary.recent() }
        }.value
        guard !Task.isCancelled else { return }
        switch result {
        case .success(let entries): recentProjects = entries
        case .failure(let failure): error = "Son projeler okunamadı. \(failure.localizedDescription)"
        }
    }

    private func resumeEditor() {
        do {
            guard let draft = try CADLocalDraftStore.restore() else {
                throw ViewerError.message("Kaydedilmiş taslak bulunamadı.")
            }
            editorRoute = EditorRoute(document: draft)
        } catch { self.error = error.localizedDescription }
    }

    private func openEditor(_ route: EditorRoute) {
        do {
            try CADLocalDraftStore.archiveBeforeSwitch()
            editorRoute = route
        } catch {
            self.error = "Önceki taslak korunamadığı için yeni çalışma açılmadı. \(error.localizedDescription)"
        }
    }

    @State private var project: Drawing?
    @State private var error: String?
    @State private var selectedPiece: Int?
    @State private var drawingRevision = UUID()
    @State private var loadRequest = UUID()
    @State private var isLoading = false

    var body: some View {
        VStack(spacing: 12) {
            if isLoading { ProgressView("Proje açılıyor…") }
            if let project {
                HStack {
                    Button {
                        loadRequest = UUID()
                        isLoading = false
                        self.project = nil
                        selectedPiece = nil
                    } label: {
                        Label("Projeler", systemImage: "chevron.backward")
                            .frame(minHeight: 44)
                    }
                    Spacer()
                    Text(project.name).font(.headline).lineLimit(1)
                }
                if hasLocalDraft { resumeButton }
                if let editableDraft = project.editableDraft {
                    Button {
                        var copy = editableDraft
                        copy.id = UUID()
                        openEditor(EditorRoute(document: copy))
                    } label: {
                        Label("Kopyasını düzenle", systemImage: "square.on.square")
                            .frame(maxWidth: .infinity, minHeight: 48)
                    }
                    .buttonStyle(.borderedProminent)
                    .accessibilityHint("Bu basit çizimin yeni düzenlenebilir bir kopyasını açar. Kaynak dosyayı değiştirmez.")
                }
                if let notice = project.formatNotice {
                    Text(notice)
                        .font(.caption)
                        .foregroundStyle(.orange)
                        .accessibilityAddTraits(.isStaticText)
                }
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
                        bounds: selectedPiece.flatMap { project.pieces[$0].bounds } ?? project.bounds,
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
                home.disabled(isLoading)
            }
        }
        .padding()
        .frame(minWidth: 300, minHeight: 300)
        .sheet(item: $editorRoute, onDismiss: refreshProjects) { route in
            EasyCADWorkspace(example: route.example, imported: route.document)
        }
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
            } catch let failure as NSError where failure.domain == NSCocoaErrorDomain && failure.code == NSUserCancelledError {
                // The Files picker was dismissed without selecting a project.
            } catch { self.error = error.localizedDescription }
        }
        .onAppear {
            guard !didStart else { return }
            didStart = true
            #if DEBUG && targetEnvironment(simulator)
            if ProcessInfo.processInfo.arguments.contains("--ui-test-reset-draft") {
                CADLocalDraftStore.discard()
                CADProjectLibrary.resetTestLibrary()
            }
            #endif
            refreshProjects()
            #if DEBUG && targetEnvironment(simulator)
            guard ProcessInfo.processInfo.arguments.contains("--ui-test-fixture"),
                  let encoded = ProcessInfo.processInfo.environment["TCAD_UI_TEST_FIXTURE"],
                  let data = Data(base64Encoded: encoded), data.count <= 64 * 1024 else { return }
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent("ui-test-\(UUID().uuidString).tcad")
            defer { try? FileManager.default.removeItem(at: url) }
            do {
                try data.write(to: url, options: .atomic)
                let drawing = try Drawing.load(url)
                selectedPiece = nil
                drawingRevision = UUID()
                project = drawing
            } catch { self.error = error.localizedDescription }
            #endif
        }
        .task(id: recentRefresh) {
            if didStart { await readRecentProjects() }
        }
        .alert("Dosya açılamadı", isPresented: Binding(get: { error != nil }, set: { if !$0 { error = nil } })) {
            Button("Tamam", role: .cancel) { error = nil }
        } message: { Text(error ?? "") }
    }

    private var resumeButton: some View {
        Button(action: resumeEditor) {
            Label("Son çizime devam et", systemImage: "clock.arrow.circlepath")
                .frame(maxWidth: .infinity, minHeight: 48)
        }
        .buttonStyle(.bordered)
        .accessibilityHint("Cihazda otomatik kaydedilmiş taslağı açar.")
    }

    private var home: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                Text("MONO DI TREE").font(.caption.bold()).tracking(3)
                    .foregroundStyle(.secondary)
                Text("Kalıbını kolayca oluştur").font(.title.bold())
                Text("Bir şekil ekle, ölçülerini milimetre olarak gir ve projeni kaydet.")
                    .foregroundStyle(.secondary)

                Button { openEditor(EditorRoute()) } label: {
                    Label("Yeni proje oluştur", systemImage: "plus.square")
                        .frame(maxWidth: .infinity, minHeight: 50)
                }
                .buttonStyle(.borderedProminent)
                Button { openEditor(EditorRoute(example: true)) } label: {
                    Label("Örnekle dene", systemImage: "hand.tap")
                        .frame(maxWidth: .infinity, minHeight: 46)
                }
                .buttonStyle(.bordered)
                Button { showingImporter = true } label: {
                    Label("Dosya aç (.tcad)", systemImage: "folder")
                        .frame(maxWidth: .infinity, minHeight: 46)
                }
                .buttonStyle(.bordered)
                .accessibilityHint("Dosyalardan bir proje seçerek konturlarını önizleyin.")

                Divider()
                Text("Son projeler").font(.headline).accessibilityAddTraits(.isHeader)
                if hasLocalDraft { resumeButton }
                if recentProjects.isEmpty && !hasLocalDraft {
                    Text("Kaydettiğin projeler burada görünür.")
                        .font(.subheadline).foregroundStyle(.secondary)
                }
                ForEach(recentProjects) { entry in
                    Button {
                        do { openEditor(EditorRoute(document: try CADProjectLibrary.open(entry.id))) }
                        catch { self.error = "Proje açılamadı. \(error.localizedDescription)" }
                    } label: {
                        HStack {
                            Image(systemName: "doc")
                            VStack(alignment: .leading) {
                                Text(entry.name)
                                Text(entry.modified, format: .dateTime.day().month().year().hour().minute())
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                            Spacer()
                            Image(systemName: "chevron.forward").font(.caption)
                        }
                        .frame(minHeight: 48)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityIdentifier("recent-\(entry.id.uuidString)")
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

}

private enum ViewerError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case let .message(text) = self { return text }; return nil }
}

private struct Drawing {
    struct Point { let x: Double; let y: Double }
    struct Bounds {
        var minX: Double
        var maxX: Double
        var minY: Double
        var maxY: Double

        init(_ point: Point) {
            minX = point.x
            maxX = point.x
            minY = point.y
            maxY = point.y
        }

        mutating func include(_ point: Point) {
            minX = min(minX, point.x)
            maxX = max(maxX, point.x)
            minY = min(minY, point.y)
            maxY = max(maxY, point.y)
        }

        mutating func include(_ other: Bounds) {
            minX = min(minX, other.minX)
            maxX = max(maxX, other.maxX)
            minY = min(minY, other.minY)
            maxY = max(maxY, other.maxY)
        }
    }
    struct Grain { let start: Point; let end: Point; let needsReview: Bool }
    struct Piece {
        let name: String?
        let points: [Point]
        let bounds: Bounds?
        let closed: Bool
        let role: String?
        let size: String?
        let material: String?
        let quantity: Int?
        let grainStatus: String
        let grain: Grain?

        func displayName(index: Int) -> String {
            guard let name, !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                return "Parça \(index + 1)"
            }
            return name
        }
    }
    let name: String
    let pieces: [Piece]
    let bounds: Bounds?
    let formatNotice: String?
    let editableDraft: DraftProject?

    // JSONSerialization bridges both JSON booleans and numbers through NSNumber.
    // Check the underlying type before conversion so malformed files cannot turn
    // true/false into coordinates or numeric values into contour flags.
    private static func jsonNumber(_ value: Any?) -> Double? {
        guard let number = value as? NSNumber,
              CFGetTypeID(number) != CFBooleanGetTypeID() else { return nil }
        return number.doubleValue
    }

    private static func jsonBoolean(_ value: Any?) -> Bool? {
        guard let number = value as? NSNumber,
              CFGetTypeID(number) == CFBooleanGetTypeID() else { return nil }
        return number.boolValue
    }
    static func load(_ url: URL) throws -> Drawing {
        let values = try url.resourceValues(forKeys: [.fileSizeKey])
        if let size = values.fileSize, size > 64 * 1024 * 1024 { throw ViewerError.message("Dosya 64 MB sınırını aşıyor.") }
        let data = try CADFileIO.read(url, limit: 64 * 1024 * 1024)
        let root: Any
        do {
            root = try JSONSerialization.jsonObject(with: data)
        } catch {
            throw ViewerError.message("Desteklenmeyen proje biçimi.")
        }
        let object: [String: Any]
        var schemaVersion = 0
        if let array = root as? [Any], array.count == 2,
           let number = array[0] as? NSNumber,
           CFGetTypeID(number) != CFBooleanGetTypeID(),
           number.doubleValue.isFinite, number.doubleValue.rounded() == number.doubleValue,
           (1...9).contains(number.intValue), let body = array[1] as? [String: Any] {
            schemaVersion = number.intValue
            object = body
        } else if let body = root as? [String: Any] { object = body }
        else { throw ViewerError.message("Desteklenmeyen proje biçimi.") }
        // Inspect only contours, not the full Windows production schema. Guard a few
        // version-required envelopes to avoid silently accepting obviously broken files.
        if schemaVersion == 6 {
            let rawPieces = object["Pieces"] as? [[String: Any]] ?? []
            guard rawPieces.contains(where: { $0["Notches"] != nil }) else {
                throw ViewerError.message("Sürüm 6 çentik işareti içermiyor.")
            }
        }
        if schemaVersion == 7 {
            guard let nesting = object["NestingSnapshot"] as? [String: Any],
                  nesting["RotationTrace"] is [String: Any] else {
                throw ViewerError.message("Sürüm 7 döndürme izi eksik.")
            }
        }
        if schemaVersion == 8 {
            guard object["Layers"] is [[String: Any]] else {
                throw ViewerError.message("Sürüm 8 katman listesi eksik.")
            }
        }
        if schemaVersion == 9 {
            let rawPieces = object["Pieces"] as? [[String: Any]] ?? []
            var foundControls = false
            for piece in rawPieces {
                guard let curves = piece["Curves"] else { continue }
                foundControls = true
                guard jsonBoolean(piece["Closed"]) == false,
                      let elements = curves as? [[String: Any]],
                      !elements.isEmpty, elements.count <= 2_000 else {
                    throw ViewerError.message("Sürüm 9 parametrik konturu geçersiz.")
                }
                for curve in elements {
                    guard let kind = curve["$type"] as? String else {
                        throw ViewerError.message("Sürüm 9 parametrik konturu geçersiz.")
                    }
                    let expected: Set<String>
                    switch kind {
                    case "line": expected = ["$type", "Start", "End"]
                    case "cubicBezier": expected = ["$type", "P0", "P1", "P2", "P3"]
                    case "arc": expected = ["$type", "Center", "Radius", "StartAngle", "SweepAngle"]
                    default: throw ViewerError.message("Bilinmeyen parametrik kontur türü.")
                    }
                    guard Set(curve.keys) == expected else {
                        throw ViewerError.message("Sürüm 9 parametrik konturu geçersiz.")
                    }
                    for key in expected where key != "$type" {
                        if ["Radius", "StartAngle", "SweepAngle"].contains(key) {
                            guard let number = jsonNumber(curve[key]),
                                  number.isFinite,
                                  (key != "Radius" || number > 0) else {
                                throw ViewerError.message("Sürüm 9 parametrik konturu geçersiz.")
                            }
                        } else {
                            guard let point = curve[key] as? [String: Any],
                                  Set(point.keys).isSubset(of: ["X", "Y", "IsFinite"]),
                                  let x = jsonNumber(point["X"]), let y = jsonNumber(point["Y"]),
                                  x.isFinite, y.isFinite,
                                  abs(x) <= 100_000_000, abs(y) <= 100_000_000,
                                  (point["IsFinite"] == nil || jsonBoolean(point["IsFinite"]) == true) else {
                                throw ViewerError.message("Sürüm 9 parametrik konturu geçersiz.")
                            }
                        }
                    }
                }
            }
            guard foundControls else {
                throw ViewerError.message("Sürüm 9 parametrik kontur içermiyor.")
            }
        } else if let pieces = object["Pieces"] as? [[String: Any]],
                  pieces.contains(where: { $0["Curves"] != nil }) {
            throw ViewerError.message("Parametrik kontur için sürüm 9 gerekir.")
        }
        guard let name = object["Name"] as? String, let rawPieces = object["Pieces"] as? [[String: Any]], rawPieces.count <= 10_000 else {
            throw ViewerError.message("Proje adı veya parça listesi eksik ya da sınır dışında.")
        }
        var total = 0
        var pieces: [Piece] = []
        var bounds: Bounds?
        for raw in rawPieces {
            guard let rawPoints = raw["Points"] as? [[String: Any]], rawPoints.count <= 10_000,
                  let closed = jsonBoolean(raw["Closed"]) else { throw ViewerError.message("Parça konturu eksik veya sınır dışında.") }
            total += rawPoints.count
            guard total <= 200_000 else { throw ViewerError.message("Toplam nokta sınırı aşıldı.") }
            var pieceBounds: Bounds?
            let points: [Point] = try rawPoints.map { point in
                guard let x = jsonNumber(point["X"]), let y = jsonNumber(point["Y"]),
                      x.isFinite, y.isFinite, abs(x) < 1e100, abs(y) < 1e100 else {
                    throw ViewerError.message("Geçersiz veya gösterilemeyen koordinat.")
                }
                let coordinate = Point(x: x, y: y)
                if pieceBounds == nil { pieceBounds = Bounds(coordinate) }
                else { pieceBounds?.include(coordinate) }
                return coordinate
            }
            let metadata = raw["Metadata"] as? [String: Any]
            let quantityNumber = metadata?["Quantity"] as? NSNumber
            let quantity: Int? = quantityNumber.flatMap { number in
                guard CFGetTypeID(number) != CFBooleanGetTypeID(), number.doubleValue.isFinite,
                      number.doubleValue.rounded() == number.doubleValue,
                      number.doubleValue >= 0, number.doubleValue <= 1_000_000 else { return nil }
                return number.intValue
            }
            let grainStatus: String
            var parsedGrain: Grain?
            if let grain = raw["Grainline"] as? [String: Any],
               let start = grain["Start"] as? [String: Any], let end = grain["End"] as? [String: Any],
               let sx = jsonNumber(start["X"]), let sy = jsonNumber(start["Y"]),
               let ex = jsonNumber(end["X"]), let ey = jsonNumber(end["Y"]),
               sx.isFinite, sy.isFinite, ex.isFinite, ey.isFinite,
               [sx, sy, ex, ey].allSatisfy({ abs($0) < 1e100 }),
               (sx != ex || sy != ey), let review = jsonBoolean(grain["NeedsReview"]) {
                grainStatus = review ? "İplik yönü: Kontrol bekliyor" : "İplik yönü: Kayıtlı"
                parsedGrain = Grain(start: Point(x: sx, y: sy), end: Point(x: ex, y: ey), needsReview: review)
            } else if raw["Grainline"] == nil || raw["Grainline"] is NSNull {
                grainStatus = "İplik yönü: Belirtilmemiş"
            } else {
                grainStatus = "İplik yönü: Okunamadı"
            }
            if points.count > 1, let pieceBounds {
                if bounds == nil { bounds = pieceBounds }
                else { bounds?.include(pieceBounds) }
            }
            pieces.append(Piece(name: raw["Name"] as? String, points: points, bounds: pieceBounds, closed: closed,
                                role: metadata?["Role"] as? String, size: metadata?["Size"] as? String,
                                material: metadata?["Material"] as? String, quantity: quantity,
                                grainStatus: grainStatus, grain: parsedGrain))
        }
        let formatNotice = schemaVersion == 9
            ? "Sürüm 9 yalnız kontur önizlemesi: Bézier/yay kontrol parametreleri iPhone'da düzenlenemez veya bağımsız doğrulanamaz. Windows CAD ile açın. Fabrika veya kesim doğrulaması değildir."
            : schemaVersion >= 6
            ? "Sürüm \(schemaVersion) yalnız kontur önizlemesi: çentikler, döndürülmüş pastal izleri ve katman görünürlüğü uygulanmaz. Fabrika veya kesim doğrulaması değildir."
            : nil
        let editableDraft = CADSketchImport.loadEditableCopy(from: data, jsonRoot: root)
        return Drawing(name: name, pieces: pieces, bounds: bounds, formatNotice: formatNotice,
                       editableDraft: editableDraft)
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

    var body: some View {
        if let bounds = piece.bounds {
            VStack(alignment: .leading, spacing: 3) {
                Text("Kontur bilgisi").font(.subheadline.bold())
                Text("\(piece.points.count) nokta • \(piece.closed ? "Kapalı" : "Açık") kontur")
                if let role = piece.role, !role.isEmpty { Text("Rol: \(role)") }
                if let size = piece.size, !size.isEmpty { Text("Beden: \(size)") }
                if let material = piece.material, !material.isEmpty { Text("Malzeme: \(material)") }
                if let quantity = piece.quantity { Text("Adet: \(quantity)") }
                Text(piece.grainStatus)
                Text("En: \((bounds.maxX - bounds.minX).formatted(.number.precision(.fractionLength(0...2)))) • Boy: \((bounds.maxY - bounds.minY).formatted(.number.precision(.fractionLength(0...2))))")
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
    let bounds: Drawing.Bounds?
    let label: String
    @State private var zoom: CGFloat = 1
    @State private var offset: CGSize = .zero
    @State private var showGrain = true
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
                VStack(alignment: .trailing, spacing: 8) {
                    Button("Görünümü sıfırla") { zoom = 1; offset = .zero }
                    if pieces.contains(where: { $0.grain != nil }) {
                        Button(showGrain ? "İplik yönlerini gizle" : "İplik yönlerini göster") { showGrain.toggle() }
                    }
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
            guard let bounds else { return }
            let width = bounds.maxX - bounds.minX, height = bounds.maxY - bounds.minY
            guard width > 0 || height > 0 else { return }
            let horizontalScale = width > 0 ? max(Double(size.width) - 32, 1) / max(width, 1e-100) : .infinity
            let verticalScale = height > 0 ? max(Double(size.height) - 32, 1) / max(height, 1e-100) : .infinity
            let scale = min(horizontalScale, verticalScale)
            let offsetX = (Double(size.width) - width * scale) / 2
            let offsetY = (Double(size.height) - height * scale) / 2
            func position(_ p: Drawing.Point) -> CGPoint {
                CGPoint(x: offsetX + (p.x - bounds.minX) * scale, y: Double(size.height) - offsetY - (p.y - bounds.minY) * scale)
            }
            for piece in pieces {
                guard let first = piece.points.first else { continue }
                var path = Path()
                path.move(to: position(first))
                for point in piece.points.dropFirst() { path.addLine(to: position(point)) }
                if piece.closed && piece.points.count > 1 { path.closeSubpath() }
                context.stroke(path, with: .color(.accentColor), lineWidth: 1.5)
                if showGrain, let grain = piece.grain {
                    let start = position(grain.start), end = position(grain.end)
                    let dx = end.x - start.x, dy = end.y - start.y
                    let length = hypot(dx, dy)
                    guard length.isFinite && length > 0.1 else { continue }
                    let ux = dx / length, uy = dy / length
                    let arrow = min(CGFloat(9), length / 3)
                    var grainPath = Path()
                    grainPath.move(to: start); grainPath.addLine(to: end)
                    grainPath.move(to: CGPoint(x: end.x - ux * arrow - uy * arrow * 0.5, y: end.y - uy * arrow + ux * arrow * 0.5))
                    grainPath.addLine(to: end)
                    grainPath.addLine(to: CGPoint(x: end.x - ux * arrow + uy * arrow * 0.5, y: end.y - uy * arrow - ux * arrow * 0.5))
                    context.stroke(grainPath, with: .color(grain.needsReview ? .orange : .primary),
                                   style: StrokeStyle(lineWidth: 1.5, dash: grain.needsReview ? [4, 3] : []))
                }
            }
        }
    }
}
