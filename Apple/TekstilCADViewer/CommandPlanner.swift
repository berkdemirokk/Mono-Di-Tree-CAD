import Foundation

// Turns a parsed command into a plan for the current drawing: one sentence that says what will
// happen, and the document that results. It changes nothing itself. The workshop applies a plan
// only after the user confirms, through the same `commit` path as every button, so a command is
// one undo step, saved to the draft the same way, and refused when the drawing changed meanwhile.
//
// iPhone facts that differ from the Windows program (the shared vectors mark them):
//  - Y grows upwards and a piece's position is its lower-left corner.
//  - New pieces go where the buttons put them: (20·n, 20·n) with n = pieces already there.
//  - The "90° döndür" button turns counter-clockwise; clockwise turns are three of those.
//  - Resizing exists (scales a piece from its lower-left corner), mirroring does not.

/// The state a command is planned against.
struct CommandContext {
    /// What is committed (and on disk).
    var document: DraftProject
    /// The document with the text fields' pending values applied, or nil when those are invalid.
    var base: DraftProject?
    var baseProblem: String?
    var selectedID: UUID?
    var undoCount: Int
    var redoCount: Int
}

struct CommandPlan {
    enum SelectionAfter: Equatable {
        case keep
        case piece(UUID?)
    }

    enum Effect: Equatable {
        case edit(DraftProject, SelectionAfter)
        case select(UUID?)
        case undo(Int)
        case redo
        case saveProject
        case saveCopy
        case info
    }

    /// The one line shown before anything happens.
    var sentence: String
    /// A second, smaller line: a platform difference or an applied pending value.
    var note: String?
    var effect: Effect
    var isDestructive: Bool
    /// The result line after it was done.
    var notice: String
    /// What the plan was made against; a changed drawing makes the plan stale.
    var base: DraftProject
    var baseSelection: UUID?
    /// Lines for the help card.
    var lines: [String] = []
}

enum PlanResult {
    case plan(CommandPlan)
    case refusal(CommandRefusal)
}

enum CommandPlanner {
    static let helpLines = [
        "120 x 80 dikdörtgen · kare 100 · 150 çizgi · 80 dikey çizgi",
        "25 sağa taşı · 20 sağa 10 yukarı · x 100 y 50 konumuna taşı",
        "90 sağa döndür · sola döndür · 180 derece",
        "genişliği 95 yap · 95 x 60 yap · adı kol olsun",
        "kopyala · sil · ön bedeni seç · geri al · yinele · kaydet",
        "Ölçüler milimetredir; santim için 12 cm yazın."
    ]

    private static func n(_ value: Double) -> String { CommandFormat.number(value) }
    private static func quoted(_ name: String) -> String { "‘\(name)’" }

    // MARK: Entry

