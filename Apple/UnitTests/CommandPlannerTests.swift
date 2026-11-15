import XCTest
@testable import TekstilCADViewer

/// Plans are made against a drawing without touching it; these tests check the sentence shown
/// before anything happens, the resulting geometry (in the iPhone's own axes: Y up, lower-left
/// corner) and the refusals that depend on the drawing.
final class CommandPlannerTests: XCTestCase {
    private func p(_ x: Double, _ y: Double) -> DraftPoint { DraftPoint(x: x, y: y) }

    /// "Ön beden" 100 × 150 at the origin (selected by default) and an open "Çizgi" 100 long.
    private func sample() -> DraftProject {
        DraftProject(name: "Deneme", pieces: [
            DraftPiece(name: "Ön beden", closed: true, points: [p(0, 0), p(100, 0), p(100, 150), p(0, 150)]),
            DraftPiece(name: "Çizgi", closed: false, points: [p(0, 0), p(100, 0)])
        ])
    }

    private func context(_ document: DraftProject, selected: Int? = 0, base: DraftProject?? = nil,
                         undo: Int = 0, redo: Int = 0) -> CommandContext {
        let resolvedBase: DraftProject? = base ?? .some(document)
        return CommandContext(document: document, base: resolvedBase, baseProblem: nil,
                              selectedID: selected.map { document.pieces[$0].id },
                              undoCount: undo, redoCount: redo)
    }

    private func plan(_ command: ParsedCommand, _ context: CommandContext,
                      file: StaticString = #filePath, line: UInt = #line) -> CommandPlan? {
        switch CommandPlanner.plan(command, context: context) {
        case .plan(let plan): return plan
        case .refusal(let r):
            XCTFail("\(command) was refused: \(r.message)", file: file, line: line)
            return nil
        }
    }

    private func refusal(_ command: ParsedCommand, _ context: CommandContext) -> CommandRefusal? {
        if case .refusal(let r) = CommandPlanner.plan(command, context: context) { return r }
        return nil
    }

    private func edited(_ plan: CommandPlan?, file: StaticString = #filePath, line: UInt = #line) -> (DraftProject, CommandPlan.SelectionAfter)? {
        guard let plan else { return nil }
        guard case .edit(let project, let selection) = plan.effect else {
            XCTFail("an edit was expected, got \(plan.effect)", file: file, line: line)
            return nil
        }
        XCTAssertTrue(CADLocalDraftStore.isValid(project), "the result must be a storable drawing", file: file, line: line)
        return (project, selection)
    }

    // MARK: Drawing

    func testRectangleIsPlacedLikeTheButtonPlacesIt() throws {
        let doc = sample()
        let planned = plan(.addRectangle(width: 120, height: 80), context(doc))
        XCTAssertEqual(planned?.sentence,
                       "Dikdörtgen çizilecek: 120 × 80 mm (12 × 8 cm). Sol alt köşe: 40 mm, 40 mm.")
        let (project, selection) = try XCTUnwrap(edited(planned))
        XCTAssertEqual(project.pieces.count, 3)
        let added = project.pieces[2]
        XCTAssertEqual(added.name, "Dikdörtgen 3")
        XCTAssertTrue(added.closed)
        XCTAssertEqual(added.points, [p(40, 40), p(160, 40), p(160, 120), p(40, 120)])
        XCTAssertEqual(selection, .piece(added.id))
        // Nothing is changed by planning.
        XCTAssertEqual(doc, sample().withIDs(of: doc))
    }

    func testFirstRectangleOfAnEmptyDrawing() throws {
        let doc = DraftProject.blank
        let planned = plan(.addRectangle(width: 120, height: 80), context(doc, selected: nil))
        XCTAssertEqual(planned?.sentence, "Dikdörtgen çizilecek: 120 × 80 mm (12 × 8 cm). Sol alt köşe: 0 mm, 0 mm.")
        let (project, _) = try XCTUnwrap(edited(planned))
        XCTAssertEqual(project.pieces[0].points[2], p(120, 80))
    }

