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
    /// Applies a confirmed plan. Returns the result line, or nil when nothing was applied.
    let apply: (CommandPlan) -> String?
    /// Undoes the last applied command.
    let undoLast: () -> Void

    @State private var text = ""
    @State private var card: CardState?
    @State private var result: ResultState?
    @State private var history: [String] = []
    @FocusState private var focused: Bool
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
    }

    struct ResultState {
        var text: String
        var undoable: Bool
    }

    /// While another field has the keyboard, the whole bar steps aside so that field keeps its room.
    private var isShown: Bool { !keyboardVisible || focused }

    var body: some View {
        if isShown {
            VStack(alignment: .leading, spacing: 8) {
                fieldRow
                if let card { cardView(card) }
                if let result { resultRow(result) }
            }
            .padding(.horizontal)
            .padding(.vertical, 8)
            .background(Color.secondary.opacity(0.06))
            .onChange(of: text) { _ in textChanged() }
            Divider()
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
        TextField("Ne yapmak istiyorsunuz? Örnek: 120 x 80 dikdörtgen", text: $text)
            .textFieldStyle(.roundedBorder)
            .font(.callout)
            .focused($focused)
            .submitLabel(.go)
            #if os(iOS)
            .textInputAutocapitalization(.never)
            .autocorrectionDisabled(true)
            #endif
            // Return only prepares the preview; it never applies anything.
            .onSubmit { prepare() }
            .frame(minHeight: 44)
            .accessibilityLabel("Komut kutusu")
            .accessibilityHint("Yapmak istediğinizi yazın ya da klavyedeki mikrofonla söyleyin. Önce ne olacağı gösterilir; Uygula'ya dokunmadan hiçbir şey değişmez.")
            .accessibilityIdentifier("command-field")
    }

    private var prepareButton: some View {
        Button("Hazırla") { prepare() }
            .buttonStyle(.borderedProminent)
            .frame(minHeight: 44)
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
            refusalCard(refusal, isQuestion: isQuestion)
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
                .frame(minHeight: 44)
                .accessibilityIdentifier("command-apply")
        } else {
            Button("Uygula") { confirm(command: command, plan: plan, state: state) }
                .buttonStyle(.borderedProminent)
                .frame(minHeight: 44)
                .accessibilityIdentifier("command-apply")
        }
        Button("Vazgeç") { card = nil }
            .buttonStyle(.bordered)
            .frame(minHeight: 44)
            .accessibilityIdentifier("command-cancel")
    }

    private func helpCard(_ plan: CommandPlan) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Şunları yazabilirsiniz").font(.callout.weight(.semibold))
            ForEach(plan.lines, id: \.self) { line in
                Text(line).font(.caption).fixedSize(horizontal: false, vertical: true)
            }
            Text("Anlaşılmayan yazı hiçbir şeyi değiştirmez.").font(.caption).foregroundStyle(.secondary)
            Button("Anladım") { card = nil }
                .buttonStyle(.bordered)
                .frame(minHeight: 44)
                .accessibilityIdentifier("command-cancel")
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.accentColor.opacity(0.12), in: RoundedRectangle(cornerRadius: 12))
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("command-card")
    }

    private func refusalCard(_ refusal: CommandRefusal, isQuestion: Bool) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(refusal.message)
                .font(.callout)
                .fixedSize(horizontal: false, vertical: true)
            if !refusal.exactChoices.isEmpty {
                ForEach(refusal.exactChoices, id: \.label) { choice in
                    Button(choice.label) { showPreview(for: choice.command, source: text) }
                        .buttonStyle(.bordered)
                        .frame(minHeight: 44)
                }
            } else {
                ForEach(Array(refusal.suggestions.prefix(3).enumerated()), id: \.offset) { _, suggestion in
                    Button(suggestion) { fillAndPrepare(suggestion) }
                        .buttonStyle(.bordered)
                        .frame(minHeight: 44)
                }
            }
            Button("Vazgeç") { card = nil }
                .buttonStyle(.borderless)
                .frame(minHeight: 44)
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
            if state.undoable {
                Button("Komutu geri al") {
                    undoLast()
                    result = ResultState(text: "İşlem geri alındı.", undoable: false)
                }
                .buttonStyle(.bordered)
                .frame(minHeight: 44)
                .accessibilityIdentifier("command-undo")
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 4)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.green.opacity(0.14), in: RoundedRectangle(cornerRadius: 12))
    }

    // MARK: Behaviour

    private func textChanged() {
        // Editing the sentence makes whatever was shown for the old one meaningless.
        if let card, card.source != text { self.card = nil }
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

    /// Return and "Hazırla": show what would happen. Never applies.
    private func prepare() {
        let typed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !typed.isEmpty else { return }
        focused = false
        result = nil
        remember(typed)
        let names = context.document.pieces.map { $0.name }
        switch CommandParser.parse(typed, pieceNames: names) {
        case .command(.cancel):
            card = nil
            text = ""
        case .command(let command):
            showPreview(for: command, source: typed)
        case .ask(let refusal):
            show(.refusal(refusal, isQuestion: true), source: typed)
        case .refuse(let refusal):
            show(.refusal(refusal, isQuestion: false), source: typed)
        }
    }

    private func showPreview(for command: ParsedCommand, source: String, banner: String? = nil) {
        switch CommandPlanner.plan(command, context: context) {
        case .plan(let plan):
            if case .info = plan.effect {
                show(.help(plan), source: source)
            } else {
                show(.preview(command, plan), source: source, banner: banner)
            }
        case .refusal(let refusal):
            show(.refusal(refusal, isQuestion: false), source: source)
        }
    }

    private func show(_ kind: CardState.Kind, source: String, banner: String? = nil) {
        card = CardState(kind: kind, source: source, banner: banner)
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
                show(.preview(command, fresh), source: state.source,
                     banner: "Parça değişti; önizleme yenilendi. Uygula ile onaylayın.")
                return
            }
            if let message = apply(plan) {
                var undoable = false
                if case .edit = plan.effect { undoable = true }
                result = ResultState(text: "Yapıldı: " + message, undoable: undoable)
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
        case .help: message = "Örnek komutlar gösterildi."
        case .refusal(let refusal, _): message = refusal.message
        }
        UIAccessibility.post(notification: .announcement, argument: message)
        #endif
    }
}