    static func plan(_ command: ParsedCommand, context: CommandContext) -> PlanResult {
        // Things the iPhone does elsewhere or not at all are refused first, in plain words.
        if let refusal = unsupported(command) { return .refusal(refusal) }

        switch command {
        case .help:
            return .plan(CommandPlan(
                sentence: "Örnek komutlar", note: nil, effect: .info, isDestructive: false,
                notice: "", base: context.document, baseSelection: context.selectedID, lines: helpLines))
        case .cancelPreview:
            return .refusal(CommandRefusal(code: "R01", message: "Bir komut yazın. Örnek: 120 x 80 dikdörtgen"))
        case .undo(let steps):
            return planUndo(steps, context)
        case .redo:
            return planRedo(context)
        case .delete(let target, let name):
            return planDelete(name: target == .named ? name : nil, index: nil, context)
        case .deleteAt(let index, let name):
            return planDelete(name: name, index: index, context)
        default:
            break
        }

        guard let base = context.base else { return .refusal(pendingProblem(context)) }
        var note: String?
        if base != context.document {
            note = "Alanlara yazdığınız ama uygulamadığınız değerler de bu işlemle uygulanır."
        }
        switch command {
        case .rectangle(let width, let height, let isSquare):
            return planRectangle(width, height, isSquare: isSquare, base: base, context: context, note: note)
        case .line(let length, let orientation):
            return planLine(length, vertical: orientation == .vertical, base: base, context: context, note: note)
        case .select(let target, let name):
            switch target {
            case .named:
                return planSelect(name: name ?? "", index: nil, base: base, context: context, note: note)
            case .next, .previous:
                return planAdjacent(next: target == .next, base: base, context: context, note: note)
            case .none:
                guard context.selectedID != nil else {
                    return .refusal(CommandRefusal(code: "R25", message: "Seçili parça yok. Değişiklik yok."))
                }
                return .plan(make("Seçim bırakılacak. Parçalar değişmez.", note, .select(nil), "Seçim bırakıldı.", base, context))
            case .selected:
                return .refusal(CommandRefusal(code: "R25", message: "Parça zaten seçili. Değişiklik yok."))
            }
        case .selectAt(let index, let name):
            return planSelect(name: name, index: index, base: base, context: context, note: note)
        case .file(let action):
            if action == .save {
                let sentence = "Proje kaydedilecek: \(quoted(base.name)). Ana ekrandaki Son projeler listesine eklenir."
                return .plan(make(sentence, note, .saveProject, "", base, context))
            }
            let copyName = String(base.name.prefix(120)) + " (kopya)"
            let sentence = "Projenin bağımsız bir kopyası \(quoted(copyName)) adıyla kaydedilecek. Açık çizim değişmez."
            return .plan(make(sentence, note, .saveCopy, "", base, context))
        default:
            break
        }

        // The rest work on the selected piece.
        guard let index = base.pieces.firstIndex(where: { $0.id == context.selectedID }) else {
            return .refusal(nothingSelected(base))
        }
        let piece = base.pieces[index]
        switch command {
        case .moveBy(let right, let down):
            return planMove(right: right, up: -down, index: index, piece: piece, base: base, context: context, note: note)
        case .moveTo(let x, let y):
            return planMoveTo(x: x, y: y, index: index, piece: piece, base: base, context: context, note: note)
        case .rotate(let degrees, let direction):
            return planRotate(degrees, clockwise: direction == .clockwise, index: index, piece: piece,
                              base: base, context: context, note: note)
        case .resize(let width, let height):
            return planResize(width: width, height: height, index: index, piece: piece, base: base, context: context, note: note)
        case .rename(let name):
            return planRename(name, index: index, piece: piece, base: base, context: context, note: note)
        case .handoff:
            // The only hand-off the iPhone does itself is copying the piece (see `unsupported`).
            return planDuplicate(index: index, piece: piece, base: base, context: context, note: note)
        default:
            return .refusal(CommandRefusal(code: "R01", message: "Bu komut anlaşılmadı. Örnek: 25 sağa taşı"))
        }
    }

    // MARK: Not on the iPhone

    private static func windowsOnly(_ feature: String) -> CommandRefusal {
        CommandRefusal(
            code: "W01",
            message: "\(feature) yalnız Windows programında var; iPhone'da bu işlem yok. Örnek: 25 sağa taşı",
            suggestions: ["25 sağa taşı"])
    }

    private static func elsewhere(_ message: String) -> CommandRefusal {
        CommandRefusal(code: "P01", message: message, suggestions: ["25 sağa taşı"])
    }

    private static let noMouseTools = "Fare araçları yalnız Windows programında var. iPhone'da parçaya dokunun; yeni kontur için “Parmakla şekil çiz” düğmesini kullanın."