    func testLinesAreHorizontalOrVertical() throws {
        let doc = sample()
        var (project, _) = try XCTUnwrap(edited(plan(.addLine(length: 150, vertical: false), context(doc))))
        XCTAssertEqual(project.pieces[2].points, [p(40, 40), p(190, 40)])
        XCTAssertFalse(project.pieces[2].closed)
        (project, _) = try XCTUnwrap(edited(plan(.addLine(length: 80, vertical: true), context(doc))))
        XCTAssertEqual(project.pieces[2].points, [p(40, 40), p(40, 120)])
        XCTAssertEqual(plan(.addLine(length: 150, vertical: false), context(doc))?.sentence,
                       "Çizgi çizilecek: 150 mm (15 cm), yatay. Başlangıç: 40 mm, 40 mm.")
    }

    func testPieceLimit() {
        var doc = DraftProject.blank
        for i in 0..<250 {
            doc.pieces.append(DraftPiece(name: "P\(i)", closed: false, points: [p(0, 0), p(1, 0)]))
        }
        XCTAssertEqual(refusal(.addRectangle(width: 10, height: 10), context(doc, selected: nil))?.code, "P03")
        XCTAssertEqual(refusal(.duplicate, context(doc, selected: 0))?.code, "P03")
    }

    // MARK: Moving

    func testMoveUsesScreenDirectionsOnTheUpwardsAxis() throws {
        let doc = sample()
        let right = plan(.move(right: 25, down: 0), context(doc))
        XCTAssertEqual(right?.sentence,
                       "‘Ön beden’ 25 mm sağa taşınacak (2,5 cm). Sol alt köşe 0, 0 mm iken 25, 0 mm olacak.")
        var (project, selection) = try XCTUnwrap(edited(right))
        XCTAssertEqual(project.pieces[0].points, [p(25, 0), p(125, 0), p(125, 150), p(25, 150)])
        XCTAssertEqual(selection, .piece(doc.pieces[0].id))
        XCTAssertEqual(project.pieces[1], doc.pieces[1])

        // "yukarı" is +Y on the iPhone (the shared vectors say down = -10).
        let up = plan(.move(right: 0, down: -10), context(doc))
        XCTAssertTrue(up?.sentence.contains("10 mm yukarı taşınacak") == true)
        (project, selection) = try XCTUnwrap(edited(up))
        XCTAssertEqual(project.pieces[0].minY, 10)

        let both = plan(.move(right: -20, down: 5), context(doc))
        XCTAssertTrue(both?.sentence.contains("20 mm sola ve 5 mm aşağı taşınacak.") == true)
        (project, selection) = try XCTUnwrap(edited(both))
        XCTAssertEqual(project.pieces[0].minX, -20)
        XCTAssertEqual(project.pieces[0].minY, -5)
    }

    func testMoveToUsesTheLowerLeftCorner() throws {
        let doc = sample()
        let planned = plan(.moveTo(x: 50, y: 60), context(doc))
        XCTAssertTrue(planned?.sentence.contains("sol alt köşesi 50, 60 mm noktasına taşınacak") == true)
        XCTAssertTrue(planned?.note?.contains("Y alttan ölçülür") == true)
        let (project, _) = try XCTUnwrap(edited(planned))
        XCTAssertEqual(project.pieces[0].minX, 50)
        XCTAssertEqual(project.pieces[0].minY, 60)
        XCTAssertEqual(project.pieces[0].maxY, 210)
        XCTAssertEqual(refusal(.moveTo(x: 0, y: 0), context(doc))?.code, "R25")
    }

    func testMoveCannotLeaveTheWorkArea() {
        let doc = sample()
        XCTAssertEqual(refusal(.move(right: 200_000_000, down: 0), context(doc))?.code, "P03")
    }

    // MARK: Rotating

