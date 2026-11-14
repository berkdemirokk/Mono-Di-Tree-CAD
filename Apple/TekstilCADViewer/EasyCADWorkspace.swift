import SwiftUI

/// Mono Di Tree CAD iPhone V1.0 düzenlenebilir çalışma alanı.
/// Sadece yeni iPhone çizimleriyle çalışır; içe alınmış fabrika dosyasını asla değiştirmez.
struct EasyCADWorkspace: View {
    @Environment(\.dismiss) private var dismiss
    @State private var document: DraftProject
    @State private var undoStack: [DraftProject] = []
    @State private var redoStack: [DraftProject] = []
    @State private var selectedID: UUID?
    @State private var projectName = ""
    @State private var pieceName = ""
    @State private var pieceX = ""
    @State private var pieceY = ""
    @State private var pieceWidth = ""
    @State private var pieceHeight = ""
    @State private var problem: String?
    @State private var success: String?
    @State private var exportURL: URL?
    @State private var snapToFiveMillimeters = true
    @State private var showingTouchComposer = false
    @State private var shouldStoreImportedSeed = false
    @State private var persistedSnapshot: DraftProject?
    @State private var savedLibrarySnapshot: DraftProject?
    @State private var showingDeleteConfirm = false
    @FocusState private var pieceNameFocused: Bool

    // Görünüm kamera durumu (çizim sekmesi için)
    @State private var viewZoom: CGFloat = 1
    @State private var viewOffset: CGSize = .zero

    init(example: Bool, imported: DraftProject? = nil) {
        let initial = imported ?? (example ? DraftProject.example : DraftProject.blank)
        _shouldStoreImportedSeed = State(initialValue: imported != nil)
        _document = State(initialValue: initial)
        _projectName = State(initialValue: initial.name)
        _selectedID = State(initialValue: initial.pieces.first?.id)
        if let first = initial.pieces.first {
            _pieceName = State(initialValue: first.name)
            _pieceX = State(initialValue: DraftPiece.display(first.minX))
            _pieceY = State(initialValue: DraftPiece.display(first.minY))
            _pieceWidth = State(initialValue: DraftPiece.display(first.lineLength))
            _pieceHeight = State(initialValue: DraftPiece.display(first.maxY - first.minY))
        }
    }

    private var selectedIndex: Int? {
        document.pieces.firstIndex(where: { $0.id == selectedID })
    }

    private var selectedPiece: DraftPiece? {
        selectedIndex.map { document.pieces[$0] }
    }

    private var hasPendingDetails: Bool {
        if projectName != document.name { return true }
        guard let index = selectedIndex else { return false }
        let piece = document.pieces[index]
        return pieceName != piece.name ||
            pieceX != DraftPiece.display(piece.minX) ||
            pieceY != DraftPiece.display(piece.minY) ||
            pieceWidth != DraftPiece.display(piece.lineLength) ||
            pieceHeight != DraftPiece.display(piece.maxY - piece.minY)
    }

    private func select(_ piece: DraftPiece?) {
        selectedID = piece?.id
        pieceName = piece?.name ?? ""
        pieceX = piece.map { DraftPiece.display($0.minX) } ?? ""
        pieceY = piece.map { DraftPiece.display($0.minY) } ?? ""
        pieceWidth = piece.map { DraftPiece.display($0.lineLength) } ?? ""
        pieceHeight = piece.map { DraftPiece.display($0.maxY - $0.minY) } ?? ""
    }

    private func selectFromMenu(_ id: UUID?) -> Bool {
        if id == selectedID { return true }
        var hasPending = projectName != document.name
        if let index = selectedIndex {
            let p = document.pieces[index]
            hasPending = hasPending || pieceName != p.name ||
                pieceX != DraftPiece.display(p.minX) ||
                pieceY != DraftPiece.display(p.minY) ||
                pieceWidth != DraftPiece.display(p.lineLength) ||
                pieceHeight != DraftPiece.display(p.maxY - p.minY)
        }
        if hasPending && !saveDetails() { return false }
        select(document.pieces.first { $0.id == id })
        return true
    }

    @discardableResult
    private func commit(_ updated: DraftProject, notice: String) -> Bool {
        guard updated != document else { return true }
        guard CADLocalDraftStore.isValid(updated) else {
            problem = "Değişiklik çizimin parça, nokta veya koordinat sınırlarını aşıyor."
            return false
        }
        guard persistDraft(updated) else { return false }
        undoStack.append(document)
        if undoStack.count > 50 { undoStack.removeFirst() }
        redoStack.removeAll()
        document = updated
        exportURL = nil
        success = notice
        return true
    }