    /// The refusal for a command the iPhone cannot do or does through a button, nil when it can.
    private static func unsupported(_ command: ParsedCommand) -> CommandRefusal? {
        switch command {
        case .mirror:
            return windowsOnly("Aynalama")
        case .view(let action):
            switch action {
            case .detailedView, .allTools, .textLarger, .textSmaller, .themeDark, .themeLight:
                return windowsOnly("Bu ayar")
            default:
                return elsewhere("Görünüm komutla değişmez; çizim ekrana sığdırılır. Parçaları taşımak için: 25 sağa taşı")
            }
        case .tool, .finishDrawing:
            return elsewhere(noMouseTools)
        case .file(let action):
            if action == .open || action == .new {
                return elsewhere("Proje açma ve yeni proje ana ekrandan yapılır. Önce “Kapat” düğmesine dokunun.")
            }
            return nil
        case .handoff(let target, let tab, _):
            switch target {
            case .duplicate:
                return nil
            case .export:
                return elsewhere("Dosya vermek için aşağıdaki “Dosyayı hazırla (.tcad)” düğmesini, sonra Paylaş düğmesini kullanın.")
            case .importDxf:
                return windowsOnly("DXF")
            case .exportSelectedDxf, .exportSelectedA4:
                return windowsOnly("PDF ve DXF çıktısı")
            case .textileTools:
                if tab == 2 { return windowsOnly("Dikiş payı") }
                if tab == 1 { return windowsOnly("Beden serileme") }
                if tab == 3 { return windowsOnly("Pastal yerleştirme") }
                return windowsOnly("Bu araç")
            case .machineCenter:
                return windowsOnly("Makine dosyası")
            case .grainline:
                return windowsOnly("İplik yönü")
            case .pieceMetadata:
                return windowsOnly("Parça bilgileri")
            case .mirrorCopy:
                return windowsOnly("Karşı parça")
            case .groupMove:
                return windowsOnly("Birden fazla parçayı taşıma")
            case .layers:
                return windowsOnly("Katmanlar")
            case .coordinateDrawing:
                return elsewhere(noMouseTools)
            case .about, .designRequest, .recoveryScan:
                return windowsOnly("Bu ayar")
            }
        default:
            return nil
        }
    }

    // MARK: Helpers

    private static func make(_ sentence: String, _ note: String?, _ effect: CommandPlan.Effect, _ notice: String,
                             _ base: DraftProject, _ context: CommandContext, destructive: Bool = false) -> CommandPlan {
        CommandPlan(sentence: sentence, note: note, effect: effect, isDestructive: destructive,
                    notice: notice, base: base, baseSelection: context.selectedID)
    }

    private static func pendingProblem(_ context: CommandContext) -> CommandRefusal {
        let reason = context.baseProblem ?? "Parça adı, konum ya da ölçü alanlarında geçersiz bir değer var."
        return CommandRefusal(
            code: "P02",
            message: "\(reason) Komut, alanlardaki değerlere dayanır; önce onları düzeltin.")
    }

    private static func nothingSelected(_ project: DraftProject) -> CommandRefusal {
        if project.pieces.isEmpty { return noPieces() }
        let names = project.pieces.prefix(3).map { "\(TurkishText.toLower($0.name)) seç" }
        return CommandRefusal(
            code: "R03",
            message: "Önce bir parça seçin. Parçaya dokunun ya da \(names.first ?? "ön beden seç") yazın.",
            suggestions: Array(names))
    }

    private static func withinLimit(_ points: [DraftPoint]) -> Bool {
        points.allSatisfy { abs($0.x) <= DraftContourGeometry.coordinateLimit && abs($0.y) <= DraftContourGeometry.coordinateLimit }
    }

    private static func limitRefusal() -> CommandRefusal {
        CommandRefusal(code: "P03", message: "Sonuç çizimin parça, nokta ya da koordinat sınırlarını aşıyor. Hiçbir şey değişmedi.")
    }

    private static func corner(_ piece: DraftPiece) -> String { "\(n(piece.minX)), \(n(piece.minY)) mm" }