    func testRotationDirectionsMatchTheButtonAndItsInverse() throws {
        let doc = sample()
        // The button turns counter-clockwise about the bounding-box centre (50, 75).
        let ccw = plan(.rotate(degrees: 90, clockwise: false), context(doc))
        XCTAssertTrue(ccw?.sentence.hasPrefix("‘Ön beden’ saat yönünün tersine 90° döndürülecek (ortası sabit).") == true)
        XCTAssertTrue(ccw?.sentence.contains("100 × 150 mm iken 150 × 100 mm olacak.") == true)
        XCTAssertTrue(ccw?.note?.contains("düğmesiyle aynı") == true)
        var (project, _) = try XCTUnwrap(edited(ccw))
        XCTAssertEqual(project.pieces[0].points[0], p(125, 25))
        XCTAssertEqual(project.pieces[0].points, doc.pieces[0].rotatedQuarterTurn())

        // Clockwise is the other three quarters: the lower-left corner goes to the upper-left.
        let cw = plan(.rotate(degrees: 90, clockwise: true), context(doc))
        XCTAssertTrue(cw?.sentence.hasPrefix("‘Ön beden’ saat yönünde 90° döndürülecek") == true)
        XCTAssertTrue(cw?.note?.contains("saat yönünün tersine döndürür") == true)
        (project, _) = try XCTUnwrap(edited(cw))
        XCTAssertEqual(project.pieces[0].points[0], p(-25, 125))
        XCTAssertEqual(project.pieces[0].minX, -25)
        XCTAssertEqual(project.pieces[0].minY, 25)

        // 270° clockwise is one counter-clockwise turn.
        let long = plan(.rotate(degrees: 270, clockwise: true), context(doc))
        (project, _) = try XCTUnwrap(edited(long))
        XCTAssertEqual(project.pieces[0].points[0], p(125, 25))

        let half = plan(.rotate(degrees: 180, clockwise: true), context(doc))
        XCTAssertTrue(half?.sentence.hasPrefix("‘Ön beden’ 180° döndürülecek (ortası sabit). Boyutları değişmez.") == true)
        (project, _) = try XCTUnwrap(edited(half))
        XCTAssertEqual(project.pieces[0].points[0], p(100, 150))
    }

    func testRotatingASquareDoesNotClaimASizeChange() {
        var doc = sample()
        doc.pieces[0].points = [p(0, 0), p(100, 0), p(100, 100), p(0, 100)]
        let planned = plan(.rotate(degrees: 90, clockwise: true), context(doc))
        XCTAssertTrue(planned?.sentence.hasSuffix("Boyutları değişmez.") == true)
    }

    // MARK: Resizing

    func testResizeScalesFromTheLowerLeftCorner() throws {
        let doc = sample()
        let width = plan(.resize(width: 95, height: nil), context(doc))
        XCTAssertEqual(width?.sentence,
                       "‘Ön beden’ genişliği 100 mm iken 95 mm olacak (9,5 cm). Yükseklik 150 mm aynı kalır; sol alt köşe yerinde durur.")
        let (project, _) = try XCTUnwrap(edited(width))
        XCTAssertEqual(project.pieces[0].minX, 0)
        XCTAssertEqual(project.pieces[0].maxX, 95, accuracy: 1e-9)
        XCTAssertEqual(project.pieces[0].maxY, 150, accuracy: 1e-9)

        let height = plan(.resize(width: nil, height: 60), context(doc))
        XCTAssertTrue(height?.sentence.hasPrefix("‘Ön beden’ yüksekliği 150 mm iken 60 mm olacak (6 cm). Genişlik 100 mm aynı kalır") == true)
        let both = plan(.resize(width: 95, height: 60), context(doc))
        XCTAssertEqual(both?.sentence,
                       "‘Ön beden’ ölçüsü 100 × 150 mm iken 95 × 60 mm olacak (9,5 × 6 cm). Sol alt köşe yerinde durur.")
    }

    func testResizeRefusals() {
        let doc = sample()
        XCTAssertEqual(refusal(.resize(width: 100, height: nil), context(doc))?.code, "R25")
        XCTAssertEqual(refusal(.resize(width: 100, height: 150), context(doc))?.code, "R25")
        // The open line has a length, not a height.
        XCTAssertEqual(refusal(.resize(width: nil, height: 50), context(doc, selected: 1))?.code, "P01")
        XCTAssertEqual(refusal(.resize(width: 100, height: nil), context(doc, selected: 1))?.code, "R25")
    }

