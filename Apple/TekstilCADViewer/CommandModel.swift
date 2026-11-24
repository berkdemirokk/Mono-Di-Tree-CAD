import Foundation

// Types of the command parser. They mirror src/TekstilCAD.Core/Commands/Command.cs and
// ParseResult.cs one to one, so the Swift and .NET parsers answer the shared vectors
// (Apple/Tests/Fixtures/command-vectors.json) with the same values.
// Lengths are millimetres; directions are SCREEN directions (right is +, down is +, so "up" is a
// negative `down`). Both editors use positive Y downwards.

enum PieceTarget: Equatable { case selected, named, next, previous, none }
enum LineOrientation: Equatable { case horizontal, vertical }
enum RotationDirection: Equatable { case clockwise, counterClockwise }
enum MirrorAxis: Equatable { case vertical, horizontal }

enum ViewAction: Equatable {
    case fitAll, fitSelected, zoomIn, zoomOut
    case panLeft, panRight, panUp, panDown
    case detailedView, allTools, textLarger, textSmaller, themeDark, themeLight
}

enum DrawingTool: Equatable { case select, line, polyline, rectangle, bezier, arc }
enum FinishDrawingAction: Equatable { case close, finishOpen, cancel, removeLastPoint }
enum FileAction: Equatable { case save, saveAs, saveCopy, open, new }

enum HandoffTarget: Equatable {
    case importDxf, export, exportSelectedDxf, exportSelectedA4
    case textileTools, machineCenter, grainline, pieceMetadata, mirrorCopy, groupMove
    case duplicate, layers, coordinateDrawing, about, designRequest, recoveryScan
}

/// One understood command. Nothing here knows about a drawing.
enum ParsedCommand: Equatable {
    case measuredApron(width: Double, length: Double)
    case previewDrawing
    case addStudioPattern(StudioPattern)
    case rectangle(width: Double, height: Double, isSquare: Bool)
    case line(length: Double, orientation: LineOrientation)
    case moveBy(right: Double, down: Double)
    case moveTo(x: Double, y: Double)
    case rotate(degrees: Int, direction: RotationDirection)
    case mirror(MirrorAxis)
    case resize(width: Double?, height: Double?)
    case rename(String)
    case delete(PieceTarget, name: String?)
    case select(PieceTarget, name: String?)
    case undo(steps: Int)
    case redo
    case view(ViewAction)
    case tool(DrawingTool)
    case finishDrawing(FinishDrawingAction)
    case file(FileAction)
    case handoff(HandoffTarget, tab: Int?, value: Double?)
    case help
    case cancelPreview
    // iPhone only; the parser never produces these. They carry one piece the user picked from
    // the buttons under an "several pieces matched" question.
    case selectAt(index: Int, name: String)
    case deleteAt(index: Int, name: String)
}

/// A button under a refusal or question. Pressing it only FILLS the command field with `fillText`.
struct RefusalButton: Equatable {
    var label: String
    var fill: String?
    var fillText: String { fill ?? label }

    init(_ label: String, fill: String? = nil) {
        self.label = label
        self.fill = fill
    }
}

/// A refusal or question with its code (R01-R28b), the full Turkish message and its buttons.
struct CommandRefusal: Equatable, Error {
    var code: String
    var message: String
    var buttons: [RefusalButton] = []

    /// iPhone-only codes: "W01" a Windows-only job, "P01" a job the phone does elsewhere,
    /// "P02" typed field values are invalid, "P03" the result would leave the work area.
    var isQuestion: Bool { code == "R06" }
}

enum ParseKind: Equatable { case command, question, refusal }

/// What the parser needs to know about the project: only the piece names, in list order.
struct ParserContext {
    var pieceNames: [String]
    var drawingInProgress = false

    static let empty = ParserContext(pieceNames: [])
}

/// Result of `CommandParser.parse`. Exactly one of `command` and `notice` is set.
struct ParseResult {
    var kind: ParseKind
    var command: ParsedCommand?
    var notice: CommandRefusal?
    /// A length was written without a unit anywhere, so millimetres were assumed.
    var unitAssumed = false
    /// A refusal whose text could still grow into a valid command ("120 x", "dikdört").
    var isIncomplete = false

    var isCommand: Bool { kind == .command }
    var code: String? { notice?.code }

    static func understood(_ command: ParsedCommand) -> ParseResult {
        ParseResult(kind: .command, command: command, notice: nil)
    }

    static func refused(_ notice: CommandRefusal) -> ParseResult {
        ParseResult(kind: notice.isQuestion ? .question : .refusal, command: nil, notice: notice)
    }

    func withUnitAssumed() -> ParseResult {
        guard isCommand, !unitAssumed else { return self }
        var copy = self
        copy.unitAssumed = true
        return copy
    }

    func asIncomplete() -> ParseResult {
        guard kind == .refusal, !isIncomplete else { return self }
        var copy = self
        copy.isIncomplete = true
        return copy
    }
}