    private static func extent(_ piece: DraftPiece) -> (width: Double, height: Double) {
        (piece.maxX - piece.minX, piece.maxY - piece.minY)
    }

    private static func isAxisRectangle(_ piece: DraftPiece) -> Bool {
        guard piece.closed, piece.points.count == 4 else { return false }
        for i in 0..<4 {
            let a = piece.points[i]
            let b = piece.points[(i + 1) % 4]
            if abs(a.x - b.x) > 1e-9 && abs(a.y - b.y) > 1e-9 { return false }
        }
        return true
    }

    // MARK: Pieces by name

    /// Indices of the pieces whose name contains the query, ignoring case and Turkish letters.
    static func matches(_ query: String, in project: DraftProject) -> [Int] {
        let needle = TurkishText.fold(query)
        guard !needle.isEmpty else { return [] }
        return project.pieces.enumerated()
            .filter { TurkishText.fold($0.element.name).contains(needle) }
            .map { $0.offset }
    }

    /// The one piece a command names, or the refusal that explains why not.
    private static func resolve(name: String, index: Int?, in project: DraftProject) -> Result<Int, CommandRefusal> {
        if let index {
            guard project.pieces.indices.contains(index), project.pieces[index].name == name else {
                return .failure(CommandRefusal(code: "R22", message: "Parçalar değişti; komutu yeniden yazın."))
            }
            return .success(index)
        }
        let hits = matches(name, in: project)
        if hits.isEmpty {
            return .failure(CommandRefusal(code: "R15a", message: "“\(name)” adında parça bulunamadı."))
        }
        if hits.count > 1 {
            return .failure(CommandRefusal(code: "R15b", message: "\(hits.count) parça eşleşti. Hangisi?",
                                           suggestions: hits.prefix(3).map { project.pieces[$0].name }))
        }
        return .success(hits[0])
    }

    private static func planSelect(name: String, index: Int?, base: DraftProject, context: CommandContext, note: String?) -> PlanResult {
        switch resolve(name: name, index: index, in: base) {
        case .failure(let refusal):
            return .refusal(refusal)
        case .success(let found):
            let piece = base.pieces[found]
            return .plan(make("\(quoted(piece.name)) seçilecek.", note, .select(piece.id),
                              "\(quoted(piece.name)) seçildi.", base, context))
        }
    }

    private static func planAdjacent(next: Bool, base: DraftProject, context: CommandContext, note: String?) -> PlanResult {
        guard !base.pieces.isEmpty else { return .refusal(noPieces()) }
        let current = base.pieces.firstIndex(where: { $0.id == context.selectedID })
        let count = base.pieces.count
        let target: Int
        if let current {
            if count == 1 {
                return .refusal(CommandRefusal(code: "R25", message: "Tek parça var; seçim değişmez."))
            }
            target = next ? (current + 1) % count : (current + count - 1) % count
        } else {
            target = next ? 0 : count - 1
        }
        let piece = base.pieces[target]
        return .plan(make("\(quoted(piece.name)) seçilecek.", note, .select(piece.id),
                          "\(quoted(piece.name)) seçildi.", base, context))
    }

    // MARK: New pieces

    private static func planRectangle(_ width: Double, _ height: Double, isSquare: Bool, base: DraftProject,
                                      context: CommandContext, note: String?) -> PlanResult {
        guard base.pieces.count < 250 else {
            return .refusal(CommandRefusal(code: "P03", message: "Parça sınırına ulaşıldı (en çok 250 parça)."))
        }
        let start = Double(base.pieces.count) * 20
        let points = [
            DraftPoint(x: start, y: start), DraftPoint(x: start + width, y: start),
            DraftPoint(x: start + width, y: start + height), DraftPoint(x: start, y: start + height)
        ]
        if DraftContourGeometry.validationError(points: points, closed: true) != nil { return .refusal(limitRefusal()) }
        let piece = DraftPiece(name: "Dikdörtgen \(base.pieces.count + 1)", closed: true, points: points)
        var updated = base
        updated.pieces.append(piece)
        guard CADLocalDraftStore.isValid(updated) else { return .refusal(limitRefusal()) }
        let size = CommandFormat.size(width, height)
        let word = isSquare ? "Kare" : "Dikdörtgen"
        let sentence = "\(word) çizilecek: \(size). Sol alt köşe: \(n(start)) mm, \(n(start)) mm."
        return .plan(make(sentence, note, .edit(updated, .piece(piece.id)), "\(word) eklendi: \(size).", base, context))
    }

