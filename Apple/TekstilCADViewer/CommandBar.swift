import SwiftUI
#if canImport(UIKit)
import UIKit
#endif

/// The one command field of the workshop: type (or dictate with the keyboard's microphone) a
/// Turkish sentence, read ONE line that says what will happen, then confirm or cancel.
///
/// Nothing is applied by the first Return or by dictation: "Hazırla" (or Return) only prepares a
/// preview, and only "Uygula" changes the drawing. The bar holds no drawing state; it asks the
/// workshop for a context, plans against it, and hands a confirmed plan back to the workshop,
/// which applies it through its normal write path.
struct CommandBar: View {
    /// The drawing as the commands see it. Recomputed by the workshop on every change.
    let context: CommandContext
    let keyboardVisible: Bool
    /// True while a card or a result line is showing; the workshop gives the room to it at very
    /// large text sizes by hiding the drawing.
    @Binding var hasOutput: Bool
    /// Applies a confirmed plan. Returns the result line, or nil when nothing was applied.
    let apply: (CommandPlan) -> String?
    /// Undoes the last applied command.
    let undoLast: () -> Void

    @State private var text = ""
    @State private var card: CardState?
    @State private var result: ResultState?
    @State private var history: [String] = []
    @FocusState private var focused: Bool
    /// Keeps the bar (and the card just made) in place while the keyboard finishes closing.
    @State private var holdOpen = false
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    struct CardState {
        enum Kind {
            case preview(ParsedCommand, CommandPlan)
            case refusal(CommandRefusal, isQuestion: Bool)
            case help(CommandPlan)
        }
        var kind: Kind
        var source: String
        var banner: String?
        var unitAssumed = false
    }

    struct ResultState {
        var text: String
        var undoable: Bool
        /// The undo count the workshop has right after this command; the undo chip is only
        /// offered while it is still the last thing done.
        var expectedUndoCount = 0
    }

    /// While another field has the keyboard, the whole bar steps aside so that field keeps its room.
    private var isShown: Bool { !keyboardVisible || focused || holdOpen }

    private var showsOutput: Bool { card != nil || result != nil }

    var body: some View {
        content
            .onChange(of: keyboardVisible) { visible in
                if !visible { holdOpen = false }
            }
            .onChange(of: showsOutput) { shown in hasOutput = shown }
    }

    @ViewBuilder
    private var content: some View {
        if isShown {
            VStack(alignment: .leading, spacing: 8) {
                fieldRow
                if dynamicTypeSize.isAccessibilitySize {
                    // At very large text a card is taller than the screen: it scrolls inside the bar
                    // instead of pushing the bar under the navigation bar.
                    ScrollView { outputs }
                } else {
                    outputs
                }
            }
            .padding(.horizontal)
            .padding(.vertical, 8)
            .background(Color.secondary.opacity(0.06))
            .onChange(of: text) { _ in textChanged() }
            Divider()
        }
    }

    @ViewBuilder
    private var outputs: some View {
        VStack(alignment: .leading, spacing: 8) {
            if let card { cardView(card) }
            if let result { resultRow(result) }
        }
    }

    // MARK: Field