    private func persistDraft(_ project: DraftProject) -> Bool {
        do {
            try CADLocalDraftStore.save(project, replacing: persistedSnapshot)
            persistedSnapshot = project
            return true
        } catch {
            problem = "Taslak kaydedilemedi. " + error.localizedDescription
            return false
        }
    }

    // MARK: - Şekil ekleme

    private func addRectangle() {
        guard !hasPendingDetails || saveDetails() else { return }
        guard document.pieces.count < 250 else { problem = "Parça sınırına ulaşıldı."; return }
        var updated = document
        let start = Double(document.pieces.count) * 40
        let piece = DraftPiece(name: "Dikdörtgen \(document.pieces.count + 1)", closed: true, points: [
            DraftPoint(x: start, y: start), DraftPoint(x: start + 100, y: start),
            DraftPoint(x: start + 100, y: start + 150), DraftPoint(x: start, y: start + 150)
        ])
        updated.pieces.append(piece)
        guard commit(updated, notice: "Yeni dikdörtgen eklendi. Ölçüleri milimetre cinsindendir.") else { return }
        select(piece)
    }

    private func addLine() {
        guard !hasPendingDetails || saveDetails() else { return }
        guard document.pieces.count < 250 else { problem = "Parça sınırına ulaşıldı."; return }
        var updated = document
        let start = Double(document.pieces.count) * 40
        let piece = DraftPiece(name: "Çizgi \(document.pieces.count + 1)", closed: false, points: [
            DraftPoint(x: start, y: start), DraftPoint(x: start + 100, y: start)
        ], curves: [.line(start: DraftPoint(x: start, y: start), end: DraftPoint(x: start + 100, y: start))])
        updated.pieces.append(piece)
        guard commit(updated, notice: "100 mm uzunluğunda bir çizgi eklendi.") else { return }
        select(piece)
    }

    private func addBezier() {
        guard !hasPendingDetails || saveDetails() else { return }
        guard document.pieces.count < 250 else { problem = "Parça sınırına ulaşıldı."; return }
        var updated = document
        let start = Double(document.pieces.count) * 40
        let p0 = DraftPoint(x: start, y: start)
        let p1 = DraftPoint(x: start, y: start + 80)
        let p2 = DraftPoint(x: start + 80, y: start + 80)
        let p3 = DraftPoint(x: start + 80, y: start)
        let bezier = CurveSegment.cubicBezier(p0: p0, p1: p1, p2: p2, p3: p3)
        let sampled = bezier.sampled()
        let piece = DraftPiece(name: "Bézier \(document.pieces.count + 1)", closed: false, points: sampled, curves: [bezier])
        updated.pieces.append(piece)
        guard commit(updated, notice: "Kubik Bézier eğrisi eklendi. Kontrol noktalarını düzenleyebilirsiniz.") else { return }
        select(piece)
    }

    private func addArc() {
        guard !hasPendingDetails || saveDetails() else { return }
        guard document.pieces.count < 250 else { problem = "Parça sınırına ulaşıldı."; return }
        var updated = document
        let start = Double(document.pieces.count) * 40
        let center = DraftPoint(x: start + 40, y: start + 40)
        let arc = CurveSegment.arc(center: center, radius: 40, startAngle: 0, sweepAngle: .pi / 2)
        let sampled = arc.sampled()
        let piece = DraftPiece(name: "Yay \(document.pieces.count + 1)", closed: false, points: sampled, curves: [arc])
        updated.pieces.append(piece)
        guard commit(updated, notice: "Dairesel yay eklendi (¼ daire, 40 mm yarıçap).") else { return }
        select(piece)
    }

    private func addTouchContour(_ piece: DraftPiece) {
        guard document.pieces.count < 250,
              document.pieces.reduce(0, { $0 + $1.points.count }) + piece.points.count <= 20_000 else {
            problem = "Çizim sınırı doldu. Yeni kontur eklenemedi."
            return
        }
        var updated = document
        updated.pieces.append(piece)
        guard commit(updated, notice: "Parmakla çizdiğiniz \(piece.points.count) noktalı kontur eklendi.") else { return }
        select(piece)
    }