    func testResizeOfALineChangesItsLength() throws {
        let doc = sample()
        let planned = plan(.resize(width: 150, height: nil), context(doc, selected: 1))
        XCTAssertTrue(planned?.sentence.contains("uzunluğu 100 mm iken 150 mm olacak (15 cm)") == true)
        let (project, _) = try XCTUnwrap(edited(planned))
        XCTAssertEqual(project.pieces[1].lineLength, 150, accuracy: 1e-9)
    }

    func testResizeOfANonRectangleSaysItScalesTheContour() {
        var doc = sample()
        doc.pieces[0].points = [p(0, 0), p(100, 0), p(100, 150), p(50, 200), p(0, 150)]
        let planned = plan(.resize(width: 50, height: nil), context(doc))
        XCTAssertTrue(planned?.note?.contains("dikdörtgen değil") == true)
    }

    // MARK: Name, copy

    func testRenameAndDuplicate() throws {
        let doc = sample()
        let renamed = plan(.rename("Kol"), context(doc))
        XCTAssertEqual(renamed?.sentence, "‘Ön beden’ adı ‘Kol’ olacak.")
        var (project, _) = try XCTUnwrap(edited(renamed))
        XCTAssertEqual(project.pieces[0].name, "Kol")
        XCTAssertEqual(project.pieces[0].points, doc.pieces[0].points)
        XCTAssertEqual(refusal(.rename("Ön beden"), context(doc))?.code, "R25")

        let copy = plan(.duplicate, context(doc))
        XCTAssertTrue(copy?.sentence.contains("‘Ön beden kopya’ adıyla eklenir") == true)
        let (copied, selection) = try XCTUnwrap(edited(copy))
        XCTAssertEqual(copied.pieces.count, 3)
        XCTAssertEqual(copied.pieces[2].name, "Ön beden kopya")
        XCTAssertEqual(copied.pieces[2].points[0], p(20, 20))
        XCTAssertNotEqual(copied.pieces[2].id, copied.pieces[0].id)
        XCTAssertEqual(selection, .piece(copied.pieces[2].id))
        project = copied
        XCTAssertEqual(project.pieces[0], doc.pieces[0])
    }

    // MARK: Delete, select

    func testDeleteAlwaysAsksAndSelectsTheFirstRemainingPiece() throws {
        let doc = sample()
        let planned = plan(.delete(name: nil), context(doc))
        XCTAssertEqual(planned?.sentence, "‘Ön beden’ silinecek (100 × 150 mm). Geri alabilirsiniz.")
        XCTAssertEqual(planned?.isDestructive, true)
        let (project, selection) = try XCTUnwrap(edited(planned))
        XCTAssertEqual(project.pieces.map(\.name), ["Çizgi"])
        XCTAssertEqual(selection, .piece(project.pieces[0].id))
    }

    func testDeleteByNameKeepsTheSelectionWhenAnotherPieceGoes() throws {
        let doc = sample()
        let planned = plan(.delete(name: "çizgi"), context(doc))
        XCTAssertEqual(planned?.sentence, "‘Çizgi’ silinecek (100 mm). Geri alabilirsiniz.")
        XCTAssertEqual(planned?.isDestructive, true)
        let (project, selection) = try XCTUnwrap(edited(planned))
        XCTAssertEqual(project.pieces.map(\.name), ["Ön beden"])
        XCTAssertEqual(selection, .keep)
        // A stale or unknown name never deletes something else.
        XCTAssertEqual(refusal(.delete(name: "bacak"), context(doc))?.code, "R15a")
        XCTAssertEqual(refusal(.deleteAt(index: 1, name: "Ön beden"), context(doc))?.code, "R22")
        XCTAssertEqual(refusal(.deleteAt(index: 9, name: "Çizgi"), context(doc))?.code, "R22")
    }