    private var hasText: Bool { !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    @ViewBuilder
    private var fieldRow: some View {
        if dynamicTypeSize.isAccessibilitySize {
            VStack(alignment: .leading, spacing: 8) {
                field
                HStack(spacing: 8) {
                    if hasText { prepareButton }
                    historyMenu
                }
            }
        } else {
            HStack(spacing: 8) {
                field
                if hasText { prepareButton }
                historyMenu
            }
        }
    }

    private var field: some View {
        TextField(awaitingConfirmation ? "Onaylamak için uygula yazın" : "Ne yapmak istiyorsunuz? Örnek: 120 x 80 dikdörtgen", text: $text)
            .textFieldStyle(.plain)
            .font(.callout)
            .padding(.horizontal, 10)
            .frame(minHeight: 44)
            .background(RoundedRectangle(cornerRadius: 8).fill(Color.primary.opacity(0.06)))
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.secondary.opacity(0.5), lineWidth: 1))
            .focused($focused)
            .submitLabel(.go)
            #if os(iOS)
            .textInputAutocapitalization(.never)
            .autocorrectionDisabled(true)
            #endif
            // Submitting an action prepares it. A separate typed confirmation applies the shown plan.
            .onSubmit { prepare() }
            .accessibilityLabel("Komut kutusu")
            .accessibilityHint("Yapmak istediğinizi yazın ya da klavyedeki mikrofonla söyleyin. Önizlemeyi onaylamak için uygula yazıp gönderin veya Uygula düğmesine dokunun.")
            .accessibilityIdentifier("command-field")
    }

    private var prepareButton: some View {
        Button("Hazırla") { prepare() }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .accessibilityIdentifier("command-prepare")
    }

    @ViewBuilder
    private var historyMenu: some View {
        if !history.isEmpty {
            Menu {
                ForEach(history, id: \.self) { entry in
                    Button(entry) { text = entry }
                }
            } label: {
                Image(systemName: "clock.arrow.circlepath")
                    .frame(minWidth: 44, minHeight: 44)
            }
            .accessibilityLabel("Son komutlar")
            .accessibilityHint("Daha önce yazdığınız komutlardan birini kutuya yazar. Uygulamaz.")
            .accessibilityIdentifier("command-history")
        }
    }

    // MARK: Cards

    @ViewBuilder
    private func cardView(_ state: CardState) -> some View {
        switch state.kind {
        case .preview(let command, let plan):
            previewCard(command: command, plan: plan, state: state)
        case .help(let plan):
            helpCard(plan)
        case .refusal(let refusal, let isQuestion):
            refusalCard(refusal, isQuestion: isQuestion, source: state.source)
        }
    }

    private func previewCard(command: ParsedCommand, plan: CommandPlan, state: CardState) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            if let banner = state.banner {
                Text(banner).font(.caption.weight(.semibold))
                    .fixedSize(horizontal: false, vertical: true)
            }
            Text(plan.sentence)
                .font(.callout)
                .fixedSize(horizontal: false, vertical: true)
            if let model = plan.patternPreview {
                GarmentTracePreview(project: model)
            }
            if let note = plan.note, !note.isEmpty {
                Text(note).font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 8) { confirmButtons(command: command, plan: plan, state: state) }
                VStack(alignment: .leading, spacing: 8) { confirmButtons(command: command, plan: plan, state: state) }
            }
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background((plan.isDestructive ? Color.red : Color.accentColor).opacity(0.12),
                    in: RoundedRectangle(cornerRadius: 12))
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("command-card")
    }

    @ViewBuilder
    private func confirmButtons(command: ParsedCommand, plan: CommandPlan, state: CardState) -> some View {
        if plan.isDestructive {
            Button("Evet, parçayı sil", role: .destructive) { confirm(command: command, plan: plan, state: state) }
                .buttonStyle(.borderedProminent)
                .tint(.red)
                .controlSize(.large)
                .accessibilityIdentifier("command-apply")
        } else {
            Button("Uygula") { confirm(command: command, plan: plan, state: state) }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .accessibilityIdentifier("command-apply")
        }
        Button("Vazgeç") { text = state.source; card = nil }
            .buttonStyle(.bordered)
            .controlSize(.large)
            .accessibilityIdentifier("command-cancel")
    }

    private func helpCard(_ plan: CommandPlan) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            if let project = plan.patternPreview {
                Text(plan.sentence).font(.callout.weight(.semibold))
                GarmentTracePreview(project: project)
            } else {
                Text("Şunları yazabilirsiniz").font(.callout.weight(.semibold))
            }
            ForEach(plan.lines, id: \.self) { line in
                Text(line).font(.caption).fixedSize(horizontal: false, vertical: true)
            }
            if plan.patternPreview == nil {
                Text("Anlaşılmayan yazı hiçbir şeyi değiştirmez.").font(.caption).foregroundStyle(.secondary)
            }
            Button("Anladım") { card = nil }
                .buttonStyle(.bordered)
                .controlSize(.large)
                .accessibilityIdentifier("command-cancel")
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.accentColor.opacity(0.12), in: RoundedRectangle(cornerRadius: 12))
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("command-card")
    }

    private func refusalCard(_ refusal: CommandRefusal, isQuestion: Bool, source: String) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(refusal.message)
                .font(.callout)
                .fixedSize(horizontal: false, vertical: true)
            if refusal.code == "R15b" {
                // Several pieces matched: the buttons are their names; one pick shows its preview.
                ForEach(Array(refusal.buttons.prefix(3).enumerated()), id: \.offset) { _, button in
                    Button(button.label) { chooseExactPiece(named: button.label, source: source) }
                        .buttonStyle(.bordered)
                        .controlSize(.large)
                }
            } else {
                ForEach(Array(refusal.buttons.prefix(3).enumerated()), id: \.offset) { _, button in
                    Button(button.label) { fillAndPrepare(button.fillText) }
                        .buttonStyle(.bordered)
                        .controlSize(.large)
                }
            }
            Button("Vazgeç") { text = state.source; card = nil }
                .buttonStyle(.borderless)
                .controlSize(.large)
                .accessibilityIdentifier("command-cancel")
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.orange.opacity(0.15), in: RoundedRectangle(cornerRadius: 12))
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("command-card")
    }

    private func resultRow(_ state: ResultState) -> some View {
        HStack(alignment: .center, spacing: 8) {
            Text(state.text)
                .font(.footnote)
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityIdentifier("command-result")
            Spacer(minLength: 0)
            if state.undoable && context.undoCount == state.expectedUndoCount {
                Button("Komutu geri al") {
                    undoLast()
                    result = ResultState(text: "İşlem geri alındı.", undoable: false)
                }
                .buttonStyle(.bordered)
                .controlSize(.large)
                .accessibilityIdentifier("command-undo")
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 4)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.green.opacity(0.14), in: RoundedRectangle(cornerRadius: 12))
    }

    // MARK: Behaviour

    private var awaitingConfirmation: Bool {
        guard let card, case .preview = card.kind else { return false }
        return true
    }

    private func textChanged() {
        // Editing the sentence makes whatever was shown for the old one meaningless.
        let typed = TurkishText.fold(text.trimmingCharacters(in: .whitespacesAndNewlines))
        let enteringConfirmation = awaitingConfirmation && "uygula".hasPrefix(typed)
        if let card, card.source != text, !enteringConfirmation { self.card = nil }
        if !text.isEmpty { result = nil }
    }

    private func remember(_ typed: String) {
        history.removeAll { $0 == typed }
        history.insert(typed, at: 0)
        if history.count > 20 { history.removeLast(history.count - 20) }
    }

    private func fillAndPrepare(_ sentence: String) {
        text = sentence
        prepare()
    }

    /// Prepare a new command or confirm the pending preview with an explicit follow-up.
    private func prepare() {
        let typed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !typed.isEmpty else { return }
        holdOpen = keyboardVisible
        focused = false
        result = nil
        if TurkishText.fold(typed) == "uygula", let card,
           case .preview(let command, let plan) = card.kind {
            confirm(command: command, plan: plan, state: card)
            return
        }
        remember(typed)
        // Names as the planner will see them (typed field values applied), so "kol seç" matches.
        let names = (context.base ?? context.document).pieces.map { $0.name }
        let parsed = CommandParser.parse(typed, context: ParserContext(pieceNames: names))
        if let command = parsed.command {
            if command == .cancelPreview {
                card = nil
                text = ""
            } else {
                showPreview(for: command, source: typed, unitAssumed: parsed.unitAssumed)
            }
        } else if let notice = parsed.notice {
            show(.refusal(notice, isQuestion: parsed.kind == .question), source: typed)
        }
    }

    /// One of the pieces named under a "several pieces matched" question: select or delete that
    /// exact piece (the sentence said which) and show its preview.
    private func chooseExactPiece(named name: String, source: String) {
        let pieces = (context.base ?? context.document).pieces
        guard let index = pieces.firstIndex(where: { $0.name == name }) else {
            show(.refusal(CommandRefusal(code: "R22", message: "Parçalar değişti; komutu yeniden yazın."), isQuestion: false),
                 source: source)
            return
        }
        let words = TurkishText.fold(source).split(separator: " ").map(String.init)
        let deleting = words.contains("sil") || words.contains("silin") || words.contains("kaldir")
        let command: ParsedCommand = deleting ? .deleteAt(index: index, name: name) : .selectAt(index: index, name: name)
        showPreview(for: command, source: source)
    }

    private func showPreview(for command: ParsedCommand, source: String, banner: String? = nil,
                             unitAssumed: Bool = false) {
        switch CommandPlanner.plan(command, context: context) {
        case .plan(var plan):
            if case .info = plan.effect {
                show(.help(plan), source: source)
            } else {
                if unitAssumed {
                    plan.note = [plan.note, "Birim yazmadınız; milimetre sayıldı."].compactMap { $0 }.joined(separator: " ")
                }
                show(.preview(command, plan), source: source, banner: banner, unitAssumed: unitAssumed)
                text = ""
            }
        case .refusal(let refusal):
            show(.refusal(refusal, isQuestion: false), source: source)
        }
    }

    private func show(_ kind: CardState.Kind, source: String, banner: String? = nil, unitAssumed: Bool = false) {
        card = CardState(kind: kind, source: source, banner: banner, unitAssumed: unitAssumed)
        announce(for: kind)
    }

    /// Applies the shown plan when the drawing is still the one it was made for; otherwise shows
    /// the plan again for the new drawing and waits for another confirmation.
    private func confirm(command: ParsedCommand, plan: CommandPlan, state: CardState) {
        switch CommandPlanner.plan(command, context: context) {
        case .refusal(let refusal):
            show(.refusal(refusal, isQuestion: false), source: state.source)
        case .plan(let fresh):
            let unchanged = fresh.base == plan.base && fresh.baseSelection == plan.baseSelection
                && fresh.sentence == plan.sentence
            guard unchanged else {
                showPreview(for: command, source: state.source,
                            banner: "Parça değişti; önizleme yenilendi. Uygula ile onaylayın.",
                            unitAssumed: state.unitAssumed)
                return
            }
            if let message = apply(plan) {
                var undoable = false
                if case .edit = plan.effect { undoable = true }
                result = ResultState(text: "Yapıldı: " + message, undoable: undoable,
                                     expectedUndoCount: context.undoCount + 1)
                card = nil
                text = ""
            } else {
                show(.refusal(CommandRefusal(code: "R28a",
                                             message: "İşlem tamamlanamadı. Hiçbir şey değişmedi."),
                              isQuestion: false), source: state.source)
            }
        }
    }

    private func announce(for kind: CardState.Kind) {
        #if canImport(UIKit)
        let message: String
        switch kind {
        case .preview(_, let plan): message = plan.sentence
        case .help(let plan): message = plan.sentence
        case .refusal(let refusal, _): message = refusal.message
        }
        UIAccessibility.post(notification: .announcement, argument: message)
        #endif
    }
}