    @discardableResult
    private func saveDetails() -> Bool {
        let name = projectName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, name.count <= 128 else {
            problem = "Proje adı 1–128 karakter arasında olmalı."
            return false
        }
        var updated = document
        updated.name = name
        if let index = selectedIndex {
            let selectedName = pieceName.trimmingCharacters(in: .whitespacesAndNewlines)
            let piece = updated.pieces[index]
            guard !selectedName.isEmpty, selectedName.count <= 128,
                  let resized = piece.applyingDimensions(x: pieceX, y: pieceY,
                                                         width: pieceWidth, height: pieceHeight) else {
                problem = "Parça adını, konumunu ve milimetre ölçülerini kontrol edin."
                return false
            }
            updated.pieces[index].points = resized
            updated.pieces[index].curves = nil
            updated.pieces[index].name = selectedName
        }
        guard commit(updated, notice: "Değişiklikler uygulandı.") else { return false }
        projectName = updated.name
        if let index = selectedIndex { select(updated.pieces[index]) }
        return true
    }

    // MARK: - Dönüşümler

    private func move(dx: Double, dy: Double) {
        guard !hasPendingDetails || saveDetails() else { return }
        guard let index = selectedIndex else { return }
        var updated = document
        let points = updated.pieces[index].points.map { DraftPoint(x: $0.x + dx, y: $0.y + dy) }
        guard points.allSatisfy({ abs($0.x) <= 100_000_000 && abs($0.y) <= 100_000_000 }) else {
            problem = "Kalıp çalışma alanı sınırını aşıyor."; return
        }
        updated.pieces[index].points = points
        if var curves = updated.pieces[index].curves {
            curves = curves.map { shift(curve: $0, dx: dx, dy: dy) }
            updated.pieces[index].curves = curves
        }
        guard commit(updated, notice: "Parça taşındı.") else { return }
        select(document.pieces[index])
    }

    private func shift(curve: CurveSegment, dx: Double, dy: Double) -> CurveSegment {
        switch curve {
        case .line(let s, let e):
            return .line(start: DraftPoint(x: s.x + dx, y: s.y + dy), end: DraftPoint(x: e.x + dx, y: e.y + dy))
        case .cubicBezier(let p0, let p1, let p2, let p3):
            return .cubicBezier(p0: DraftPoint(x: p0.x + dx, y: p0.y + dy),
                                p1: DraftPoint(x: p1.x + dx, y: p1.y + dy),
                                p2: DraftPoint(x: p2.x + dx, y: p2.y + dy),
                                p3: DraftPoint(x: p3.x + dx, y: p3.y + dy))
        case .arc(let c, let r, let sa, let sw):
            return .arc(center: DraftPoint(x: c.x + dx, y: c.y + dy), radius: r, startAngle: sa, sweepAngle: sw)
        }
    }

    private func rotateSelected() {
        guard !hasPendingDetails || saveDetails() else { return }
        guard let index = selectedIndex else { return }
        var updated = document
        let piece = updated.pieces[index]
        let points = piece.rotatedQuarterTurn()
        guard points.allSatisfy({ abs($0.x) <= 100_000_000 && abs($0.y) <= 100_000_000 }) else {
            problem = "Döndürülmüş parça çalışma alanı sınırını aşıyor."; return
        }
        updated.pieces[index].points = points
        updated.pieces[index].curves = nil
        guard commit(updated, notice: "Parça 90° döndürüldü.") else { return }
        select(document.pieces[index])
    }

    private func duplicateSelected() {
        guard !hasPendingDetails || saveDetails() else { return }
        guard let index = selectedIndex else { return }
        var updated = document
        var copy = updated.pieces[index]
        copy.id = UUID()
        copy.name = String(copy.name.prefix(122)) + " kopya"
        copy.points = copy.points.map { DraftPoint(x: $0.x + 20, y: $0.y + 20) }
        copy.curves = copy.curves?.map { shift(curve: $0, dx: 20, dy: 20) }
        guard copy.points.allSatisfy({ abs($0.x) <= 100_000_000 && abs($0.y) <= 100_000_000 }) else {
            problem = "Kopya çalışma alanı sınırını aşıyor."; return
        }
        updated.pieces.append(copy)
        guard commit(updated, notice: "Bağımsız bir parça kopyası oluşturuldu.") else { return }
        select(copy)
    }