    private static func planLine(_ length: Double, vertical: Bool, base: DraftProject,
                                 context: CommandContext, note: String?) -> PlanResult {
        guard base.pieces.count < 250 else {
            return .refusal(CommandRefusal(code: "P03", message: "Parça sınırına ulaşıldı (en çok 250 parça)."))
        }
        let start = Double(base.pieces.count) * 20
        let end = vertical ? DraftPoint(x: start, y: start + length) : DraftPoint(x: start + length, y: start)
        let points = [DraftPoint(x: start, y: start), end]
        if DraftContourGeometry.validationError(points: points, closed: false) != nil { return .refusal(limitRefusal()) }
        let piece = DraftPiece(name: "Çizgi \(base.pieces.count + 1)", closed: false, points: points)
        var updated = base
        updated.pieces.append(piece)
        guard CADLocalDraftStore.isValid(updated) else { return .refusal(limitRefusal()) }
        let direction = vertical ? "dikey" : "yatay"
        let sentence = "Çizgi çizilecek: \(CommandFormat.length(length)), \(direction). Başlangıç: \(n(start)) mm, \(n(start)) mm."
        return .plan(make(sentence, note, .edit(updated, .piece(piece.id)),
                          "Çizgi eklendi: \(CommandFormat.length(length)).", base, context))
    }

    // MARK: Moving

    private static func planMove(right: Double, up: Double, index: Int, piece: DraftPiece, base: DraftProject,
                                 context: CommandContext, note: String?) -> PlanResult {
        let points = piece.points.map { DraftPoint(x: $0.x + right, y: $0.y + up) }
        guard withinLimit(points) else { return .refusal(limitRefusal()) }
        var updated = base
        updated.pieces[index].points = points
        var parts: [String] = []
        if right != 0 { parts.append("\(n(abs(right))) mm \(right > 0 ? "sağa" : "sola")") }
        if up != 0 { parts.append("\(n(abs(up))) mm \(up > 0 ? "yukarı" : "aşağı")") }
        let what = parts.joined(separator: " ve ")
        var centimetres = ""
        if parts.count == 1 {
            let distance = abs(right != 0 ? right : up)
            centimetres = " (\(n(distance / 10)) cm)"
        }
        let moved = updated.pieces[index]
        let sentence = "\(quoted(piece.name)) \(what) taşınacak\(centimetres). Sol alt köşe \(corner(piece)) iken \(corner(moved)) olacak."
        return .plan(make(sentence, note, .edit(updated, .piece(piece.id)),
                          "\(quoted(piece.name)) taşındı: \(what).", base, context))
    }

    private static func planMoveTo(x: Double, y: Double, index: Int, piece: DraftPiece, base: DraftProject,
                                   context: CommandContext, note: String?) -> PlanResult {
        let dx = x - piece.minX
        let dy = y - piece.minY
        if dx == 0 && dy == 0 {
            return .refusal(CommandRefusal(code: "R25", message: "Parça zaten \(n(x)), \(n(y)) mm noktasında. Değişiklik yok."))
        }
        let points = piece.points.map { DraftPoint(x: $0.x + dx, y: $0.y + dy) }
        guard withinLimit(points) else { return .refusal(limitRefusal()) }
        var updated = base
        updated.pieces[index].points = points
        let sentence = "\(quoted(piece.name)) sol alt köşesi \(n(x)), \(n(y)) mm noktasına taşınacak."
        let extra = "X ve Y, alanlardaki değerlerdir; Y alttan ölçülür."
        return .plan(make(sentence, [note, extra].compactMap { $0 }.joined(separator: " "),
                          .edit(updated, .piece(piece.id)),
                          "\(quoted(piece.name)) \(n(x)), \(n(y)) mm noktasına taşındı.", base, context))
    }

