import SwiftUI
#if canImport(UIKit)
import UIKit
#endif

// A deliberately small, genuinely editable workspace. It creates new contour-only
// .tcad documents, never rewrites an imported factory project or strips metadata.
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
    @State private var dragStarted = false
    @State private var draggedPieceID: UUID?
    @State private var currentDrag = CGSize.zero
    @State private var editingPoints: DraftPiece?
    @State private var showingTouchComposer = false
    @State private var shouldStoreImportedSeed = false
    // Last verified on-disk state, distinct from the next UI edit. An absent
    // draft uses nil; resumed sessions start from their verified snapshot.
    @State private var persistedSnapshot: DraftProject?
    @State private var savedLibrarySnapshot: DraftProject?
    // The pinned drawing gives way to the keyboard so the focused field stays reachable.
    @State private var keyboardVisible = false
    #if os(iOS)
    @Environment(\.verticalSizeClass) private var verticalSizeClass
    #endif
    @FocusState private var pieceNameFocused: Bool

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

    private func selectFromCanvasOrMenu(_ id: UUID?) -> Bool {
        if id == selectedID { return true }
        if hasPendingDetails && !saveDetails() { return false }
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

    private func addRectangle() {
        guard !hasPendingDetails || saveDetails() else { return }
        guard document.pieces.count < 250 else { problem = "Parça sınırına ulaşıldı."; return }
        var updated = document
        let start = Double(document.pieces.count) * 20
        let piece = DraftPiece(name: "Dikdörtgen \(document.pieces.count + 1)", closed: true, points: [
            DraftPoint(x: start, y: start), DraftPoint(x: start + 100, y: start),
            DraftPoint(x: start + 100, y: start + 150), DraftPoint(x: start, y: start + 150)
        ])
        updated.pieces.append(piece)
        guard commit(updated, notice: "Yeni dikdörtgen eklendi. Ölçüleri milimetre cinsindendir.") else { return }
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

    private func addLine() {
        guard !hasPendingDetails || saveDetails() else { return }
        guard document.pieces.count < 250 else { problem = "Parça sınırına ulaşıldı."; return }
        var updated = document
        let start = Double(document.pieces.count) * 20
        let piece = DraftPiece(name: "Çizgi \(document.pieces.count + 1)", closed: false, points: [
            DraftPoint(x: start, y: start), DraftPoint(x: start + 100, y: start)
        ])
        updated.pieces.append(piece)
        guard commit(updated, notice: "100 mm uzunluğunda bir çizgi eklendi.") else { return }
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
            updated.pieces[index].name = selectedName
        }
        guard commit(updated, notice: "Değişiklikler uygulandı.") else { return false }
        projectName = updated.name
        if let index = selectedIndex { select(updated.pieces[index]) }
        return true
    }

    private func move(dx: Double, dy: Double) {
        guard !hasPendingDetails || saveDetails() else { return }
        guard let index = selectedIndex else { return }
        var updated = document
        let points = updated.pieces[index].points.map { DraftPoint(x: $0.x + dx, y: $0.y + dy) }
        guard points.allSatisfy({ abs($0.x) <= 100_000_000 && abs($0.y) <= 100_000_000 }) else {
            problem = "Kalıp çalışma alanı sınırını aşıyor."; return
        }
        updated.pieces[index].points = points
        guard commit(updated, notice: "Parça taşındı.") else { return }
        select(document.pieces[index])
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
        guard copy.points.allSatisfy({ abs($0.x) <= 100_000_000 && abs($0.y) <= 100_000_000 }) else {
            problem = "Kopya çalışma alanı sınırını aşıyor."; return
        }
        updated.pieces.append(copy)
        guard commit(updated, notice: "Bağımsız bir parça kopyası oluşturuldu.") else { return }
        select(copy)
    }

    private func deleteSelected() {
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

    private func prepareExport() {
        guard !document.pieces.isEmpty else { problem = "Önce en az bir parça ekleyin."; return }
        do {
            let bytes = try CADSketchImport.encode(document)
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent("MonoDiTree-\(UUID().uuidString).tcad")
            try bytes.write(to: url, options: .atomic)
            exportURL = url
            success = "Dosya hazır. Paylaş düğmesinden Dosyalara Kaydet'i seçin."
        } catch {
            problem = "Dosya hazırlanamadı: \(error.localizedDescription)"
        }
    }

    // MARK: Layout
    // The drawing stays on screen while the tools below it scroll, so a dimension can be
    // typed while looking at the piece it changes.

    private var canvasHeight: CGFloat {
        if keyboardVisible { return 110 }
        #if os(iOS)
        if verticalSizeClass == .compact { return 130 }
        #endif
        return 230
    }

    /// With the keyboard up in landscape there is no room left for both the drawing and a field.
    private var showsCanvas: Bool {
        #if os(iOS)
        if keyboardVisible && verticalSizeClass == .compact { return false }
        #endif
        return true
    }

    private var canvasPanel: some View {
        VStack(alignment: .leading, spacing: 4) {
            preview
            if !document.pieces.isEmpty {
                Text("\(document.pieces.count) parça • Önizleme ölçüye göre ekrana sığdırılır")
                    .font(.caption).foregroundStyle(.secondary)
            }
            if let success {
                Text(success).font(.footnote).foregroundStyle(.secondary)
            }
        }
        .padding(.horizontal)
        .padding(.top, 8)
        .padding(.bottom, 6)
    }

    private var addRectangleButton: some View {
        Button { addRectangle() } label: {
            Label("Dikdörtgen ekle", systemImage: "square")
                .frame(maxWidth: .infinity, minHeight: 48)
        }
        .buttonStyle(.borderedProminent)
    }

    private var addLineButton: some View {
        Button { addLine() } label: {
            Label("Çizgi ekle", systemImage: "line.diagonal")
                .frame(maxWidth: .infinity, minHeight: 48)
        }
        .buttonStyle(.bordered)
    }

    private var addSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label("1 · Parça ekle", systemImage: "plus.square.on.square").font(.headline)
            // Side by side when both labels fit on one line, otherwise stacked (narrow
            // screens, large text), so a label is never broken inside a word.
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 10) {
                    addRectangleButton
                    addLineButton
                }
                VStack(spacing: 10) {
                    addRectangleButton
                    addLineButton
                }
            }
            Button {
                if !hasPendingDetails || saveDetails() { showingTouchComposer = true }
            } label: {
                Label("Parmakla yeni kontur çiz", systemImage: "pencil.tip.crop.circle")
                    .frame(maxWidth: .infinity, minHeight: 50)
            }
            .buttonStyle(.bordered)
        }
    }

    private func millimetreField(_ title: String, label: String, text: Binding<String>) -> some View {
        VStack(alignment: .leading) {
            Text(label).font(.caption)
            TextField(title, text: text)
                .textFieldStyle(.roundedBorder)
                .submitLabel(.done)
                #if os(iOS)
                // Digits, minus, comma and point without switching keyboards.
                .keyboardType(.numbersAndPunctuation)
                #endif
        }
    }

    @ViewBuilder
    private var moveButtons: some View {
        Button("← 10 mm") { move(dx: -10, dy: 0) }
        Button("10 mm →") { move(dx: 10, dy: 0) }
        Button("90° döndür") { rotateSelected() }
    }

    @ViewBuilder
    private var copyDeleteButtons: some View {
        Button("Kopyala", action: duplicateSelected)
        Button("Sil", role: .destructive, action: deleteSelected)
    }

    @ViewBuilder
    private func selectedPieceControls(_ index: Int) -> some View {
        TextField("Parça adı", text: $pieceName)
            .textFieldStyle(.roundedBorder)
            .accessibilityLabel("Seçili parçanın adı")
            .submitLabel(.done)
            .focused($pieceNameFocused)
            .onSubmit { pieceNameFocused = false }
        HStack {
            millimetreField("X", label: "X (mm)", text: $pieceX)
            millimetreField("Y", label: "Y (mm)", text: $pieceY)
        }
        if document.pieces[index].closed {
            HStack {
                millimetreField("En", label: "En (mm)", text: $pieceWidth)
                millimetreField("Boy", label: "Boy (mm)", text: $pieceHeight)
            }
        } else {
            millimetreField("Uzunluk", label: "Toplam çizgi uzunluğu (mm)", text: $pieceWidth)
        }
        Button("Ölçüleri, konumu ve adı uygula") { _ = saveDetails() }
            .buttonStyle(.borderedProminent)
        if document.pieces[index].points.count <= DraftContourGeometry.pointLimit {
            Button("Kontur noktalarını düzenle") {
                if !hasPendingDetails || saveDetails() { editingPoints = document.pieces[index] }
            }
            .buttonStyle(.bordered)
        } else {
            Text("Nokta düzenleme en fazla 250 noktalı konturlarda kullanılabilir.")
                .font(.caption)
        }
        ViewThatFits(in: .horizontal) {
            HStack { moveButtons }
            VStack(alignment: .leading) { moveButtons }
        }
        .buttonStyle(.bordered)
        .font(.caption)
        ViewThatFits(in: .horizontal) {
            HStack { copyDeleteButtons }
            VStack(alignment: .leading) { copyDeleteButtons }
        }
        .buttonStyle(.bordered)
    }

    private var editSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label("2 · Düzenle", systemImage: "slider.horizontal.3").font(.headline)
            Toggle("Sürüklerken 5 mm'ye hizala", isOn: $snapToFiveMillimeters)
                .font(.caption)
            Text("Bir parçaya dokunarak seç. Parmağınla sürükleyerek yerini değiştir.")
                .font(.caption).foregroundStyle(.secondary)
            Picker("Parça seç", selection: Binding(
                get: { selectedID },
                set: { _ = selectFromCanvasOrMenu($0) }
            )) {
                ForEach(document.pieces) { piece in
                    Text(piece.name).tag(Optional(piece.id))
                }
            }
            .pickerStyle(.menu)
            if let index = selectedIndex {
                selectedPieceControls(index)
            }
        }
    }

    private var saveSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label("3 · Kaydet", systemImage: "square.and.arrow.up").font(.headline)
            TextField("Proje adı", text: $projectName)
                .textFieldStyle(.roundedBorder)
            Button("Proje adını uygula") { _ = saveDetails() }
                .buttonStyle(.bordered)
            Button {
                guard saveDetails() else { return }
                do {
                    try CADProjectLibrary.save(document, replacing: savedLibrarySnapshot)
                    savedLibrarySnapshot = document
                    success = "Proje kaydedildi. Ana ekrandaki Son projeler bölümünden açabilirsiniz."
                } catch { problem = error.localizedDescription }
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
                    success = "Bağımsız kopya kaydedildi. Son projeler bölümünden açabilirsiniz."
                } catch { problem = "Kopya kaydedilemedi. " + error.localizedDescription }
            } label: {
                Label("Farklı kopya kaydet", systemImage: "doc.on.doc")
                    .frame(maxWidth: .infinity, minHeight: 48)
            }
            .buttonStyle(.bordered)
            // One prominent action per section: saving the project. Preparing a file is secondary
            // until it is ready to share.
            Button { if saveDetails() { prepareExport() } } label: {
                Label("Dosyayı hazırla (.tcad)", systemImage: "doc.badge.plus")
                    .frame(maxWidth: .infinity, minHeight: 48)
            }
            .buttonStyle(.bordered)
            if let exportURL, !hasPendingDetails {
                ShareLink(item: exportURL, preview: SharePreview("Mono Di Tree CAD çizimi")) {
                    Label("Paylaş veya Dosyalara kaydet", systemImage: "square.and.arrow.up")
                        .frame(maxWidth: .infinity, minHeight: 48)
                }
                .buttonStyle(.borderedProminent)
            }
            Text("Yalnız yeni çizim parçaları dışa aktarılır. Bu araç, içe alınmış fabrika dosyalarını değiştirmez.")
                .font(.caption).foregroundStyle(.secondary)
        }
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                if showsCanvas {
                    canvasPanel
                    Divider()
                }
                ScrollView {
                    VStack(alignment: .leading, spacing: 18) {
                        Text("Ölçüler milimetredir. Çizimler cihazda otomatik saklanır • Üretim/kesim onayı değildir.")
                            .font(.caption).foregroundStyle(.secondary)
                        addSection
                        if !document.pieces.isEmpty { editSection }
                        saveSection
                    }
                    .padding()
                    .controlSize(.large)
                }
                .scrollDismissesKeyboard(.interactively)
            }
            .animation(.default, value: keyboardVisible)
            .modifier(KeyboardVisibility(visible: $keyboardVisible))
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .navigationTitle("Kalıp atölyesi")
            .onAppear {
                if shouldStoreImportedSeed {
                    shouldStoreImportedSeed = false
                    // Only attach a library revision when the opened file
                    // exactly matches the document we actually displayed.
                    // A resumed draft may be newer than its last library save.
                    if let saved = try? CADProjectLibrary.open(document.id), saved == document {
                        savedLibrarySnapshot = saved
                    }
                    do {
                        let stored = try CADLocalDraftStore.restore()
                        if let stored {
                            // A resumed scene must not silently overwrite a
                            // different revision with the same project UUID.
                            guard stored == document else { throw SketchStoreError.changed }
                            // This is the resumed current draft. Switching projects archives
                            // the draft first, so the saved project with the same UUID is the
                            // revision this draft continues from, even when the draft is newer.
                            // Without it, "Projeyi kaydet" was always refused as a conflict.
                            // A revision saved after this point still rejects the save.
                            if savedLibrarySnapshot == nil {
                                savedLibrarySnapshot = try? CADProjectLibrary.open(document.id)
                            }
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
                    if commit(updated, notice: "Kontur noktaları güncellendi.") { select(edited) }
                }
            }
            .sheet(isPresented: $showingTouchComposer) {
                TouchContourComposer { newPiece in
                    addTouchContour(newPiece)
                }
            }
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Kapat") { if saveDetails() { dismiss() } }
                }
                // Always in reach, wherever the tools below are scrolled to.
                ToolbarItemGroup(placement: .primaryAction) {
                    Button(action: undo) {
                        Label("Geri al", systemImage: "arrow.uturn.backward")
                    }
                    .disabled(undoStack.isEmpty)
                    Button(action: redo) {
                        Label("İleri al", systemImage: "arrow.uturn.forward")
                    }
                    .disabled(redoStack.isEmpty)
                }
            }
        }
        .interactiveDismissDisabled(hasPendingDetails)
        .alert("İşlem tamamlanamadı", isPresented: Binding(
            get: { problem != nil },
            set: { if !$0 { problem = nil } }
        )) {
            Button("Tamam", role: .cancel) { problem = nil }
        } message: {
            Text(problem ?? "")
        }
    }

    // Drawing, hit testing and finger movement share the same transform.
    private func transform(_ size: CGSize) -> DraftCanvasMap? {
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
        guard let transform = transform(size) else { return nil }
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

    private func finishDrag(_ translation: CGSize, size: CGSize) {
        defer { dragStarted = false; draggedPieceID = nil; currentDrag = .zero }
        guard let index = selectedIndex, document.pieces[index].id == draggedPieceID,
              let transform = transform(size) else { return }
        let delta = transform.translation(translation, for: document.pieces[index], snap: snapToFiveMillimeters)
        let dx = delta.x, dy = delta.y
        guard dx.isFinite, dy.isFinite, abs(dx) <= 100_000_000, abs(dy) <= 100_000_000 else {
            problem = "Sürükleme çalışma alanını aşıyor."
            return
        }
        if dx != 0 || dy != 0 { move(dx: dx, dy: dy) }
    }

    private var preview: some View {
        GeometryReader { geometry in
            Canvas { context, size in
                guard let mapper = transform(size) else { return }
                for piece in document.pieces {
                    guard let first = piece.points.first else { continue }
                    let delta = piece.id == draggedPieceID ? currentDrag : .zero
                    func position(_ p: DraftPoint) -> CGPoint {
                        let q = mapper.screen(p)
                        return CGPoint(x: q.x + delta.width, y: q.y + delta.height)
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
            .contentShape(Rectangle())
            .simultaneousGesture(SpatialTapGesture().onEnded { value in
                _ = selectFromCanvasOrMenu(hitPiece(value.location, size: geometry.size)?.id)
            })
            .simultaneousGesture(DragGesture(minimumDistance: 8)
                .onChanged { value in
                    if !dragStarted {
                        dragStarted = true
                        let piece = hitPiece(value.startLocation, size: geometry.size)
                        if let piece, selectFromCanvasOrMenu(piece.id),
                           !hasPendingDetails || saveDetails() { draggedPieceID = piece.id }
                    }
                    if draggedPieceID != nil { currentDrag = value.translation }
                }
                .onEnded { value in
                    finishDrag(value.translation, size: geometry.size)
                })
        }
        .frame(height: canvasHeight)
        .frame(maxWidth: .infinity)
        .background(Color.secondary.opacity(0.08), in: RoundedRectangle(cornerRadius: 14))
        .accessibilityLabel("Dokunarak parça seçebileceğiniz kalıp önizlemesi")
        .accessibilityHint("Bir parçaya dokunarak seçin ve sürükleyerek taşıyın. Alternatif: Parça seç menüsü.")
        .overlay {
            if document.pieces.isEmpty {
                VStack(spacing: 4) {
                    Text("Kalıbını oluştur").font(.headline)
                    Text("Aşağıdan bir şekil ekle. Ölçüler milimetredir.")
                        .font(.subheadline).foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                }
                .padding(8)
                .allowsHitTesting(false)
            }
        }
    }
}

/// Reports whether the software keyboard is showing. It does nothing on macOS.
private struct KeyboardVisibility: ViewModifier {
    @Binding var visible: Bool

    func body(content: Content) -> some View {
        #if os(iOS)
        content
            .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillShowNotification)) { _ in
                visible = true
            }
            .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillHideNotification)) { _ in
                visible = false
            }
        #else
        content
        #endif
    }
}