    private func performDelete() {
        guard let index = selectedIndex else { return }
        var updated = document
        updated.pieces.remove(at: index)
        guard commit(updated, notice: "Parça silindi. Geri Al ile geri getirebilirsiniz.") else { return }
        select(updated.pieces.first)
    }

    private func undo() {
        guard let previous = undoStack.last, persistDraft(previous) else { return }
        undoStack.removeLast()
        redoStack.append(document)
        document = previous
        exportURL = nil
        select(document.pieces.first(where: { $0.id == selectedID }) ?? document.pieces.first)
        projectName = document.name
        success = "İşlem geri alındı."
    }

    private func redo() {
        guard let next = redoStack.last, persistDraft(next) else { return }
        redoStack.removeLast()
        undoStack.append(document)
        document = next
        exportURL = nil
        select(document.pieces.first(where: { $0.id == selectedID }) ?? document.pieces.first)
        projectName = document.name
        success = "İşlem yeniden uygulandı."
    }

    // MARK: - Dışa aktarım

    private func prepareExport() {
        guard !document.pieces.isEmpty else { problem = "Önce en az bir parça ekleyin."; return }
        do {
            let bytes = try CADSketchImport.encode(document)
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent("MonoDiTree-\(UUID().uuidString).tcad")
            try bytes.write(to: url, options: .atomic)
            exportURL = url
            success = "Dosya hazır. Paylaş düğmesinden 'Dosyalara Kaydet' seçin."
        } catch {
            problem = "Dosya hazırlanamadı: \(error.localizedDescription)"
        }
    }

    // MARK: - Görünüm (zoom/pan)

    private func limitedZoom(_ value: CGFloat) -> CGFloat {
        min(max(value.isFinite ? value : 1, 0.25), 12)
    }

    // MARK: - Body

    var body: some View {
        TabView {
            DrawTabView(
                document: $document, selectedID: $selectedID,
                addRectangle: addRectangle, addLine: addLine, addBezier: addBezier, addArc: addArc,
                addTouchContour: { showingTouchComposer = true },
                selectPiece: { id in _ = selectFromMenu(id) }
            )
            .tabItem { Label("Çizim", systemImage: "pencil") }
            .tag(0)

            EditTabView(
                document: $document, selectedID: $selectedID, projectName: $projectName,
                pieceName: $pieceName, pieceX: $pieceX, pieceY: $pieceY,
                pieceWidth: $pieceWidth, pieceHeight: $pieceHeight,
                saveDetails: saveDetails, move: move, rotateSelected: rotateSelected,
                duplicateSelected: duplicateSelected, deleteSelected: { showingDeleteConfirm = true },
                editingPoints: $editingPoints
            )
            .tabItem { Label("Düzenle", systemImage: "slider.horizontal.3") }
            .tag(1)

            ProjectTabView(
                document: $document, projectName: $projectName,
                undoStack: $undoStack, redoStack: $redoStack,
                saveDetails: saveDetails, undo: undo, redo: redo,
                prepareExport: prepareExport, exportURL: $exportURL
            )
            .tabItem { Label("Proje", systemImage: "folder") }
            .tag(2)
        }
        .tabViewStyle(.automatic)
        .onAppear {
            if shouldStoreImportedSeed {
                shouldStoreImportedSeed = false
                if let saved = try? CADProjectLibrary.open(document.id), saved == document {
                    savedLibrarySnapshot = saved
                }
                do {
                    let stored = try CADLocalDraftStore.restore()
                    if let stored {
                        guard stored == document else { throw SketchStoreError.changed }
                    } else {
                        try CADLocalDraftStore.save(document, replacing: nil)
                    }
                    persistedSnapshot = document
                } catch {
                    problem = "Kopya açılsa da taslak güvenle saklanamadı. " +
                        error.localizedDescription + " Kapatmadan önce .tcad dışa aktarın."
                }
            }
        }
        .sheet(item: $editingPoints) { piece in
            ContourPointEditor(piece: piece) { edited in
                guard let index = document.pieces.firstIndex(where: { $0.id == edited.id }) else { return }
                var updated = document
                updated.pieces[index] = edited
                updated.pieces[index].curves = nil
                if commit(updated, notice: "Kontur noktaları güncellendi.") { select(edited) }
            }
        }
        .sheet(isPresented: $showingTouchComposer) {
            TouchContourComposer { newPiece in
                addTouchContour(newPiece)
            }
        }
        .alert("Parçayı sil", isPresented: $showingDeleteConfirm) {
            Button("Sil", role: .destructive) { performDelete() }
            Button("Vazgeç", role: .cancel) {}
        } message: {
            Text("Seçili parçayı silmek istediğinize emin misiniz?")
        }
        .alert("İşlem tamamlanamadı", isPresented: Binding(
            get: { problem != nil },
            set: { if !$0 { problem = nil } }
        )) {
            Button("Tamam", role: .cancel) { problem = nil }
        } message: {
            Text(problem ?? "")
        }
    }
}