    // MARK: Rotating

    private static func planRotate(_ degrees: Int, clockwise: Bool, index: Int, piece: DraftPiece, base: DraftProject,
                                   context: CommandContext, note: String?) -> PlanResult {
        let quarters = degrees / 90
        // The app turns counter-clockwise; a clockwise turn is the other three quarters.
        let turns = clockwise ? (4 - quarters % 4) % 4 : quarters % 4
        var turned = piece
        for _ in 0..<turns { turned.points = turned.rotatedQuarterTurn() }
        guard withinLimit(turned.points) else {
            return .refusal(CommandRefusal(code: "P03", message: "Döndürülmüş parça çalışma alanı sınırını aşıyor. Hiçbir şey değişmedi."))
        }
        var updated = base
        updated.pieces[index].points = turned.points
        let before = extent(piece)
        let after = extent(turned)
        let direction: String
        if degrees == 180 {
            direction = "180°"
        } else {
            direction = (clockwise ? "saat yönünde " : "saat yönünün tersine ") + "\(degrees)°"
        }
        var sentence = "\(quoted(piece.name)) \(direction) döndürülecek (ortası sabit)."
        if degrees != 180 && abs(before.width - before.height) > 1e-9 {
            sentence += " \(n(before.width)) × \(n(before.height)) mm iken \(n(after.width)) × \(n(after.height)) mm olacak."
        } else {
            sentence += " Boyutları değişmez."
        }
        var extra: String?
        if degrees != 180 {
            extra = clockwise
                ? "“90° döndür” düğmesi saat yönünün tersine döndürür; bu komut saat yönünde döndürüyor."
                : "Bu, “90° döndür” düğmesiyle aynı yöndür."
        }
        let combined = [note, extra].compactMap { $0 }.joined(separator: " ")
        return .plan(make(sentence, combined.isEmpty ? nil : combined, .edit(updated, .piece(piece.id)),
                          "\(quoted(piece.name)) \(direction) döndürüldü.", base, context))
    }

    // MARK: Resizing