    func testDeleteIgnoresInvalidFieldsLikeTheButtonDoes() {
        let doc = sample()
        let blocked = CommandContext(document: doc, base: nil, baseProblem: "x", selectedID: doc.pieces[0].id,
                                     undoCount: 0, redoCount: 0)
        XCTAssertNotNil(plan(.delete(name: nil), blocked))
    }

    func testSelectByNameAndNeighbour() {
        let doc = sample()
        XCTAssertEqual(plan(.select(name: "çizgi"), context(doc))?.effect, .select(doc.pieces[1].id))
        XCTAssertEqual(plan(.select(name: "çizgi"), context(doc))?.sentence, "‘Çizgi’ seçilecek.")
        XCTAssertEqual(plan(.selectAdjacent(next: true), context(doc, selected: 0))?.effect, .select(doc.pieces[1].id))
        XCTAssertEqual(plan(.selectAdjacent(next: true), context(doc, selected: 1))?.effect, .select(doc.pieces[0].id))
        XCTAssertEqual(plan(.selectAdjacent(next: false), context(doc, selected: 0))?.effect, .select(doc.pieces[1].id))
        XCTAssertEqual(plan(.selectAdjacent(next: true), context(doc, selected: nil))?.effect, .select(doc.pieces[0].id))
        XCTAssertEqual(plan(.clearSelection, context(doc))?.effect, .select(nil))
        XCTAssertEqual(refusal(.clearSelection, context(doc, selected: nil))?.code, "R25")
        XCTAssertEqual(refusal(.selectAt(index: 0, name: "Başka"), context(doc))?.code, "R22")
    }

    // MARK: What needs a selected piece

    func testNothingSelectedAsksForASelection() {
        let doc = sample()
        let none = context(doc, selected: nil)
        for command in [ParsedCommand.move(right: 1, down: 0), .rotate(degrees: 90, clockwise: true),
                        .resize(width: 1, height: nil), .rename("a"), .duplicate, .moveTo(x: 1, y: 1), .delete(name: nil)] {
            let r = refusal(command, none)
            XCTAssertEqual(r?.code, "R03", "\(command)")
            XCTAssertEqual(r?.suggestions, ["ön beden seç", "çizgi seç"])
        }
        XCTAssertEqual(refusal(.move(right: 1, down: 0), context(DraftProject.blank, selected: nil))?.code, "R03")
    }

    // MARK: Undo, redo, save, help

    func testUndoAndRedoCountsComeFromTheWorkshop() {
        let doc = sample()
        XCTAssertEqual(refusal(.undo(steps: 1), context(doc, undo: 0))?.code, "R18")
        XCTAssertEqual(refusal(.undo(steps: 3), context(doc, undo: 2))?.code, "R18")
        XCTAssertEqual(plan(.undo(steps: 1), context(doc, undo: 4))?.sentence, "Son işlem geri alınacak.")
        XCTAssertEqual(plan(.undo(steps: 3), context(doc, undo: 4))?.sentence, "Son 3 işlem geri alınacak.")
        XCTAssertEqual(plan(.undo(steps: 3), context(doc, undo: 4))?.effect, .undo(3))
        XCTAssertEqual(refusal(.redo, context(doc, redo: 0))?.sentenceFreeMessage, "Yinelenecek işlem yok.")
        XCTAssertEqual(plan(.redo, context(doc, redo: 1))?.effect, .redo)
    }

    func testSaveCommandsDescribeWhatWillBeWritten() {
        let doc = sample()
        XCTAssertEqual(plan(.save, context(doc))?.sentence,
                       "Proje kaydedilecek: ‘Deneme’. Ana ekrandaki Son projeler listesine eklenir.")
        XCTAssertEqual(plan(.save, context(doc))?.effect, .saveProject)
        XCTAssertEqual(plan(.saveCopy, context(doc))?.sentence,
                       "Projenin bağımsız bir kopyası ‘Deneme (kopya)’ adıyla kaydedilecek. Açık çizim değişmez.")
        XCTAssertEqual(plan(.saveCopy, context(doc))?.effect, .saveCopy)
    }