// MARK: - Boş DraftPiece yardımcıları

extension DraftPiece {
    static var blankPiece: DraftPiece {
        DraftPiece(name: "", closed: true, points: [])
    }
}

/// Çizim tuvali: zoom, parmak hareketi, parça seçimi.
private struct DrawTabView: View {
    @Binding var document: DraftProject
    @Binding var selectedID: UUID?
    let addRectangle: () -> Void
    let addLine: () -> Void
    let addBezier: () -> Void
    let addArc: () -> Void
    let addTouchContour: () -> Void
    let selectPiece: (UUID?) -> Void
    @State private var viewZoom: CGFloat = 1
    @State private var viewOffset: CGSize = .zero
    @GestureState private var viewPinch: CGFloat = 1
    @GestureState private var viewDrag: CGSize = .zero

    private var selectedPiece: DraftPiece? {
        document.pieces.first { $0.id == selectedID }
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    VStack(alignment: .leading, spacing: 5) {
                        Text("Kalıbını çiz").font(.title2.bold())
                        Text("Ölçüler milimetre (mm). Şekil ekle, ekrandan parçaya dokunarak seç ve sürükle.")
                            .foregroundStyle(.secondary)
                        Text("Çizimler cihazda otomatik saklanır • Üretim/kesim onayı değildir.")
                            .font(.caption).foregroundStyle(.secondary)
                    }

                    VStack(alignment: .leading, spacing: 10) {
                        Label("1 · Şekil ekle", systemImage: "plus.square.on.square").font(.headline)
                        LazyVGrid(columns: [
                            GridItem(.adaptive(minimum: 150), spacing: 10)
                        ], spacing: 10) {
                            toolButton(title: "Dikdörtgen", icon: "square", action: addRectangle, prominent: true)
                            toolButton(title: "Çizgi", icon: "line.diagonal", action: addLine)
                            toolButton(title: "Bézier", icon: "curlybraces", action: addBezier)
                            toolButton(title: "Yay", icon: "circlebadge", action: addArc)
                            toolButton(title: "Parmakla çiz", icon: "pencil.tip.crop.circle", action: addTouchContour)
                        }
                    }

                    canvas
                        .frame(height: 300)
                        .frame(maxWidth: .infinity)
                        .background(Color.secondary.opacity(0.08), in: RoundedRectangle(cornerRadius: 14))
                        .accessibilityLabel("Dokunarak parça seçebileceğiniz kalıp önizlemesi")
                        .accessibilityHint("Bir parçaya dokunarak seçin ve sürükleyerek taşıyın.")

                    if document.pieces.isEmpty {
                        Text("Çalışma alanın boş. Yukarıdan bir şekil seçerek başla.")
                            .foregroundStyle(.secondary)
                    } else {
                        Text("\(document.pieces.count) parça • Seçili parça kalın renkte gösterilir")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
                .padding()
            }
            .navigationTitle("Çizim")
        }
    }

    private func toolButton(title: String, icon: String, action: @escaping () -> Void, prominent: Bool = false) -> some View {
        Button { action() } label: {
            Label(title, systemImage: icon)
                .frame(maxWidth: .infinity, minHeight: 52)
        }
        .buttonStyle(prominent ? .borderedProminent : .bordered)
    }

    private var canvas: some View {
        GeometryReader { geometry in
            let currentZoom = min(max(viewZoom * viewPinch, 0.25), 12)
            ZStack(alignment: .topTrailing) {
                Canvas { context, size in
                    guard let mapper = canvasMap(size: size) else { return }
                    for piece in document.pieces {
                        guard let first = piece.points.first else { continue }
                        func position(_ p: DraftPoint) -> CGPoint {
                            let q = mapper.screen(p)
                            return CGPoint(x: q.x, y: q.y)
                        }
                        var path = Path()
                        path.move(to: position(first))
                        for p in piece.points.dropFirst() { path.addLine(to: position(p)) }
                        if piece.closed { path.closeSubpath() }
                        context.stroke(path,
                            with: .color(piece.id == selectedID ? .accentColor : .secondary),
                            lineWidth: piece.id == selectedID ? 3 : 1.8)
                    }
                }
                .scaleEffect(currentZoom, anchor: .topLeading)
                .offset(viewOffset)
                .frame(width: geometry.size.width, height: geometry.size.height)
                .contentShape(Rectangle())
                .simultaneousGesture(SpatialTapGesture().onEnded { value in
                    selectPiece(hitPiece(value.location, size: geometry.size)?.id)
                })
                .simultaneousGesture(DragGesture(minimumDistance: 8)
                    .onChanged { _ in }
                    .onEnded { value in
                        viewOffset = CGSize(width: viewOffset.width + value.translation.width,
                                            height: viewOffset.height + value.translation.height)
                    })
                VStack(alignment: .trailing, spacing: 8) {
                    Button("Görünümü sıfırla") { viewZoom = 1; viewOffset = .zero }
                    Button("Yakınlaştır +") { viewZoom = min(viewZoom * 1.25, 12) }
                    Button("Uzaklaştır −") { viewZoom = max(viewZoom / 1.25, 0.25) }
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
                    .updating($viewPinch) { value, state, _ in state = value }
                    .onEnded { value in
                        viewZoom = min(max(viewZoom * value, 0.25), 12)
                    }
            )
        }
    }

    private func canvasMap(size: CGSize) -> DraftCanvasMap? {
        guard let first = document.pieces.first?.points.first, size.width > 40, size.height > 40 else { return nil }
        var minX = first.x, maxX = first.x, minY = first.y, maxY = first.y
        for piece in document.pieces {
            for p in piece.points {
                minX = min(minX, p.x); maxX = max(maxX, p.x)
                minY = min(minY, p.y); maxY = max(maxY, p.y)
            }
        }
        return DraftCanvasMap(size: size, minX: minX, maxX: maxX, minY: minY, maxY: maxY, inset: 16)
    }

    private func segmentDistance(_ p: CGPoint, _ a: CGPoint, _ b: CGPoint) -> Double {
        let dx = Double(b.x - a.x), dy = Double(b.y - a.y)
        let d2 = dx * dx + dy * dy
        let t = d2 < 1e-12 ? 0 : max(0, min(1,
            (Double(p.x - a.x) * dx + Double(p.y - a.y) * dy) / d2))
        return hypot(Double(p.x - a.x) - t * dx, Double(p.y - a.y) - t * dy)
    }

    private func inside(_ point: CGPoint, polygon: [CGPoint]) -> Bool {
        guard polygon.count >= 3 else { return false }
        var isInside = false
        var j = polygon.count - 1
        for i in polygon.indices {
            let a = polygon[i], b = polygon[j]
            if (a.y > point.y) != (b.y > point.y) {
                let edgeX = Double(b.x - a.x) * Double(point.y - a.y) /
                    Double(b.y - a.y) + Double(a.x)
                if Double(point.x) < edgeX { isInside.toggle() }
            }
            j = i
        }
        return isInside
    }

    private func hitPiece(_ point: CGPoint, size: CGSize) -> DraftPiece? {
        guard let transform = canvasMap(size: size) else { return nil }
        var closest: (piece: DraftPiece, distance: Double)?
        for piece in document.pieces.reversed() {
            let vertices = piece.points.map(transform.screen)
            if piece.closed && inside(point, polygon: vertices) { return piece }
            guard vertices.count >= 2 else { continue }
            let count = piece.closed ? vertices.count : vertices.count - 1
            for index in 0..<count {
                let d = segmentDistance(point, vertices[index], vertices[(index + 1) % vertices.count])
                if closest == nil || d < closest!.distance { closest = (piece, d) }
            }
        }
        return (closest?.distance ?? Double.infinity) <= 24 ? closest?.piece : nil
    }
}

