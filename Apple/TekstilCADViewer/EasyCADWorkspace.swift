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
    // Zoom and pan of the drawing. The live values follow a gesture that is still in progress.
    @State private var viewport = DraftViewport()
    @State private var liveZoom = 1.0
    @State private var livePan = CGSize.zero
    @State private var panning = false
    @State private var pinching = false
    @State private var canvasSize = CGSize.zero
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var editingPoints: DraftPiece?
    @State private var showingTouchComposer = false
    @State private var shouldStoreImportedSeed = false
    // Last verified on-disk state, distinct from the next UI edit. An absent
    // draft uses nil; resumed sessions start from their verified snapshot.
    @State private var persistedSnapshot: DraftProject?
    @State private var savedLibrarySnapshot: DraftProject?
    // The pinned drawing gives way to the keyboard so the focused field stays reachable.
    @State private var keyboardVisible = false
    // The zoom buttons grow with the text size and never drop below the 44 pt touch target.
    @ScaledMetric(relativeTo: .body) private var zoomButtonBase: CGFloat = 44
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize
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
    /// True only when the saved library copy equals the drawing and nothing typed is still pending.
    private var isSavedToLibrary: Bool { savedLibrarySnapshot == document && !hasPendingDetails }

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
        if id != nil { Haptics.selection() }
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
            problem = "Çizim sınırı doldu. Yeni şekil eklenemedi."
            return
        }
        var updated = document
        updated.pieces.append(piece)
        guard commit(updated, notice: "Parmakla çizdiğiniz \(piece.points.count) noktalı şekil eklendi.") else { return }
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
        guard !name.isEmpty else {
            problem = "Proje adı boş olamaz."
            return false
        }
        guard name.count <= 128 else {
            problem = "Proje adı en fazla 128 karakter olabilir (şu an \(name.count))."
            return false
        }
        var updated = document
        updated.name = name
        if let index = selectedIndex {
            let selectedName = pieceName.trimmingCharacters(in: .whitespacesAndNewlines)
            let piece = updated.pieces[index]
            if let message = detailsProblem(for: piece, name: selectedName) {
                problem = message
                return false
            }
            guard let resized = piece.applyingDimensions(x: pieceX, y: pieceY,
                                                         width: pieceWidth, height: pieceHeight) else {
                problem = dimensionProblem(for: piece)
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

    // MARK: Field messages
    // Each message names the box and the reason, so nobody has to guess which one is wrong.
    // They follow the checks of DraftPiece.applyingDimensions without changing its behaviour.

    /// Reads a box the way applyingDimensions does: untouched text keeps the exact stored value.
    private func parsedMillimetres(_ text: String, original: Double) -> Double? {
        text == DraftPiece.display(original) ? original : DraftContourGeometry.coordinate(text)
    }

    private func fieldProblem(_ title: String, text: String, original: Double,
                              mustBePositive: Bool) -> String? {
        if text == DraftPiece.display(original) { return nil }
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return "\(title): boş bırakılamaz. Milimetre cinsinden bir sayı yazın." }
        guard let value = DraftContourGeometry.coordinate(text) else {
            let separators = trimmed.filter { $0 == "," || $0 == "." }.count
            if separators > 1 {
                return "\(title): birden çok virgül ya da nokta var. Ondalık için tek virgül kullanın."
            }
            if let number = Double(trimmed.replacingOccurrences(of: ",", with: ".")), number.isFinite {
                return "\(title): değer çalışma alanı sınırını aşıyor (en çok 100 000 000 mm)."
            }
            return "\(title): sayı değil. Yalnız rakam, eksi işareti ve ondalık için virgül yazın; birim yazmayın."
        }
        if mustBePositive && value <= 0 {
            return "\(title): 0'dan büyük olmalı (sıfır ve eksi değer olmaz)."
        }
        return nil
    }

    /// The first problem in the name or in the four boxes, or nil when each of them reads fine.
    private func detailsProblem(for piece: DraftPiece, name: String) -> String? {
        if name.isEmpty { return "Parça adı boş olamaz." }
        if name.count > 128 { return "Parça adı en fazla 128 karakter olabilir (şu an \(name.count))." }
        struct Box {
            let title: String
            let text: String
            let original: Double
            let positive: Bool
        }
        var boxes = [
            Box(title: "X (soldan uzaklık)", text: pieceX, original: piece.minX, positive: false),
            Box(title: "Y (alttan uzaklık)", text: pieceY, original: piece.minY, positive: false),
            Box(title: piece.closed ? "En" : "Uzunluk", text: pieceWidth,
                original: piece.lineLength, positive: true)
        ]
        if piece.closed {
            boxes.append(Box(title: "Boy", text: pieceHeight,
                             original: piece.maxY - piece.minY, positive: true))
        }
        for box in boxes {
            if let message = fieldProblem(box.title, text: box.text, original: box.original,
                                          mustBePositive: box.positive) { return message }
        }
        return nil
    }

    /// Why applyingDimensions refused values that each read fine on their own.
    private func dimensionProblem(for piece: DraftPiece) -> String {
        let generic = "Parça adını, konumunu ve milimetre ölçülerini kontrol edin."
        let left = piece.minX, bottom = piece.minY
        let oldWidth = piece.lineLength, oldHeight = piece.maxY - bottom
        guard let x = parsedMillimetres(pieceX, original: left),
              let y = parsedMillimetres(pieceY, original: bottom),
              let width = parsedMillimetres(pieceWidth, original: oldWidth),
              let height = parsedMillimetres(pieceHeight, original: oldHeight) else { return generic }
        guard oldWidth > 0, !piece.closed || oldHeight > 0 else {
            return "Bu şeklin eni ya da boyu sıfır olduğu için ölçüleri yazarak değiştirilemez."
        }
        let sx = width / oldWidth
        let sy = piece.closed ? height / oldHeight : sx
        let moved: [DraftPoint] = piece.points.map { p in
            DraftPoint(x: sx == 1 ? p.x + (x - left) : x + (p.x - left) * sx,
                       y: sy == 1 ? p.y + (y - bottom) : y + (p.y - bottom) * sy)
        }
        let limit = DraftContourGeometry.coordinateLimit
        guard moved.allSatisfy({ abs($0.x) <= limit && abs($0.y) <= limit }) else {
            return "Yeni ölçüler ya da konum çalışma alanı sınırını (100 000 000 mm) aşıyor."
        }
        if moved.count <= DraftContourGeometry.pointLimit,
           let reason = DraftContourGeometry.validationError(points: moved, closed: piece.closed) {
            return "Bu ölçülerle şekil geçersiz olur. " + reason
        }
        return generic
    }

    /// A hint shown beside a box while its text is easy to misread. It never changes the value.
    private func separatorNotice(for text: String) -> String? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        let separators = trimmed.filter { $0 == "," || $0 == "." }.count
        if separators > 1 {
            return "Birden çok nokta veya virgül var. Ondalık için tek virgül kullanın."
        }
        guard separators == 1,
              let separator = trimmed.firstIndex(where: { $0 == "," || $0 == "." }) else { return nil }
        let whole = String(trimmed[..<separator]).replacingOccurrences(of: "-", with: "")
        let fraction = String(trimmed[trimmed.index(after: separator)...])
        // Like 1.250: three digits after the only separator. It is read as 1,25, not as 1250.
        guard !whole.isEmpty, whole.count <= 3, whole.first != "0",
              whole.allSatisfy({ $0.isASCII && $0.isNumber }),
              fraction.count == 3, fraction.allSatisfy({ $0.isASCII && $0.isNumber }),
              let value = DraftContourGeometry.coordinate(trimmed) else { return nil }
        return "\(trimmed) sayısı \(DraftPiece.display(value)) olarak okunur. Nokta ve virgül ondalık ayraçtır, binlik ayraç değildir."
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
            // Named after the project, so the file is recognisable in Files and Mail.
            let folder = FileManager.default.temporaryDirectory
                .appendingPathComponent(UUID().uuidString, isDirectory: true)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let url = folder.appendingPathComponent(CADSketchImport.exportFileName(forProjectNamed: document.name))
            try bytes.write(to: url, options: .atomic)
            exportURL = url
            success = "Dosya hazır. Aşağıdaki «Paylaş veya Dosyalara kaydet» düğmesine dokun."
        } catch {
            problem = "Dosya hazırlanamadı: \(error.localizedDescription)"
        }
    }

    // MARK: Guidance
    // One sentence that says what to do next, chosen from the state of the drawing.
    @ViewBuilder
    private var coachTip: some View {
        if document.pieces.isEmpty {
            CoachTip(systemImage: "1.circle.fill", title: "Başlamak için bir şekil ekle",
                     text: "Aşağıdan Dikdörtgen ya da Çizgi seç. Ölçüleri sonra kendin yazarsın.")
        } else if selectedIndex == nil {
            CoachTip(systemImage: "hand.tap.fill", title: "Düzenlemek için parçaya dokun",
                     text: "Çizimdeki bir parçaya dokun ya da aşağıdaki Parça seç menüsünü kullan.")
        } else {
            CoachTip(systemImage: "ruler", title: "Ölçüleri milimetre olarak yaz",
                     text: "Kutulara yaz ve uygula. Parçayı parmağınla sürükleyebilirsin. Bitince en alttan kaydet.")
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
        // Very large text needs the room for the tools more than for the drawing.
        return dynamicTypeSize.isAccessibilitySize ? 160 : 230
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
            // This panel does not scroll, so its text is limited: it must not push the tools away.
            if !document.pieces.isEmpty {
                Text(viewport.isFit
                     ? "\(document.pieces.count) parça • Önizleme ölçüye göre ekrana sığdırılır"
                     : "\(document.pieces.count) parça • \(String(format: "%.1f", viewport.zoom))× yakınlaştırıldı")
                    .font(.caption).foregroundStyle(AppTheme.muted)
                    .lineLimit(dynamicTypeSize.isAccessibilitySize ? 1 : 2)
            }
            if let success {
                Text(success).font(.footnote).foregroundStyle(AppTheme.muted)
                    .lineLimit(dynamicTypeSize.isAccessibilitySize ? 1 : 2)
            }
        }
        .padding(.horizontal)
        .padding(.top, 8)
        .padding(.bottom, 6)
    }

    private func addRectangleButton(fill: Bool) -> some View {
        Button { addRectangle() } label: {
            Label("Dikdörtgen ekle", systemImage: "square")
                .frame(maxWidth: fill ? .infinity : nil, minHeight: 48)
        }
        .buttonStyle(.appPrimary)
    }

    private func addLineButton(fill: Bool) -> some View {
        Button { addLine() } label: {
            Label("Çizgi ekle", systemImage: "line.diagonal")
                .frame(maxWidth: fill ? .infinity : nil, minHeight: 48)
        }
        .buttonStyle(.appSecondary)
    }

    private var addSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label("1 · Parça ekle", systemImage: "plus.square.on.square").font(.headline)
                .accessibilityAddTraits(.isHeader)
            // Side by side only at their natural one-line widths; otherwise stacked full width
            // (narrow screens, large text). An equal split would give the longer label half the
            // row and wrap it even though the pair as a whole fits.
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 10) {
                    addRectangleButton(fill: false).fixedSize(horizontal: true, vertical: false)
                    addLineButton(fill: false).fixedSize(horizontal: true, vertical: false)
                }
                VStack(spacing: 10) {
                    addRectangleButton(fill: true)
                    addLineButton(fill: true)
                }
            }
            Button {
                if !hasPendingDetails || saveDetails() { showingTouchComposer = true }
            } label: {
                Label("Parmakla şekil çiz", systemImage: "pencil.tip.crop.circle")
                    .frame(maxWidth: .infinity, minHeight: 50)
            }
            .buttonStyle(.appSecondary)
        }
    }

    private func millimetreField(_ title: String, label: String, text: Binding<String>,
                                 current: String) -> some View {
        VStack(alignment: .leading) {
            Text(label).font(.caption)
            TextField(title, text: text)
                .textFieldStyle(.roundedBorder)
                .submitLabel(.done)
                #if os(iOS)
                // Digits, minus, comma and point without switching keyboards.
                .keyboardType(.numbersAndPunctuation)
                #endif
            if text.wrappedValue != current, let notice = separatorNotice(for: text.wrappedValue) {
                Text(notice)
                    .font(.caption).foregroundStyle(AppTheme.danger)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    @ViewBuilder
    private var horizontalMoveButtons: some View {
        Button("← 10 mm") { move(dx: -10, dy: 0) }
            .accessibilityHint("Seçili parçayı 10 milimetre sola taşır")
        Button("10 mm →") { move(dx: 10, dy: 0) }
            .accessibilityHint("Seçili parçayı 10 milimetre sağa taşır")
    }

    // CAD Y grows upwards, so "up" adds to Y.
    @ViewBuilder
    private var verticalMoveButtons: some View {
        Button("↑ 10 mm") { move(dx: 0, dy: 10) }
            .accessibilityHint("Seçili parçayı 10 milimetre yukarı taşır")
        Button("↓ 10 mm") { move(dx: 0, dy: -10) }
            .accessibilityHint("Seçili parçayı 10 milimetre aşağı taşır")
    }

    private var rotateButton: some View {
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
            millimetreField("X", label: "Soldan uzaklık (mm)", text: $pieceX,
                            current: DraftPiece.display(document.pieces[index].minX))
            millimetreField("Y", label: "Alttan uzaklık (mm)", text: $pieceY,
                            current: DraftPiece.display(document.pieces[index].minY))
        }
        if document.pieces[index].closed {
            HStack {
                millimetreField("En", label: "En (mm)", text: $pieceWidth,
                                current: DraftPiece.display(document.pieces[index].lineLength))
                millimetreField("Boy", label: "Boy (mm)", text: $pieceHeight,
                                current: DraftPiece.display(document.pieces[index].maxY
                                                            - document.pieces[index].minY))
            }
        } else {
            millimetreField("Uzunluk", label: "Toplam çizgi uzunluğu (mm)", text: $pieceWidth,
                            current: DraftPiece.display(document.pieces[index].lineLength))
        }
        Button("Ölçüleri, konumu ve adı uygula") { _ = saveDetails() }
            .buttonStyle(.appPrimary)
        if document.pieces[index].points.count <= DraftContourGeometry.pointLimit {
            Button("Köşe noktalarını düzenle") {
                if !hasPendingDetails || saveDetails() { editingPoints = document.pieces[index] }
            }
            .buttonStyle(.appSecondary)
        } else {
            Text("Nokta düzenleme en fazla 250 noktalı şekillerde kullanılabilir.")
                .font(.caption)
        }
        // One row when everything fits; otherwise the rotate button wraps first, then the
        // vertical pair, and finally every button gets its own line.
        ViewThatFits(in: .horizontal) {
            HStack {
                horizontalMoveButtons
                verticalMoveButtons
                rotateButton
            }
            VStack(alignment: .leading) {
                HStack {
                    horizontalMoveButtons
                    verticalMoveButtons
                }
                rotateButton
            }
            VStack(alignment: .leading) {
                HStack { horizontalMoveButtons }
                HStack { verticalMoveButtons }
                rotateButton
            }
            VStack(alignment: .leading) {
                horizontalMoveButtons
                verticalMoveButtons
                rotateButton
            }
        }
        .buttonStyle(.appSecondary)
        .font(.caption)
        ViewThatFits(in: .horizontal) {
            HStack { copyDeleteButtons }
            VStack(alignment: .leading) { copyDeleteButtons }
        }
        .buttonStyle(.appSecondary)
    }

    private var editSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label("2 · Düzenle", systemImage: "slider.horizontal.3").font(.headline)
                .accessibilityAddTraits(.isHeader)
            Toggle("5 mm'lik adımlarla sürükle", isOn: $snapToFiveMillimeters)
                .font(.caption)
            Text("Bir parçaya dokunarak seç. Parmağınla sürükleyerek yerini değiştir.")
                .font(.caption).foregroundStyle(AppTheme.muted)
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
                .accessibilityAddTraits(.isHeader)
            TextField("Proje adı", text: $projectName)
                .textFieldStyle(.roundedBorder)
            Text("Çizimin cihazda otomatik saklanır. Listede kalıcı görünmesi için Projeyi kaydet'e bas.")
                .font(.caption).foregroundStyle(AppTheme.muted)
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
            .buttonStyle(.appPrimary)
            // Words and an icon, not a colour, tell whether the saved copy is current.
            HStack(spacing: 6) {
                Image(systemName: isSavedToLibrary ? "checkmark.circle" : "circle.dashed")
                    .accessibilityHidden(true)
                Text(isSavedToLibrary ? "Kaydedildi" : "Henüz kaydedilmedi")
            }
            .font(.caption).foregroundStyle(AppTheme.muted)
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
            .buttonStyle(.appSecondary)
            // One prominent action per section: saving the project. Preparing a file is secondary
            // until it is ready to share.
            Button { if saveDetails() { prepareExport() } } label: {
                Label("Dosyayı hazırla (.tcad)", systemImage: "doc.badge.plus")
                    .frame(maxWidth: .infinity, minHeight: 48)
            }
            .buttonStyle(.appSecondary)
            if let exportURL, !hasPendingDetails {
                ShareLink(item: exportURL, preview: SharePreview("Mono Di Tree CAD çizimi")) {
                    Label("Paylaş veya Dosyalara kaydet", systemImage: "square.and.arrow.up")
                        .frame(maxWidth: .infinity, minHeight: 48)
                }
                .buttonStyle(.appPrimary)
            }
            Text("Yalnız yeni çizim parçaları dışa aktarılır. Bu araç, içe alınmış fabrika dosyalarını değiştirmez.")
                .font(.caption).foregroundStyle(AppTheme.muted)
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
                    VStack(alignment: .leading, spacing: 16) {
                        coachTip.appCard()
                        addSection.appCard()
                        if !document.pieces.isEmpty { editSection.appCard() }
                        saveSection.appCard()
                        Text("Ölçüler milimetredir. Çizimler cihazda otomatik saklanır • Üretim/kesim onayı değildir.")
                            .font(.caption).foregroundStyle(AppTheme.muted)
                    }
                    .padding()
                    .controlSize(.large)
                }
                .scrollDismissesKeyboard(.interactively)
                .accessibilityIdentifier("workshop-tools")
            }
            .background(AppTheme.page.ignoresSafeArea())
            .animation(reduceMotion ? nil : .default, value: keyboardVisible)
            // The drawing can leave the screen while the keyboard changes; a drag that was in
            // progress then never ends, so its state is dropped here.
            .onChange(of: keyboardVisible) { _ in
                dragStarted = false
                draggedPieceID = nil
                currentDrag = .zero
                panning = false
                pinching = false
                livePan = .zero
                liveZoom = 1
                // The drawing gets smaller with the keyboard up; a zoomed view would point at
                // places that are no longer on screen.
                if keyboardVisible { viewport = DraftViewport() }
            }
            .onChange(of: document.pieces.isEmpty) { isEmpty in
                if isEmpty { viewport = DraftViewport() }
            }
            .onChange(of: problem) { _ in
                // An alert can cancel a gesture half way; its live values must not stay behind.
                liveZoom = 1
                livePan = .zero
                panning = false
                pinching = false
            }
            .onChange(of: success) { message in announce(message) }
            .onChange(of: viewport.zoom) { zoom in
                announce(viewport.isFit ? "Ekrana sığdırıldı"
                                        : "Yakınlaştırma \(String(format: "%.1f", zoom)) kat")
            }
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
                ContourPointEditor(piece: piece) { edited -> String? in
                    guard let index = document.pieces.firstIndex(where: { $0.id == edited.id }) else {
                        return "Şekil artık çizimde yok."
                    }
                    var updated = document
                    updated.pieces[index] = edited
                    guard commit(updated, notice: "Şekil noktaları güncellendi.") else {
                        // The editor shows the reason itself; the drawing's own alert would sit behind it.
                        let reason = problem ?? "Değişiklik kaydedilemedi."
                        problem = nil
                        return reason
                    }
                    select(edited)
                    return nil
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
        // The drawing now sits at the top of the sheet, where dragging a piece downwards would also
        // pull the sheet. Closing goes through "Kapat", which applies pending details first.
        .interactiveDismissDisabled(true)
        .preferredColorScheme(.light)
        .tint(AppTheme.accent)
        .alert("İşlem tamamlanamadı", isPresented: Binding(
            get: { problem != nil },
            set: { if !$0 { problem = nil } }
        )) {
            Button("Tamam", role: .cancel) { problem = nil }
        } message: {
            Text(problem ?? "")
        }
    }

    private func announce(_ message: String?) {
        #if os(iOS)
        if let message { UIAccessibility.post(notification: .announcement, argument: message) }
        #endif
    }

    // Drawing, hit testing and finger movement share the same transform.
    private func fitMap(_ size: CGSize) -> DraftCanvasMap? {
        guard let first = document.pieces.first?.points.first, size.width > 40, size.height > 40 else { return nil }
        var minX = first.x, maxX = first.x, minY = first.y, maxY = first.y
        for piece in document.pieces {
            for p in piece.points {
                minX = min(minX, p.x); maxX = max(maxX, p.x)
                minY = min(minY, p.y); maxY = max(maxY, p.y)
            }
        }
        // The margin leaves room for the measurement tags around the selected piece.
        return DraftCanvasMap(size: size, minX: minX, maxX: maxX, minY: minY, maxY: maxY, inset: 30)
    }

    private func transform(_ size: CGSize) -> DraftCanvasMap? {
        guard let fit = fitMap(size) else { return nil }
        let live = viewport.zoomed(by: liveZoom)
            .panned(by: livePan, in: size, content: fit.contentSize)
        return fit.viewed(live, in: size)
    }

    /// Brings the selected piece to the middle of the drawing area without any gesture.
    private func centerOnSelected() {
        guard let index = selectedIndex, let fit = fitMap(canvasSize) else { return }
        let piece = document.pieces[index]
        let middle = DraftPoint(x: (piece.minX + piece.maxX) / 2, y: (piece.minY + piece.maxY) / 2)
        let pan = fit.panCentering(middle, zoom: viewport.zoom, in: canvasSize)
        viewport = DraftViewport(zoom: viewport.zoom)
            .panned(by: pan, in: canvasSize, content: fit.contentSize)
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
        if dx != 0 || dy != 0 {
            move(dx: dx, dy: dy)
            Haptics.drop()
        }
    }

    /// A small white tag with the measurement, kept fully inside the drawing area.
    private func drawLabel(_ text: String, center: CGPoint, in context: inout GraphicsContext, size: CGSize) {
        let resolved = context.resolve(Text(text).font(.caption2.weight(.semibold)).foregroundColor(AppTheme.accent))
        let box = resolved.measure(in: CGSize(width: 160, height: 40))
        let w = box.width + 10, h = box.height + 4
        guard size.width > w + 4, size.height > h + 4 else { return }
        let cx = min(max(center.x, w / 2 + 2), size.width - w / 2 - 2)
        let cy = min(max(center.y, h / 2 + 2), size.height - h / 2 - 2)
        let tag = Path(roundedRect: CGRect(x: cx - w / 2, y: cy - h / 2, width: w, height: h), cornerRadius: 6)
        context.fill(tag, with: .color(AppTheme.card.opacity(0.92)))
        context.stroke(tag, with: .color(AppTheme.accent.opacity(0.35)), lineWidth: 1)
        context.draw(resolved, at: CGPoint(x: cx, y: cy), anchor: .center)
    }

    /// Width and height (or length) of the selected piece, written next to it in millimetres.
    private func drawMeasurements(of piece: DraftPiece, mapper: DraftCanvasMap,
                                  in context: inout GraphicsContext, size: CGSize) {
        guard size.height >= 150, piece.points.count >= 2 else { return }
        let delta = piece.id == draggedPieceID ? currentDrag : .zero
        let a = mapper.screen(DraftPoint(x: piece.minX, y: piece.minY))
        let b = mapper.screen(DraftPoint(x: piece.maxX, y: piece.maxY))
        let left = min(a.x, b.x) + delta.width, right = max(a.x, b.x) + delta.width
        let top = min(a.y, b.y) + delta.height, bottom = max(a.y, b.y) + delta.height
        if piece.closed {
            drawLabel("\(DraftPiece.display(piece.maxX - piece.minX)) mm",
                      center: CGPoint(x: (left + right) / 2, y: bottom + 12), in: &context, size: size)
            drawLabel("\(DraftPiece.display(piece.maxY - piece.minY)) mm",
                      center: CGPoint(x: right + 28, y: (top + bottom) / 2), in: &context, size: size)
        } else {
            drawLabel("\(DraftPiece.display(piece.lineLength)) mm",
                      center: CGPoint(x: (left + right) / 2, y: bottom + 12), in: &context, size: size)
        }
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
                if let selected = document.pieces.first(where: { $0.id == selectedID }) {
                    drawMeasurements(of: selected, mapper: mapper, in: &context, size: size)
                }
            }
            .contentShape(Rectangle())
            .simultaneousGesture(SpatialTapGesture().onEnded { value in
                _ = selectFromCanvasOrMenu(hitPiece(value.location, size: geometry.size)?.id)
            })
            .simultaneousGesture(DragGesture(minimumDistance: 8)
                .onChanged { value in
                    // Two fingers moving together are a pinch, not a drag of a piece or the view.
                    if pinching { return }
                    if !dragStarted {
                        dragStarted = true
                        let piece = hitPiece(value.startLocation, size: geometry.size)
                        if let piece, selectFromCanvasOrMenu(piece.id),
                           !hasPendingDetails || saveDetails() { draggedPieceID = piece.id }
                        // Dragging empty paper moves the view instead of a piece.
                        panning = piece == nil && !viewport.isFit
                    }
                    if draggedPieceID != nil { currentDrag = value.translation }
                    else if panning { livePan = value.translation }
                }
                .onEnded { value in
                    if panning {
                        viewport = viewport.panned(by: value.translation, in: geometry.size,
                                                   content: fitMap(geometry.size)?.contentSize ?? .zero)
                        panning = false
                        livePan = .zero
                    }
                    finishDrag(value.translation, size: geometry.size)
                })
            .simultaneousGesture(MagnificationGesture()
                .onChanged { value in
                    pinching = true
                    // A piece that is being dragged keeps its scale until it is dropped.
                    if draggedPieceID == nil { liveZoom = Double(value) }
                }
                .onEnded { _ in
                    // Commit what was shown; liveZoom is 1 when a piece was being dragged.
                    viewport = viewport.zoomed(by: liveZoom)
                    liveZoom = 1
                    pinching = false
                })
            .onAppear { canvasSize = geometry.size }
            .onChange(of: geometry.size) { canvasSize = $0 }
        }
        .clipped()
        .frame(height: canvasHeight)
        .frame(maxWidth: .infinity)
        .background(AppTheme.card, in: RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(AppTheme.line, lineWidth: 1))
        .accessibilityLabel("Dokunarak parça seçebileceğiniz kalıp önizlemesi")
        .accessibilityHint("Bir parçaya dokunarak seçin ve sürükleyerek taşıyın. Yakınlaştırmak için yukarı, uzaklaştırmak için aşağı kaydırın. Alternatif: Parça seç menüsü.")
        .accessibilityValue(canvasSpokenValue)
        .accessibilityAdjustableAction { direction in
            switch direction {
            case .increment: viewport = viewport.zoomed(by: DraftViewport.step)
            case .decrement: viewport = viewport.zoomed(by: 1 / DraftViewport.step)
            @unknown default: break
            }
        }
        .overlay {
            if document.pieces.isEmpty {
                VStack(spacing: 4) {
                    Text("Kalıbını oluştur").font(.headline)
                    Text("Aşağıdan bir şekil ekle. Ölçüler milimetredir.")
                        .font(.subheadline).foregroundStyle(AppTheme.muted)
                        .multilineTextAlignment(.center)
                }
                .padding(8)
                .allowsHitTesting(false)
            }
        }
        .overlay(alignment: .bottomTrailing) {
            if !document.pieces.isEmpty && !keyboardVisible { zoomControls }
        }
    }

    private var canvasSpokenValue: String {
        var text = "\(document.pieces.count) parça. "
        if let index = selectedIndex {
            let piece = document.pieces[index]
            text += "Seçili: \(piece.name). "
            if piece.closed {
                text += "En \(DraftPiece.display(piece.maxX - piece.minX)) milimetre, boy \(DraftPiece.display(piece.maxY - piece.minY)) milimetre. "
            } else {
                text += "Uzunluk \(DraftPiece.display(piece.lineLength)) milimetre. "
            }
        }
        text += viewport.isFit ? "Ekrana sığdırılmış." : "Yakınlaştırma \(String(format: "%.1f", viewport.zoom)) kat."
        return text
    }

    // Buttons for the same zoom the pinch gesture gives, so nothing depends on two fingers.
    private var zoomControls: some View {
        HStack(spacing: 6) {
            if !viewport.isFit {
                zoomButton("Ekrana sığdır", systemImage: "arrow.up.left.and.down.right.magnifyingglass",
                           id: "zoom-fit", enabled: true) { viewport = DraftViewport() }
                    .keyboardShortcut("0", modifiers: .command)
            }
            if !viewport.isFit && selectedIndex != nil {
                zoomButton("Seçili parçayı göster", systemImage: "scope",
                           id: "zoom-selected", enabled: true) { centerOnSelected() }
            }
            zoomButton("Uzaklaştır", systemImage: "minus.magnifyingglass",
                       id: "zoom-out", enabled: viewport.canZoomOut) {
                viewport = viewport.zoomed(by: 1 / DraftViewport.step)
            }
            .keyboardShortcut("-", modifiers: .command)
            zoomButton("Yakınlaştır", systemImage: "plus.magnifyingglass",
                       id: "zoom-in", enabled: viewport.canZoomIn) {
                viewport = viewport.zoomed(by: DraftViewport.step)
            }
            .keyboardShortcut("+", modifiers: .command)
        }
        .padding(6)
    }

    /// 44 pt at the default text size, larger with big text, capped so four buttons still fit.
    private var zoomButtonSide: CGFloat { min(56, max(44, zoomButtonBase)) }

    private func zoomButton(_ title: String, systemImage: String, id: String, enabled: Bool,
                            action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: systemImage)
                .font(.body.weight(.semibold))
                .frame(width: zoomButtonSide, height: zoomButtonSide)
                .background(.thinMaterial, in: Circle())
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
        .opacity(enabled ? 1 : 0.4)
        .accessibilityLabel(title)
        .accessibilityIdentifier(id)
    }
}

/// Reports whether the software keyboard is showing. It does nothing on macOS.
private struct KeyboardVisibility: ViewModifier {
    @Binding var visible: Bool

    func body(content: Content) -> some View {
        #if os(iOS)
        content
            .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillShowNotification)) { note in
                // With a hardware keyboard only a short bar appears; that is not worth shrinking
                // the drawing for.
                let frame = note.userInfo?[UIResponder.keyboardFrameEndUserInfoKey] as? CGRect
                visible = (frame?.height ?? 0) > 120
            }
            .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillHideNotification)) { _ in
                visible = false
            }
        #else
        content
        #endif
    }
}