    private static func planResize(width: Double?, height: Double?, index: Int, piece: DraftPiece, base: DraftProject,
                                   context: CommandContext, note: String?) -> PlanResult {
        let oldWidth = piece.lineLength
        let oldHeight = piece.maxY - piece.minY
        if !piece.closed && height != nil {
            return .refusal(CommandRefusal(
                code: "P01",
                message: "Çizgide yalnız uzunluk değişir. Örnek: uzunluğu 150 yap",
                suggestions: ["uzunluğu 150 yap"]))
        }
        let newWidth = width ?? oldWidth
        let newHeight = height ?? oldHeight
        if newWidth == oldWidth && (newHeight == oldHeight || !piece.closed) {
            let text = piece.closed ? "\(n(oldWidth)) × \(n(oldHeight)) mm" : "\(n(oldWidth)) mm"
            return .refusal(CommandRefusal(code: "R25", message: "Ölçü zaten \(text). Değişiklik yok."))
        }
        func text(_ value: Double?, current: Double) -> String {
            value.map { String($0) } ?? DraftPiece.display(current)
        }
        guard let points = piece.applyingDimensions(
            x: DraftPiece.display(piece.minX), y: DraftPiece.display(piece.minY),
            width: text(width, current: oldWidth), height: text(height, current: oldHeight)) else {
            return .refusal(CommandRefusal(
                code: "P04",
                message: "Bu ölçü parçanın şeklini bozuyor ya da çalışma alanı sınırını aşıyor. Hiçbir şey değişmedi."))
        }
        var updated = base
        updated.pieces[index].points = points
        guard CADLocalDraftStore.isValid(updated) else { return .refusal(limitRefusal()) }
        let name = quoted(piece.name)
        var sentence: String
        if !piece.closed {
            sentence = "\(name) uzunluğu \(n(oldWidth)) mm iken \(n(newWidth)) mm olacak (\(n(newWidth / 10)) cm). Sol alt köşe yerinde durur."
        } else if width != nil && height != nil {
            sentence = "\(name) ölçüsü \(n(oldWidth)) × \(n(oldHeight)) mm iken \(n(newWidth)) × \(n(newHeight)) mm olacak (\(n(newWidth / 10)) × \(n(newHeight / 10)) cm). Sol alt köşe yerinde durur."
        } else if width != nil {
            sentence = "\(name) genişliği \(n(oldWidth)) mm iken \(n(newWidth)) mm olacak (\(n(newWidth / 10)) cm). Yükseklik \(n(oldHeight)) mm aynı kalır; sol alt köşe yerinde durur."
        } else {
            sentence = "\(name) yüksekliği \(n(oldHeight)) mm iken \(n(newHeight)) mm olacak (\(n(newHeight / 10)) cm). Genişlik \(n(oldWidth)) mm aynı kalır; sol alt köşe yerinde durur."
        }
        var extra: String?
        if piece.closed && !isAxisRectangle(piece) { extra = "Bu parça dikdörtgen değil; bütün konturu oranlı ölçeklenir." }
        let combined = [note, extra].compactMap { $0 }.joined(separator: " ")
        let done = piece.closed ? "\(n(newWidth)) × \(n(newHeight)) mm" : "\(n(newWidth)) mm"
        return .plan(make(sentence, combined.isEmpty ? nil : combined, .edit(updated, .piece(piece.id)),
                          "\(name) ölçüsü \(done) oldu.", base, context))
    }

    // MARK: Name, copy, delete

    private static func planRename(_ newName: String, index: Int, piece: DraftPiece, base: DraftProject,
                                   context: CommandContext, note: String?) -> PlanResult {
        let name = newName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else {
            return .refusal(CommandRefusal(code: "R24", message: "Yeni ad boş olamaz. Örnek: adı ön beden olsun",
                                           suggestions: ["adı ön beden olsun"]))
        }
        guard name.count <= 128 else {
            return .refusal(CommandRefusal(code: "R24", message: "Ad en fazla 128 karakter olabilir."))
        }
        if name == piece.name {
            return .refusal(CommandRefusal(code: "R25", message: "Ad zaten \(quoted(name)). Değişiklik yok."))
        }
        var updated = base
        updated.pieces[index].name = name
        return .plan(make("\(quoted(piece.name)) adı \(quoted(name)) olacak.", note,
                          .edit(updated, .piece(piece.id)), "Parça adı \(quoted(name)) oldu.", base, context))
    }

    private static func planDuplicate(index: Int, piece: DraftPiece, base: DraftProject,
                                      context: CommandContext, note: String?) -> PlanResult {
        guard base.pieces.count < 250 else {
            return .refusal(CommandRefusal(code: "P03", message: "Parça sınırına ulaşıldı (en çok 250 parça)."))
        }
        var copy = piece
        copy.id = UUID()
        copy.name = String(piece.name.prefix(122)) + " kopya"
        copy.points = piece.points.map { DraftPoint(x: $0.x + 20, y: $0.y + 20) }
        guard withinLimit(copy.points) else { return .refusal(limitRefusal()) }
        var updated = base
        updated.pieces.append(copy)
        guard CADLocalDraftStore.isValid(updated) else { return .refusal(limitRefusal()) }
        let sentence = "\(quoted(piece.name)) kopyalanacak. Kopya 20 mm sağa ve 20 mm yukarı kayık, \(quoted(copy.name)) adıyla eklenir."
        return .plan(make(sentence, note, .edit(updated, .piece(copy.id)),
                          "Kopya eklendi: \(quoted(copy.name)).", base, context))
    }