// MARK: - Düzenle sekmesi

private struct EditTabView: View {
    @Binding var document: DraftProject
    @Binding var selectedID: UUID?
    @Binding var projectName: String
    @Binding var pieceName: String
    @Binding var pieceX: String
    @Binding var pieceY: String
    @Binding var pieceWidth: String
    @Binding var pieceHeight: String
    let saveDetails: () -> Bool
    let move: (dx: Double, dy: Double) -> Void
    let rotateSelected: () -> Void
    let duplicateSelected: () -> Void
    let deleteSelected: () -> Void
    @Binding var editingPoints: DraftPiece?

    private var selectedIndex: Int? {
        document.pieces.firstIndex(where: { $0.id == selectedID })
    }

    private var selectedPiece: DraftPiece? {
        selectedIndex.map { document.pieces[$0] }
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    VStack(alignment: .leading, spacing: 5) {
                        Text("Parçayı düzenle").font(.title2.bold())
                        Text("Bir parça seç, konumunu veya adını değiştir. Ölçüler milimetre (mm).")
                            .foregroundStyle(.secondary)
                    }

                    if document.pieces.isEmpty {
                        Text("Düzenlenecek parça yok. Çizim sekmesinden bir şekil ekle.")
                            .foregroundStyle(.secondary)
                    } else {
                        VStack(alignment: .leading, spacing: 10) {
                            Label("2 · Parça seç", systemImage: "cursorarrow").font(.headline)
                            Picker("Parça", selection: Binding(
                                get: { selectedID },
                                set: { id in
                                    var hasPending = projectName != document.name
                                    if let index = selectedIndex {
                                        let p = document.pieces[index]
                                        hasPending = hasPending || pieceName != p.name ||
                                            pieceX != DraftPiece.display(p.minX) ||
                                            pieceY != DraftPiece.display(p.minY) ||
                                            pieceWidth != DraftPiece.display(p.lineLength) ||
                                            pieceHeight != DraftPiece.display(p.maxY - p.minY)
                                    }
                                    if hasPending && !saveDetails() { return }
                                    selectedID = id
                                    if let piece = document.pieces.first(where: { $0.id == id }) {
                                        pieceName = piece.name
                                        pieceX = DraftPiece.display(piece.minX)
                                        pieceY = DraftPiece.display(piece.minY)
                                        pieceWidth = DraftPiece.display(piece.lineLength)
                                        pieceHeight = DraftPiece.display(piece.maxY - piece.minY)
                                    }
                                }
                            )) {
                                ForEach(document.pieces) { piece in
                                    Text(piece.name).tag(Optional(piece.id))
                                }
                            }
                            .pickerStyle(.menu)
                            .frame(minHeight: 44)

                            if let selectedIndex {
                                TextField("Parça adı", text: $pieceName)
                                    .textFieldStyle(.roundedBorder)
                                    .accessibilityLabel("Seçili parçanın adı")
                                    .submitLabel(.done)

                                HStack {
                                    VStack(alignment: .leading) {
                                        Text("X (mm)").font(.caption)
                                        TextField("X", text: $pieceX).textFieldStyle(.roundedBorder)
                                    }
                                    VStack(alignment: .leading) {
                                        Text("Y (mm)").font(.caption)
                                        TextField("Y", text: $pieceY).textFieldStyle(.roundedBorder)
                                    }
                                }
                                if document.pieces[selectedIndex].closed {
                                    HStack {
                                        VStack(alignment: .leading) {
                                            Text("En (mm)").font(.caption)
                                            TextField("En", text: $pieceWidth).textFieldStyle(.roundedBorder)
                                        }
                                        VStack(alignment: .leading) {
                                            Text("Boy (mm)").font(.caption)
                                            TextField("Boy", text: $pieceHeight).textFieldStyle(.roundedBorder)
                                        }
                                    }
                                } else {
                                    Text("Toplam çizgi uzunluğu (mm)").font(.caption)
                                    TextField("Uzunluk", text: $pieceWidth)
                                        .textFieldStyle(.roundedBorder)
                                }

                                Button("Ölçüleri, konumu ve adı uygula") { _ = saveDetails() }
                                    .buttonStyle(.borderedProminent)

                                if let piece = selectedPiece, piece.points.count <= DraftContourGeometry.pointLimit {
                                    Button("Kontur noktalarını düzenle") { editingPoints = piece }
                                        .buttonStyle(.bordered)
                                } else if selectedPiece?.points.count ?? 0 > DraftContourGeometry.pointLimit {
                                    Text("Nokta düzenleme en fazla 250 noktalı konturlarda kullanılabilir.")
                                        .font(.caption)
                                }

                                HStack {
                                    Button("← 10 mm") { move(dx: -10, dy: 0) }
                                    Button("10 mm →") { move(dx: 10, dy: 0) }
                                    Button("90° döndür") { rotateSelected() }
                                }
                                .buttonStyle(.bordered)
                                .font(.caption)

                                HStack {
                                    Button("Kopyala", action: duplicateSelected)
                                    Button("Sil", role: .destructive, action: deleteSelected)
                                }
                                .buttonStyle(.bordered)
                            }
                        }
                    }
                }
                .padding()
            }
            .navigationTitle("Düzenle")
        }
    }
}