    func testHelpListsTheCommands() {
        let planned = plan(.help, context(sample()))
        XCTAssertEqual(planned?.effect, .info)
        XCTAssertEqual(planned?.lines, CommandPlanner.helpLines)
        XCTAssertTrue(planned?.lines.joined().contains("120 x 80 dikdörtgen") == true)
    }

    // MARK: Fields that were typed but not applied

    func testInvalidFieldsRefuseTheCommandWithTheirOwnReason() {
        let doc = sample()
        let blocked = CommandContext(document: doc, base: nil,
                                     baseProblem: "Parça adını, konumunu ve milimetre ölçülerini kontrol edin.",
                                     selectedID: doc.pieces[0].id, undoCount: 0, redoCount: 0)
        let r = refusal(.move(right: 25, down: 0), blocked)
        XCTAssertEqual(r?.code, "P02")
        XCTAssertTrue(r?.message.hasPrefix("Parça adını, konumunu ve milimetre ölçülerini kontrol edin.") == true)
        for command in [ParsedCommand.addRectangle(width: 10, height: 10), .save, .select(name: "çizgi"), .rename("a")] {
            XCTAssertEqual(refusal(command, blocked)?.code, "P02", "\(command)")
        }
    }

    func testValidPendingFieldsAreAppliedInTheSamePlan() throws {
        let doc = sample()
        var typed = doc
        typed.name = "Yeni ad"
        typed.pieces[0].name = "Gövde"
        let pending = context(doc, base: .some(typed))
        let planned = plan(.move(right: 10, down: 0), pending)
        XCTAssertEqual(planned?.base, typed)
        XCTAssertTrue(planned?.note?.contains("uygulamadığınız değerler de bu işlemle uygulanır") == true)
        XCTAssertTrue(planned?.sentence.hasPrefix("‘Gövde’ 10 mm sağa") == true)
        let (project, _) = try XCTUnwrap(edited(planned))
        // One document carries both the typed name and the moved geometry: one undo step.
        XCTAssertEqual(project.name, "Yeni ad")
        XCTAssertEqual(project.pieces[0].name, "Gövde")
        XCTAssertEqual(project.pieces[0].minX, 10)
    }

    func testPlanRecordsWhatItWasMadeAgainst() {
        let doc = sample()
        let planned = plan(.move(right: 10, down: 0), context(doc))
        XCTAssertEqual(planned?.base, doc)
        XCTAssertEqual(planned?.baseSelection, doc.pieces[0].id)
        var other = doc
        other.pieces[0].points = other.pieces[0].points.map { p($0.x + 1, $0.y) }
        // A changed drawing gives another base, which makes the bar refuse to apply the old plan.
        XCTAssertNotEqual(plan(.move(right: 10, down: 0), context(other))?.base, planned?.base)
    }

    func testEveryPlanResultIsStorable() throws {
        let doc = sample()
        let commands: [ParsedCommand] = [
            .addRectangle(width: 1, height: 1), .addRectangle(width: 20_000, height: 20_000),
            .addLine(length: 1, vertical: true), .move(right: 20_000, down: -20_000),
            .rotate(degrees: 270, clockwise: false), .resize(width: 1, height: 1), .resize(width: 20_000, height: 20_000),
            .rename(String(repeating: "k", count: 128)), .duplicate
        ]
        for command in commands {
            switch CommandPlanner.plan(command, context: context(doc)) {
            case .plan(let planned):
                if case .edit(let project, _) = planned.effect {
                    XCTAssertTrue(CADLocalDraftStore.isValid(project), "\(command)")
                }
            case .refusal:
                break
            }
        }
    }
}

private extension DraftProject {
    /// The same drawing with another one's identifiers, for comparing pieces by content.
    func withIDs(of other: DraftProject) -> DraftProject {
        var copy = self
        copy.id = other.id
        for index in copy.pieces.indices { copy.pieces[index].id = other.pieces[index].id }
        return copy
    }
}

private extension CommandRefusal {
    var sentenceFreeMessage: String { message }
}