    /// Deleting does not apply the text fields first (the Sil button does not either) and always asks.
    private static func planDelete(name: String?, index: Int?, _ context: CommandContext) -> PlanResult {
        let document = context.document
        let target: Int
        if let name {
            switch resolve(name: name, index: index, in: document) {
            case .failure(let refusal): return .refusal(refusal)
            case .success(let found): target = found
            }
        } else {
            guard let found = document.pieces.firstIndex(where: { $0.id == context.selectedID }) else {
                return .refusal(nothingSelected(document))
            }
            target = found
        }
        let piece = document.pieces[target]
        var updated = document
        updated.pieces.remove(at: target)
        let selection: CommandPlan.SelectionAfter = piece.id == context.selectedID ? .piece(updated.pieces.first?.id) : .keep
        let size = extent(piece)
        let measure = piece.closed ? "\(n(size.width)) × \(n(size.height)) mm" : "\(n(piece.lineLength)) mm"
        let sentence = "\(quoted(piece.name)) silinecek (\(measure)). Geri alabilirsiniz."
        let plan = CommandPlan(sentence: sentence, note: nil, effect: .edit(updated, selection), isDestructive: true,
                               notice: "\(quoted(piece.name)) silindi. Geri alabilirsiniz.",
                               base: document, baseSelection: context.selectedID)
        return .plan(plan)
    }

    // MARK: Undo and redo

    private static func planUndo(_ steps: Int, _ context: CommandContext) -> PlanResult {
        if context.undoCount == 0 {
            return .refusal(CommandRefusal(
                code: "R18", message: "Geri alınacak işlem yok. Önce bir şey yazıp uygulayın. Örnek: 120 x 80 dikdörtgen",
                suggestions: ["120 x 80 dikdörtgen"]))
        }
        if steps > context.undoCount {
            return .refusal(CommandRefusal(
                code: "R18", message: "Yalnız \(context.undoCount) işlem geri alınabilir. Örnek: \(context.undoCount) adım geri",
                suggestions: ["\(context.undoCount) adım geri"]))
        }
        let pending = context.base != context.document
        let sentence = steps == 1 ? "Son işlem geri alınacak." : "Son \(steps) işlem geri alınacak."
        let note = pending ? "Alanlara yazılıp uygulanmamış değerler silinir." : nil
        return .plan(CommandPlan(sentence: sentence, note: note, effect: .undo(steps), isDestructive: false,
                                 notice: steps == 1 ? "Son işlem geri alındı." : "Son \(steps) işlem geri alındı.",
                                 base: context.document, baseSelection: context.selectedID))
    }

    private static func planRedo(_ context: CommandContext) -> PlanResult {
        if context.redoCount == 0 {
            return .refusal(CommandRefusal(code: "R18", message: "Yinelenecek işlem yok."))
        }
        let pending = context.base != context.document
        return .plan(CommandPlan(sentence: "Geri alınan işlem yinelenecek.",
                                 note: pending ? "Alanlara yazılıp uygulanmamış değerler silinir." : nil,
                                 effect: .redo, isDestructive: false, notice: "İşlem yeniden uygulandı.",
                                 base: context.document, baseSelection: context.selectedID))
    }
}

extension CommandPlanner {
    /// The refusal for a drawing without any piece.
    fileprivate static func noPieces() -> CommandRefusal {
        CommandRefusal(code: "R03", message: "Henüz parça yok. Önce bir parça çizin. Örnek: 120 x 80 dikdörtgen",
                       suggestions: ["120 x 80 dikdörtgen"])
    }
}
