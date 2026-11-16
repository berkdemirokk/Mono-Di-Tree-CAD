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
    // Where a pinch started (iOS 17 and later); nil zooms about the centre.
    @State private var pinchAnchor: CGPoint?
    // The rule for dragging while zoomed is explained once, the first time the view is zoomed.
    @AppStorage("workshop.panHintSeen") private var panHintSeen = false
    @State private var showPanHint = false
    @AccessibilityFocusState private var zoomInFocused: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var editingPoints: DraftPiece?
    @State private var showingTouchComposer = false
    // Set while "Kapat" waits for a choice about a box whose value cannot be applied.
    @State private var discardReason: String?
    #if os(iOS)
    // The file handed to the system share sheet; set only after it was written successfully.
    @State private var shareFile: ShareFile?
    #endif
    // Size of the whole workshop area. Wide areas put the drawing beside the tools.
    @State private var workspaceSize = CGSize.zero
    @State private var wideByShape = false
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
    @Environment(\.horizontalSizeClass) private var horizontalSizeClass
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
        guard commit(updated, notice: "Parmakla çizdiğin \(piece.points.count) noktalı şekil eklendi.") else { return }
        select(piece)
    }

    private func addCircle() {
        guard !hasPendingDetails || saveDetails() else { return }
        let start = Double(document.pieces.count) * 20
        appendShape(DraftPiece(name: "Daire \(document.pieces.count + 1)", closed: true,
                               points: DraftShapes.circle(left: start, bottom: start, diameter: 100)),
                    notice: "Çapı 100 mm olan bir daire eklendi. En ve Boy kutularıyla büyüklüğünü değiştirebilirsin.")
    }

    private func addTriangle() {
        guard !hasPendingDetails || saveDetails() else { return }
        let start = Double(document.pieces.count) * 20
        appendShape(DraftPiece(name: "Üçgen \(document.pieces.count + 1)", closed: true,
                               points: DraftShapes.triangle(left: start, bottom: start, width: 100, height: 100)),
                    notice: "Eni ve boyu 100 mm olan bir üçgen eklendi. Ölçüleri milimetre cinsindendir.")
    }

    /// Adds a ready-made shape within the drawing's piece and point limits and selects it.
    private func appendShape(_ piece: DraftPiece, notice: String) {
        guard document.pieces.count < 250 else { problem = "Parça sınırına ulaşıldı."; return }
        guard document.pieces.reduce(0, { $0 + $1.points.count }) + piece.points.count <= 20_000 else {
            problem = "Çizim sınırı doldu. Yeni şekil eklenemedi."
            return
        }
        var updated = document
        updated.pieces.append(piece)
        guard commit(updated, notice: notice) else { return }
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
        if trimmed.isEmpty { return "\(title): boş bırakılamaz. Milimetre cinsinden bir sayı yaz." }
        guard let value = DraftContourGeometry.coordinate(text) else {
            let separators = trimmed.filter { $0 == "," || $0 == "." }.count
            if separators > 1 {
                return "\(title): birden çok virgül ya da nokta var. Ondalık için tek virgül kullan."
            }
            if let number = Double(trimmed.replacingOccurrences(of: ",", with: ".")), number.isFinite {
                return "\(title): değer çalışma alanı sınırını aşıyor (en çok 100 000 000 mm)."
            }
            return "\(title): sayı değil. Yalnız rakam, eksi işareti ve ondalık için virgül yaz; birim yazma."
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
            return "Birden çok nokta ya da virgül var. Ondalık için tek virgül kullan."
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
        guard commit(updated, notice: "Parça silindi. Geri al ile geri getirebilirsin.") else { return }
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

    /// Writes the drawing as a .tcad file in a fresh temporary folder, or says why it cannot.
    private func writeExportFile() -> URL? {
        guard !document.pieces.isEmpty else { problem = "Önce en az bir parça ekle."; return nil }
        do {
            let bytes = try CADSketchImport.encode(document)
            // Named after the project, so the file is recognisable in Files and Mail.
            let folder = FileManager.default.temporaryDirectory
                .appendingPathComponent(UUID().uuidString, isDirectory: true)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let url = folder.appendingPathComponent(CADSketchImport.exportFileName(forProjectNamed: document.name))
            try bytes.write(to: url, options: .atomic)
            return url
        } catch {
            problem = "Dosya hazırlanamadı: \(error.localizedDescription)"
            return nil
        }
    }

    /// One step on iOS: typed values are applied (or the wrong box is named), the file is written
    /// from the current drawing and the system share sheet opens. Nothing is prepared in advance,
    /// so an edit can never leave an outdated file or a vanished button behind.
    private func shareDrawing() {
        guard saveDetails() else { return }
        guard let url = writeExportFile() else { return }
        #if os(iOS)
        shareFile = ShareFile(url: url)
        #else
        exportURL = url
        success = "Dosya hazır. Aşağıdaki «Paylaş veya Dosyalara kaydet» düğmesine dokun."
        #endif
    }

    /// Why sharing cannot start yet, or what will happen first. Shown under the share button.
    private var shareNote: String? {
        if document.pieces.isEmpty { return "Paylaşmak için önce en az bir parça ekle." }
        if hasPendingDetails { return "Kutulara yazdığın değerler paylaşmadan önce uygulanır." }
        return nil
    }

    // MARK: Closing

    /// Applies typed values and closes. A value that cannot be applied no longer traps the
    /// person here: they choose between fixing it and leaving without it.
    private func requestClose() {
        if saveDetails() { dismiss(); return }
        // The reason goes into the choice itself instead of an alert that only says "OK".
        discardReason = problem ?? "Yazılan bir değer uygulanamıyor."
        problem = nil
    }

    /// Drops only the text typed into the boxes; the drawing and its saved copies stay as they are.
    private func discardPendingDetails() {
        projectName = document.name
        select(document.pieces.first(where: { $0.id == selectedID }))
    }

    // MARK: Guidance
    // One sentence that says what to do next, chosen from the state of the drawing.
    @ViewBuilder
    private var coachTip: some View {
        if document.pieces.isEmpty {
            CoachTip(systemImage: "1.circle.fill", title: "Başlamak için bir şekil ekle",
                     text: "Dikdörtgen, Çizgi, Daire ya da Üçgen ekle düğmelerinden birine dokun. Ölçüleri sonra kendin yazarsın.")
        } else if selectedIndex == nil {
            CoachTip(systemImage: "hand.tap.fill", title: "Düzenlemek için parçaya dokun",
                     text: "Çizimdeki bir parçaya dokun ya da aşağıdaki Parça seç menüsünü kullan.")
        } else {
            CoachTip(systemImage: "ruler", title: "Ölçüleri milimetre olarak yaz",
                     text: "Kutulara yaz ve uygula ya da parçayı parmağınla sürükle. Bitince Projeyi kaydet'e dokun.")
        }
    }

    // MARK: Layout
    // The drawing stays on screen while the tools below it scroll, so a dimension can be
    // typed while looking at the piece it changes.

    /// iPad, landscape and other wide areas: the drawing on the left, the tools on the right.
    /// A portrait iPhone keeps the drawing above the tools.
    private var isWide: Bool {
        #if os(iOS)
        // Size classes do not change while the keyboard is up, so typing never switches layout.
        if horizontalSizeClass == .regular || verticalSizeClass == .compact { return true }
        #endif
        return wideByShape
    }

    /// The keyboard only shrinks the drawing when it sits above the tools; beside them (iPad,
    /// landscape) the drawing keeps its height, so zoom, controls and hints stay.
    private var keyboardCramps: Bool { keyboardVisible && !isWide }

    /// Share of the width the drawing gets beside the tools.
    private static let wideCanvasShare: CGFloat = 0.6

    private func measureWorkspace(_ size: CGSize) {
        workspaceSize = size
        // The keyboard shortens the area; a portrait phone must not switch layout while typing.
        if !keyboardVisible { wideByShape = size.width > size.height }
    }

    private var canvasHeight: CGFloat {
        // Beside the tools the drawing uses the full height, less the two status lines under it.
        if isWide { return max(160, workspaceSize.height - 110) }
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

    /// Drawing above the tools, or beside them on a wide area. AnyLayout keeps the drawing's and
    /// the tools' state when the device turns.
    private var workshopLayout: AnyLayout {
        isWide ? AnyLayout(HStackLayout(alignment: .top, spacing: 0))
               : AnyLayout(VStackLayout(spacing: 0))
    }

    private var canvasPanel: some View {
        VStack(alignment: .leading, spacing: 4) {
            preview
            // This panel does not scroll, so its text is limited: it must not push the tools away.
            if !document.pieces.isEmpty {
                Text(viewport.isFit
                     ? "\(document.pieces.count) parça • Tümü ekranda"
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

    private func addCircleButton(fill: Bool) -> some View {
        Button { addCircle() } label: {
            Label("Daire ekle", systemImage: "circle")
                .frame(maxWidth: fill ? .infinity : nil, minHeight: 48)
        }
        .buttonStyle(.appSecondary)
        .accessibilityHint("Çapı 100 milimetre olan yuvarlak bir parça ekler")
    }

    private func addTriangleButton(fill: Bool) -> some View {
        Button { addTriangle() } label: {
            Label("Üçgen ekle", systemImage: "triangle")
                .frame(maxWidth: fill ? .infinity : nil, minHeight: 48)
        }
        .buttonStyle(.appSecondary)
        .accessibilityHint("Eni ve boyu 100 milimetre olan üçgen bir parça ekler")
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
            // The round and pointed shapes share a second row on the same terms.
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 10) {
                    addCircleButton(fill: false).fixedSize(horizontal: true, vertical: false)
                    addTriangleButton(fill: false).fixedSize(horizontal: true, vertical: false)
                }
                VStack(spacing: 10) {
                    addCircleButton(fill: true)
                    addTriangleButton(fill: true)
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

    private var moveLeftButton: some View {
        Button { move(dx: -10, dy: 0) } label: {
            Text("← 10 mm").frame(maxWidth: .infinity, minHeight: 44)
        }
        .accessibilityHint("Seçili parçayı 10 milimetre sola taşır")
    }

    private var moveRightButton: some View {
        Button { move(dx: 10, dy: 0) } label: {
            Text("10 mm →").frame(maxWidth: .infinity, minHeight: 44)
        }
        .accessibilityHint("Seçili parçayı 10 milimetre sağa taşır")
    }

    // CAD Y grows upwards, so "up" adds to Y.
    private var moveUpButton: some View {
        Button { move(dx: 0, dy: 10) } label: {
            Text("↑ 10 mm").frame(maxWidth: .infinity, minHeight: 44)
        }
        .accessibilityHint("Seçili parçayı 10 milimetre yukarı taşır")
    }

    private var moveDownButton: some View {
        Button { move(dx: 0, dy: -10) } label: {
            Text("↓ 10 mm").frame(maxWidth: .infinity, minHeight: 44)
        }
        .accessibilityHint("Seçili parçayı 10 milimetre aşağı taşır")
    }

    private var rotateButton: some View {
        Button { rotateSelected() } label: {
            Text("90° döndür").frame(maxWidth: .infinity, minHeight: 44)
        }
    }

    private var copyButton: some View {
        Button { duplicateSelected() } label: {
            Text("Kopyala").frame(maxWidth: .infinity, minHeight: 44)
        }
    }

    private var deleteButton: some View {
        Button(role: .destructive) { deleteSelected() } label: {
            Text("Sil").frame(maxWidth: .infinity, minHeight: 44)
        }
    }

    // Pairs of equal-width buttons so the tools read as one grid.
    // Large text falls back to one full-width button per line.
    private var pieceToolGrid: some View {
        ViewThatFits(in: .horizontal) {
            VStack(spacing: 10) {
                HStack(spacing: 10) { moveLeftButton; moveRightButton }
                HStack(spacing: 10) { moveUpButton; moveDownButton }
                HStack(spacing: 10) { rotateButton; copyButton }
                deleteButton
            }
            VStack(spacing: 10) {
                moveLeftButton
                moveRightButton
                moveUpButton
                moveDownButton
                rotateButton
                copyButton
                deleteButton
            }
        }
        .buttonStyle(.appSecondary)
        .font(.caption)
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
        Button { _ = saveDetails() } label: {
            Text("Ölçüleri, konumu ve adı uygula")
                .multilineTextAlignment(.center)
                .frame(maxWidth: .infinity, minHeight: 48)
        }
        .buttonStyle(.appPrimary)
        if document.pieces[index].points.count <= DraftContourGeometry.pointLimit {
            Button {
                if !hasPendingDetails || saveDetails() { editingPoints = document.pieces[index] }
            } label: {
                Label("Köşe noktalarını düzenle", systemImage: "point.topleft.down.curvedto.point.bottomright.up")
                    .frame(maxWidth: .infinity, minHeight: 44)
            }
            .buttonStyle(.appSecondary)
        } else {
            Text("Nokta düzenleme en fazla 250 noktalı şekillerde kullanılabilir.")
                .font(.caption)
        }
        pieceToolGrid
    }

    private var editSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label("2 · Düzenle", systemImage: "slider.horizontal.3").font(.headline)
                .accessibilityAddTraits(.isHeader)
            Toggle("5 mm'lik adımlarla sürükle", isOn: $snapToFiveMillimeters)
                .font(.caption)
            Text("Bir parçaya dokunarak seç. Parmağınla sürükleyerek yerini değiştir; yakınlaştırılmışken parçayı kenar çizgisinden tut.")
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
            Text("Çizimin bu cihazda kendiliğinden saklanır. Ana ekrandaki listeye eklemek için Projeyi kaydet'e bas.")
                .font(.caption).foregroundStyle(AppTheme.muted)
            Button {
                guard saveDetails() else { return }
                do {
                    try CADProjectLibrary.save(document, replacing: savedLibrarySnapshot)
                    savedLibrarySnapshot = document
                    success = "Proje kaydedildi. Ana ekrandaki Son projeler bölümünden açabilirsin."
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
                    success = "Kopya kaydedildi. Son projeler bölümünden açabilirsin."
                } catch { problem = "Kopya kaydedilemedi. " + error.localizedDescription }
            } label: {
                Label("Kopyasını kaydet", systemImage: "doc.on.doc")
                    .frame(maxWidth: .infinity, minHeight: 48)
            }
            .buttonStyle(.appSecondary)
            // One prominent action per section: saving the project. Sharing is secondary.
            #if os(iOS)
            // One tap: the file is written from the current drawing and the share sheet opens.
            // The button never disappears; when sharing cannot start, the note below says why.
            Button { shareDrawing() } label: {
                Label("Paylaş veya Dosyalara kaydet", systemImage: "square.and.arrow.up")
                    .frame(maxWidth: .infinity, minHeight: 48)
            }
            .buttonStyle(.appSecondary)
            .disabled(document.pieces.isEmpty)
            .accessibilityHint("Çizimi .tcad dosyası olarak hazırlar ve paylaşma seçeneklerini açar")
            .accessibilityIdentifier("share-drawing")
            #else
            Button { shareDrawing() } label: {
                Label("Dosyayı hazırla (.tcad)", systemImage: "doc.badge.plus")
                    .frame(maxWidth: .infinity, minHeight: 48)
            }
            .buttonStyle(.appSecondary)
            .disabled(document.pieces.isEmpty)
            if let exportURL, !hasPendingDetails {
                ShareLink(item: exportURL, preview: SharePreview("Mono Di Tree CAD çizimi")) {
                    Label("Paylaş veya Dosyalara kaydet", systemImage: "square.and.arrow.up")
                        .frame(maxWidth: .infinity, minHeight: 48)
                }
                .buttonStyle(.appPrimary)
            }
            #endif
            if let shareNote {
                Text(shareNote)
                    .font(.caption).foregroundStyle(AppTheme.muted)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Text("Paylaşılan dosyada yalnız bu çizim olur. Açtığın başka dosyalar değişmez.")
                .font(.caption).foregroundStyle(AppTheme.muted)
        }
    }

    var body: some View {
        NavigationStack {
            workshopLayout {
                if showsCanvas {
                    canvasPanel
                        .frame(width: isWide && workspaceSize.width > 0
                               ? workspaceSize.width * Self.wideCanvasShare : nil)
                    Divider()
                }
                ScrollView {
                    VStack(alignment: .leading, spacing: 16) {
                        coachTip.appCard()
                        addSection.appCard()
                        if !document.pieces.isEmpty { editSection.appCard() }
                        saveSection.appCard()
                        Text("Ölçüler milimetredir. Bu çizim üretim ya da kesim onayı değildir.")
                            .font(.caption).foregroundStyle(AppTheme.muted)
                    }
                    .padding()
                    .controlSize(.large)
                }
                .scrollDismissesKeyboard(.interactively)
                .accessibilityIdentifier("workshop-tools")
            }
            .background(GeometryReader { proxy in
                Color.clear
                    .onAppear { measureWorkspace(proxy.size) }
                    .onChange(of: proxy.size) { measureWorkspace($0) }
            })
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
                if keyboardCramps { viewport = DraftViewport() }
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
            #if os(iOS)
            .toolbarBackground(AppTheme.page, for: .navigationBar)
            .toolbarBackground(.visible, for: .navigationBar)
            #endif
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
            #if os(iOS)
            .sheet(item: $shareFile) { file in
                ActivityShareSheet(url: file.url) { completed in
                    shareFile = nil
                    // The share target has its own copy by now; drop the per-share temporary folder.
                    try? FileManager.default.removeItem(at: file.url.deletingLastPathComponent())
                    if completed { success = "Dosya paylaşıldı." }
                }
                .presentationDetents([.medium, .large])
                .ignoresSafeArea()
            }
            #endif
            .confirmationDialog("Yazdığın değer uygulanamıyor", isPresented: Binding(
                get: { discardReason != nil },
                set: { if !$0 { discardReason = nil } }
            ), titleVisibility: .visible) {
                Button("Değişikliği at ve kapat", role: .destructive) {
                    discardPendingDetails()
                    discardReason = nil
                    dismiss()
                }
                Button("Düzeltmeye dön", role: .cancel) { discardReason = nil }
            } message: {
                Text((discardReason ?? "") +
                     " Atarsan yalnız kutulara yazdığın değerler silinir; çizimin ve kaydettiğin proje değişmez.")
            }
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Kapat") { requestClose() }
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
        .appAppearance()
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
        // The margin leaves room for the measurement tags around the selected piece; the zoom
        // buttons' corner is kept free so they never sit on top of the drawing at fit.
        let corner = keyboardCramps ? CGSize.zero : zoomControlsFitFootprint
        return DraftCanvasMap(size: size, minX: minX, maxX: maxX, minY: minY, maxY: maxY, inset: 30,
                              keepingClear: corner)
    }

    private func transform(_ size: CGSize) -> DraftCanvasMap? {
        guard let fit = fitMap(size) else { return nil }
        let live = liveZoomed(viewport, in: size, content: fit.contentSize)
            .panned(by: livePan, in: size, content: fit.contentSize)
        return fit.viewed(live, in: size)
    }

    /// `base` with the pinch in progress applied: about where the fingers started when that is
    /// known, otherwise about the centre.
    private func liveZoomed(_ base: DraftViewport, in size: CGSize, content: CGSize) -> DraftViewport {
        guard let anchor = pinchAnchor else { return base.zoomed(by: liveZoom) }
        return base.zoomed(by: liveZoom, keeping: anchor, in: size, content: content)
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

    /// Screen distance from `point` to the nearest edge of `piece`.
    private func outlineDistance(_ point: CGPoint, of piece: DraftPiece, on map: DraftCanvasMap) -> Double {
        let vertices = piece.points.map(map.screen)
        guard vertices.count >= 2 else {
            return vertices.first.map { hypot(Double($0.x - point.x), Double($0.y - point.y)) } ?? .infinity
        }
        let count = piece.closed ? vertices.count : vertices.count - 1
        var best = Double.infinity
        for index in 0..<count {
            best = min(best, segmentDistance(point, vertices[index], vertices[(index + 1) % vertices.count]))
        }
        return best
    }

    /// Decides what a one-finger drag does. At fit it picks up the piece under the finger, as
    /// before. Zoomed in, only the outline of the selected piece picks it up; a drag anywhere else
    /// (empty paper, another piece, the inside of the selected piece) moves the view, so a piece
    /// that fills the screen can still be panned past.
    private func beginCanvasDrag(at start: CGPoint, size: CGSize) {
        panning = false
        if viewport.isFit {
            let piece = hitPiece(start, size: size)
            if let piece, selectFromCanvasOrMenu(piece.id),
               !hasPendingDetails || saveDetails() { draggedPieceID = piece.id }
            return
        }
        if let index = selectedIndex, let map = transform(size),
           outlineDistance(start, of: document.pieces[index], on: map) <= 24 {
            let id = document.pieces[index].id
            if !hasPendingDetails || saveDetails() { draggedPieceID = id }
        } else {
            panning = true
        }
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
    private func drawLabel(_ text: String, center: CGPoint, in context: inout GraphicsContext, size: CGSize,
                           color: Color = AppTheme.accent) {
        let resolved = context.resolve(Text(text).font(.caption2.weight(.semibold)).foregroundColor(color))
        let box = resolved.measure(in: CGSize(width: 160, height: 40))
        let w = box.width + 10, h = box.height + 4
        guard size.width > w + 4, size.height > h + 4 else { return }
        let cx = min(max(center.x, w / 2 + 2), size.width - w / 2 - 2)
        let cy = min(max(center.y, h / 2 + 2), size.height - h / 2 - 2)
        let tag = Path(roundedRect: CGRect(x: cx - w / 2, y: cy - h / 2, width: w, height: h), cornerRadius: 6)
        context.fill(tag, with: .color(AppTheme.card.opacity(0.92)))
        context.stroke(tag, with: .color(color.opacity(0.35)), lineWidth: 1)
        context.draw(resolved, at: CGPoint(x: cx, y: cy), anchor: .center)
    }

    /// The grid that matches the current zoom, or nil when there is nothing to measure against.
    private func grid(for mapper: DraftCanvasMap) -> DraftGrid? {
        DraftGrid(pointsPerMillimetre: mapper.pointsPerMillimetre)
    }

    /// Faint millimetre squares and the two axes through (0, 0), drawn under the pieces so the
    /// X and Y boxes have something visible to refer to.
    private func drawGrid(_ grid: DraftGrid, mapper: DraftCanvasMap,
                          in context: inout GraphicsContext, size: CGSize) {
        let a = mapper.model(at: .zero)
        let b = mapper.model(at: CGPoint(x: size.width, y: size.height))
        let x0 = min(a.x, b.x), x1 = max(a.x, b.x), y0 = min(a.y, b.y), y1 = max(a.y, b.y)
        guard [x0, x1, y0, y1].allSatisfy({ $0.isFinite }) else { return }
        let first = (x: (x0 / grid.step).rounded(.down), y: (y0 / grid.step).rounded(.down))
        let last = (x: (x1 / grid.step).rounded(.up), y: (y1 / grid.step).rounded(.up))
        // The step keeps squares at least 12 pt wide, so this only trips on a broken map.
        guard last.x - first.x <= 400, last.y - first.y <= 400 else { return }
        var minor = Path(), major = Path()
        var i = first.x
        while i <= last.x {
            let x = mapper.screen(DraftPoint(x: i * grid.step, y: 0)).x
            if grid.isMajor(i) {
                major.move(to: CGPoint(x: x, y: 0)); major.addLine(to: CGPoint(x: x, y: size.height))
            } else {
                minor.move(to: CGPoint(x: x, y: 0)); minor.addLine(to: CGPoint(x: x, y: size.height))
            }
            i += 1
        }
        var j = first.y
        while j <= last.y {
            let y = mapper.screen(DraftPoint(x: 0, y: j * grid.step)).y
            if grid.isMajor(j) {
                major.move(to: CGPoint(x: 0, y: y)); major.addLine(to: CGPoint(x: size.width, y: y))
            } else {
                minor.move(to: CGPoint(x: 0, y: y)); minor.addLine(to: CGPoint(x: size.width, y: y))
            }
            j += 1
        }
        context.stroke(minor, with: .color(AppTheme.line.opacity(0.7)), lineWidth: 0.5)
        context.stroke(major, with: .color(AppTheme.line), lineWidth: 1)
        let origin = mapper.screen(DraftPoint(x: 0, y: 0))
        var axes = Path()
        if origin.x >= 0 && origin.x <= size.width {
            axes.move(to: CGPoint(x: origin.x, y: 0)); axes.addLine(to: CGPoint(x: origin.x, y: size.height))
        }
        if origin.y >= 0 && origin.y <= size.height {
            axes.move(to: CGPoint(x: 0, y: origin.y)); axes.addLine(to: CGPoint(x: size.width, y: origin.y))
        }
        context.stroke(axes, with: .color(AppTheme.muted.opacity(0.35)), lineWidth: 1)
    }

    /// Whether the point (0, 0) is inside the drawing area.
    private func originVisible(on mapper: DraftCanvasMap, size: CGSize) -> Bool {
        let origin = mapper.screen(DraftPoint(x: 0, y: 0))
        return origin.x >= 0 && origin.x <= size.width && origin.y >= 0 && origin.y <= size.height
    }

    /// A dot at (0, 0) with short arrows towards growing X (right) and growing Y (up). The labels
    /// sit outside the arrows, so a piece that starts at (0, 0) does not hide them.
    private func drawOrigin(mapper: DraftCanvasMap, in context: inout GraphicsContext, size: CGSize) {
        guard originVisible(on: mapper, size: size) else { return }
        let o = mapper.screen(DraftPoint(x: 0, y: 0))
        let arm: CGFloat = 22, head: CGFloat = 4
        var arrows = Path()
        arrows.move(to: o); arrows.addLine(to: CGPoint(x: o.x + arm, y: o.y))
        arrows.move(to: CGPoint(x: o.x + arm - head, y: o.y - head))
        arrows.addLine(to: CGPoint(x: o.x + arm, y: o.y))
        arrows.addLine(to: CGPoint(x: o.x + arm - head, y: o.y + head))
        arrows.move(to: o); arrows.addLine(to: CGPoint(x: o.x, y: o.y - arm))
        arrows.move(to: CGPoint(x: o.x - head, y: o.y - arm + head))
        arrows.addLine(to: CGPoint(x: o.x, y: o.y - arm))
        arrows.addLine(to: CGPoint(x: o.x + head, y: o.y - arm + head))
        let ink = AppTheme.muted
        context.stroke(arrows, with: .color(ink.opacity(0.8)),
                       style: StrokeStyle(lineWidth: 1.2, lineCap: .round, lineJoin: .round))
        context.fill(Path(ellipseIn: CGRect(x: o.x - 3, y: o.y - 3, width: 6, height: 6)), with: .color(ink))
        // Large text would turn these letters into blocks over the small drawing; the spoken value
        // says the same.
        guard !dynamicTypeSize.isAccessibilitySize else { return }
        let font = Font.caption2.weight(.semibold)
        context.draw(Text("X").font(font).foregroundColor(ink), at: CGPoint(x: o.x + arm, y: o.y + 10), anchor: .center)
        context.draw(Text("Y").font(font).foregroundColor(ink), at: CGPoint(x: o.x - 10, y: o.y - arm), anchor: .center)
        context.draw(Text("0").font(font).foregroundColor(ink), at: CGPoint(x: o.x - 8, y: o.y + 9), anchor: .center)
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
                // Reference under everything else: squares, axes and the origin.
                let squares = grid(for: mapper)
                if let squares { drawGrid(squares, mapper: mapper, in: &context, size: size) }
                drawOrigin(mapper: mapper, in: &context, size: size)
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
                // At accessibility text sizes the tags grow until they cover the small drawing; the
                // same numbers stay in the fields below and in the spoken value.
                if !dynamicTypeSize.isAccessibilitySize {
                    if let squares {
                        drawLabel("Kare: \(DraftPiece.display(squares.step)) mm", center: .zero,
                                  in: &context, size: size, color: AppTheme.muted)
                    }
                    if let selected = document.pieces.first(where: { $0.id == selectedID }) {
                        drawMeasurements(of: selected, mapper: mapper, in: &context, size: size)
                    }
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
                        beginCanvasDrag(at: value.startLocation, size: geometry.size)
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
                        // Panning once shows the rule was understood.
                        if showPanHint { panHintSeen = true; showPanHint = false }
                    }
                    finishDrag(value.translation, size: geometry.size)
                })
            .modifier(PinchZoom(
                changed: { scale, start in
                    pinching = true
                    // A one-finger pan that turns into a pinch is dropped, so its translation is
                    // not added again when the drag ends.
                    if panning { panning = false; livePan = .zero }
                    // A piece that is being dragged keeps its scale until it is dropped.
                    guard draggedPieceID == nil else { return }
                    if pinchAnchor == nil, let start, start.x.isFinite, start.y.isFinite {
                        pinchAnchor = CGPoint(x: min(max(start.x, 0), geometry.size.width),
                                              y: min(max(start.y, 0), geometry.size.height))
                    }
                    liveZoom = scale
                },
                ended: {
                    // Commit what was shown; liveZoom is 1 when a piece was being dragged.
                    viewport = liveZoomed(viewport, in: geometry.size,
                                          content: fitMap(geometry.size)?.contentSize ?? .zero)
                    liveZoom = 1
                    pinchAnchor = nil
                    pinching = false
                }))
            .onAppear { canvasSize = geometry.size }
            .onChange(of: geometry.size) { canvasSize = $0 }
        }
        .clipped()
        // Beside the tools the drawing takes whatever height is left; a height computed from the
        // measured area could grow with the text under it and feed back into the measurement.
        .frame(minHeight: isWide ? 160 : canvasHeight, maxHeight: isWide ? .infinity : canvasHeight)
        .frame(maxWidth: .infinity)
        .background(AppTheme.card, in: RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(AppTheme.line, lineWidth: 1))
        .accessibilityLabel("Dokunarak parça seçebileceğin kalıp çizimi")
        .accessibilityHint("Bir parçaya dokunarak seç, sürükleyerek taşı. Yakınlaştırılmışken sürüklemek görünümü kaydırır; seçili parça kenar çizgisinden tutulunca taşınır. Yakınlaştırmak için yukarı, uzaklaştırmak için aşağı kaydır. Alternatif: Parça seç menüsü.")
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
                    Text("Bir şekil ekleyerek başla. Ölçüler milimetredir.")
                        .font(.subheadline).foregroundStyle(AppTheme.muted)
                        .multilineTextAlignment(.center)
                }
                .padding(8)
                .allowsHitTesting(false)
            }
        }
        .overlay(alignment: .bottomTrailing) {
            if !document.pieces.isEmpty && !keyboardCramps { zoomControls }
        }
        .overlay(alignment: .top) {
            if showPanHint && !viewport.isFit && !keyboardCramps { panHint }
        }
        .onChange(of: viewport.isFit) { isFit in
            if !isFit && !panHintSeen { showPanHint = true }
        }
    }

    /// Shown once, the first time the drawing is zoomed: dragging now moves the view.
    private var panHint: some View {
        HStack(alignment: .center, spacing: 8) {
            Image(systemName: "hand.draw")
                .foregroundStyle(AppTheme.accent)
                .accessibilityHidden(true)
            Text("Yakınlaştırınca parmakla sürüklemek görünümü kaydırır. Seçili parçayı taşımak için kenar çizgisinden tutup sürükle.")
                .font(.caption)
                .foregroundStyle(AppTheme.ink)
                .lineLimit(dynamicTypeSize.isAccessibilitySize ? 3 : 4)
                .fixedSize(horizontal: false, vertical: true)
            Button("Anladım") {
                panHintSeen = true
                showPanHint = false
            }
            .font(.caption.weight(.semibold))
            .frame(minWidth: 44, minHeight: 44)
            .accessibilityIdentifier("canvas-pan-hint-ok")
        }
        .padding(.horizontal, 10)
        .background(AppTheme.card.opacity(0.95), in: RoundedRectangle(cornerRadius: 10))
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(AppTheme.line, lineWidth: 1))
        .padding(6)
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("canvas-pan-hint")
    }

    /// Pinch to zoom. iOS 17 and macOS 14 report where the fingers started, so the drawing grows
    /// around that point; iOS 16 gives no location and the zoom stays about the centre.
    private struct PinchZoom: ViewModifier {
        let changed: (Double, CGPoint?) -> Void
        let ended: () -> Void

        @ViewBuilder
        func body(content: Content) -> some View {
            if #available(iOS 17.0, macOS 14.0, *) {
                content.simultaneousGesture(MagnifyGesture()
                    .onChanged { value in changed(Double(value.magnification), value.startLocation) }
                    .onEnded { _ in ended() })
            } else {
                content.simultaneousGesture(MagnificationGesture()
                    .onChanged { value in changed(Double(value), nil) }
                    .onEnded { _ in ended() })
            }
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
        if !viewport.isFit && viewport.pan != .zero { text += " Görünüm kaydırılmış." }
        // The same reference the drawing shows: the grid size and where X and Y are measured from.
        if let mapper = transform(canvasSize) {
            if let squares = grid(for: mapper) {
                text += " Arkadaki her kare \(DraftPiece.display(squares.step)) milimetre."
            }
            text += originVisible(on: mapper, size: canvasSize)
                ? " Başlangıç noktası (0, 0) ekranda; X sağa, Y yukarı doğru artar."
                : " Başlangıç noktası (0, 0) şu an ekranda değil; X sağa, Y yukarı doğru artar."
        }
        return text
    }

    // Buttons for the same zoom the pinch gesture gives, so nothing depends on two fingers.
    private var zoomControls: some View {
        HStack(spacing: 6) {
            if !viewport.isFit {
                zoomButton("Ekrana sığdır", systemImage: "arrow.up.left.and.down.right.magnifyingglass",
                           id: "zoom-fit", enabled: true) {
                    viewport = DraftViewport()
                    // This button disappears at fit; VoiceOver would otherwise lose its place.
                    DispatchQueue.main.async { zoomInFocused = true }
                }
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
            .accessibilityFocused($zoomInFocused)
        }
        .padding(6)
        // Cmd + needs Shift on most keyboards; Cmd = is the same key without it. A button takes
        // one shortcut, so this invisible twin carries the second one.
        .background {
            Button {
                viewport = viewport.zoomed(by: DraftViewport.step)
            } label: {
                Color.clear.frame(width: 1, height: 1)
            }
            .keyboardShortcut("=", modifiers: .command)
            .disabled(!viewport.canZoomIn)
            .opacity(0)
            .allowsHitTesting(false)
            .accessibilityHidden(true)
        }
    }

    /// The bottom-right corner the zoom buttons cover at fit (zoom out and zoom in), kept free of
    /// the drawing by `fitMap`.
    private var zoomControlsFitFootprint: CGSize {
        CGSize(width: 2 * zoomButtonSide + 6 + 12, height: zoomButtonSide + 12)
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

#if os(iOS)
/// A written .tcad file waiting for the share sheet.
private struct ShareFile: Identifiable {
    let id = UUID()
    let url: URL
}

/// The system share sheet ("Paylaş", "Dosyalara Kaydet", Mail, AirDrop) for one file. SwiftUI's
/// ShareLink needs its file before the tap; this one is handed a file written at the tap.
private struct ActivityShareSheet: UIViewControllerRepresentable {
    let url: URL
    let finished: (Bool) -> Void

    func makeUIViewController(context: Context) -> UIActivityViewController {
        let controller = UIActivityViewController(activityItems: [url], applicationActivities: nil)
        controller.completionWithItemsHandler = { _, completed, _, _ in finished(completed) }
        return controller
    }

    func updateUIViewController(_ controller: UIActivityViewController, context: Context) {}
}
#endif

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