// MARK: - Proje sekmesi

private struct ProjectTabView: View {
    @Binding var document: DraftProject
    @Binding var projectName: String
    @Binding var undoStack: [DraftProject]
    @Binding var redoStack: [DraftProject]
    let saveDetails: () -> Bool
    let undo: () -> Void
    let redo: () -> Void
    let prepareExport: () -> Void
    @Binding var exportURL: URL?

    @State private var successMessage: String?
    @State private var problemMessage: String?

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    VStack(alignment: .leading, spacing: 5) {
                        Text("Projeyi kaydet").font(.title2.bold())
                        Text("Proje adını ver, kaydet ve dosyayı paylaş veya Dosyalara aktar.")
                            .foregroundStyle(.secondary)
                    }

                    VStack(alignment: .leading, spacing: 10) {
                        TextField("Proje adı", text: $projectName)
                            .textFieldStyle(.roundedBorder)
                            .accessibilityLabel("Proje adı")
                        Button("Proje adını uygula") { _ = saveDetails() }
                            .buttonStyle(.bordered)

                        HStack {
                            Button("Geri al", action: undo).disabled(undoStack.isEmpty)
                                .buttonStyle(.bordered)
                            Button("İleri al", action: redo).disabled(redoStack.isEmpty)
                                .buttonStyle(.bordered)
                        }

                        Button {
                            guard saveDetails() else { return }
                            do {
                                try CADProjectLibrary.save(document)
                                successMessage = "Proje kaydedildi. Ana ekrandaki Son projeler bölümünden açabilirsiniz."
                            } catch {
                                problemMessage = error.localizedDescription
                            }
                        } label: {
                            Label("Projeyi kaydet", systemImage: "tray.and.arrow.down")
                                .frame(maxWidth: .infinity, minHeight: 48)
                        }
                        .buttonStyle(.borderedProminent)

                        Button {
                            guard saveDetails() else { return }
                            var copy = document
                            copy.id = UUID()
                            copy.name = String(document.name.prefix(120)) + " (kopya)"
                            do {
                                try CADProjectLibrary.save(copy)
                                successMessage = "Bağımsız kopya kaydedildi. Son projeler bölümünden açabilirsiniz."
                            } catch {
                                problemMessage = "Kopya kaydedilemedi. " + error.localizedDescription
                            }
                        } label: {
                            Label("Farklı kopya kaydet", systemImage: "doc.on.doc")
                                .frame(maxWidth: .infinity, minHeight: 48)
                        }
                        .buttonStyle(.bordered)

                        Button {
                            guard saveDetails() else { return }
                            prepareExport()
                        } label: {
                            Label("Dosyayı hazırla (.tcad)", systemImage: "doc.badge.plus")
                                .frame(maxWidth: .infinity, minHeight: 48)
                        }
                        .buttonStyle(.borderedProminent)

                        if let exportURL {
                            ShareLink(item: exportURL, preview: SharePreview("Mono Di Tree CAD çizimi")) {
                                Label("Paylaş veya Dosyalara kaydet", systemImage: "square.and.arrow.up")
                                    .frame(maxWidth: .infinity, minHeight: 48)
                            }
                            .buttonStyle(.borderedProminent)
                        }

                        Text("Yalnız yeni çizim parçaları dışa aktarılır. İçe alınmış fabrika dosyası değiştirilmez.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
                .padding()
            }
            .navigationTitle("Proje")
            .alert("Bildirim", isPresented: Binding(
                get: { successMessage != nil || problemMessage != nil },
                set: { if !$0 { successMessage = nil; problemMessage = nil } }
            )) {
                Button("Tamam", role: .cancel) { successMessage = nil; problemMessage = nil }
            } message: {
                Text(successMessage ?? problemMessage ?? "")
            }
        }
    }
}
