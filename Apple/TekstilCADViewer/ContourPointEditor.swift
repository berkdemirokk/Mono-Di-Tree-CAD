import SwiftUI
#if canImport(UIKit)
import UIKit
#endif

/// Insert and delete rules for the point editor. Pure functions on plain values, so they can be
/// unit-tested without a view. Every result is checked with `DraftContourGeometry.validationError`,
/// so a `.changed` outcome is always a shape the drawing accepts.
enum PointEditing {
    struct Edit: Equatable {
        var points: [DraftPoint]
        /// The point to select after the change.
        var selected: Int
    }

    enum Outcome: Equatable {
        case changed(Edit)
        /// Nothing changed; the text is a plain-language reason for the user.
        case refused(String)
    }

    /// Same rule as `DraftContourGeometry.validationError`: a closed piece needs 3 points, an open
    /// line 2.
    static func minimumCount(closed: Bool) -> Int { closed ? 3 : 2 }

    static func canDelete(count: Int, closed: Bool) -> Bool { count > minimumCount(closed: closed) }

    static func canInsert(count: Int) -> Bool { count < DraftContourGeometry.pointLimit }

    static func minimumReason(closed: Bool) -> String {
        closed
            ? "Kapalı bir parçada en az 3 nokta kalmalı. Bu noktayı silmek için önce yeni bir nokta ekle."
            : "Açık bir çizgide en az 2 nokta kalmalı. Bu noktayı silmek için önce yeni bir nokta ekle."
    }

    static var limitReason: String {
        "Bir parçada en fazla \(DraftContourGeometry.pointLimit) nokta olabilir. Yeni nokta eklemek için önce bir nokta sil."
    }

    /// True when a new point after `index` extends an open line instead of splitting an edge.
    static func extendsLine(after index: Int, count: Int, closed: Bool) -> Bool {
        !closed && index == count - 1
    }

    /// Where a new point after `index` goes: the middle of the edge to the next point. On a closed
    /// piece the point after the last one is the first. The last point of an open line has no next
    /// point, so the line is continued in the direction of its last edge by half that edge's length.
    static func newPoint(after index: Int, in points: [DraftPoint], closed: Bool) -> DraftPoint? {
        guard points.count >= 2, points.indices.contains(index) else { return nil }
        let current = points[index]
        if extendsLine(after: index, count: points.count, closed: closed) {
            let previous = points[index - 1]
            return DraftPoint(x: current.x + (current.x - previous.x) / 2,
                              y: current.y + (current.y - previous.y) / 2)
        }
        let next = points[(index + 1) % points.count]
        return DraftPoint(x: (current.x + next.x) / 2, y: (current.y + next.y) / 2)
    }

    /// Inserts a point right after `index`; the new point is selected.
    static func inserting(after index: Int, in points: [DraftPoint], closed: Bool) -> Outcome {
        guard points.indices.contains(index) else { return .refused("Önce bir nokta seç.") }
        guard canInsert(count: points.count) else { return .refused(limitReason) }
        guard let point = newPoint(after: index, in: points, closed: closed) else {
            return .refused("Yeni noktanın yeri bulunamadı.")
        }
        var updated = points
        updated.insert(point, at: index + 1)
        if let message = DraftContourGeometry.validationError(points: updated, closed: closed) {
            return .refused("Buraya nokta eklenemedi. " + message)
        }
        return .changed(Edit(points: updated, selected: index + 1))
    }

    /// Removes the point at `index`. Afterwards the point that took its place is selected; on a
    /// closed piece that wraps to the first point, on an open line it stops at the new last point.
    static func deleting(at index: Int, in points: [DraftPoint], closed: Bool) -> Outcome {
        guard points.indices.contains(index) else { return .refused("Önce bir nokta seç.") }
        guard canDelete(count: points.count, closed: closed) else {
            return .refused(minimumReason(closed: closed))
        }
        var updated = points
        updated.remove(at: index)
        if let message = DraftContourGeometry.validationError(points: updated, closed: closed) {
            return .refused("Bu nokta silinemedi. " + message)
        }
        let selected = closed ? index % updated.count : min(index, updated.count - 1)
        return .changed(Edit(points: updated, selected: selected))
    }
}

/// Moves, adds and deletes the corner points of one shape, one point at a time.
///
/// The sheet stays open while points are changed: every change is reported through `onSave` straight
/// away, so fixing four corners is one visit, not four. Typed X/Y values are never dropped silently:
/// switching to another point, adding a point or tapping "Bitti" applies them first, and if they
/// cannot be used the sheet stays on that point and says why. The caller's `onSave` / init contract
/// is unchanged.
struct ContourPointEditor: View {
    @Environment(\.dismiss) private var dismiss
    /// Returns nil when the change was kept, otherwise the reason it was not.
    let onSave: (DraftPiece) -> String?

    // The sheet is handed a snapshot of the shape. Because the sheet does not close after each
    // change, it keeps its own working copy so the next edit is made on top of the last one.
    @State private var working: DraftPiece
    @State private var selected = 0
    @State private var x: String
    @State private var y: String
    @State private var problem: String?
    @State private var status: String?

    private static let fixHint = "Değeri düzelt ya da “Yazdığımı geri al” düğmesine bas."

    init(piece: DraftPiece, onSave: @escaping (DraftPiece) -> String?) {
        self.onSave = onSave
        _working = State(initialValue: piece)
        _x = State(initialValue: piece.points.first.map { DraftPiece.display($0.x) } ?? "")
        _y = State(initialValue: piece.points.first.map { DraftPiece.display($0.y) } ?? "")
    }

    /// Shows `index` in the fields. Callers decide first what happens to typed text.
    private func select(_ index: Int) {
        guard working.points.indices.contains(index) else { return }
        selected = index
        x = DraftPiece.display(working.points[index].x)
        y = DraftPiece.display(working.points[index].y)
        problem = nil
        status = nil
    }

    // The fields show values rounded to three decimals. Text the user did not change must keep the
    // exact original coordinate (same idea as DraftPiece.measurement).
    private func resolved(_ text: String, original: Double) -> Double? {
        text == DraftPiece.display(original) ? original : DraftContourGeometry.coordinate(text)
    }

    /// True when the fields hold something that has not been applied to the selected point yet.
    private var isDirty: Bool {
        guard working.points.indices.contains(selected) else { return false }
        let point = working.points[selected]
        return x != DraftPiece.display(point.x) || y != DraftPiece.display(point.y)
    }

    // MARK: - Status and announcements

    /// Shows a result under the fields and reads it out with VoiceOver.
    private func report(_ message: String) {
        status = message
        announce(message)
    }

    /// Shows why nothing changed. VoiceOver reads the alert when it appears, so no separate
    /// announcement is posted (it would be heard twice). The status line keeps the reason visible
    /// after the alert is closed.
    private func refuse(_ reason: String) {
        status = reason
        problem = reason
    }

    private func announce(_ message: String) {
        #if os(iOS)
        // The workshop also announces a short notice for each saved change, on its next update.
        // Posting a moment later and queued keeps this more specific message from being cut off.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
            let text = NSAttributedString(string: message,
                                          attributes: [.accessibilitySpeechQueueAnnouncement: true])
            UIAccessibility.post(notification: .announcement, argument: text)
        }
        #endif
    }

    // MARK: - Changes

    /// What happened to the values typed for the selected point.
    private enum Pending {
        /// Nothing typed, or the typed numbers are the current position.
        case unchanged
        /// The typed position was saved.
        case applied
        /// The typed text could not be used; the reason is shown and the text stays in the fields.
        case refused
    }

    /// Saves the typed X/Y of the selected point through `onSave`. `context` is added to a refusal
    /// to say what did not happen because of it (for example that the sheet stayed open).
    private func takePending(context: String = "") -> Pending {
        guard working.points.indices.contains(selected) else { return .unchanged }
        let current = working.points[selected]
        let number = selected + 1
        let suffix = context.isEmpty ? "" : " " + context
        guard let newX = resolved(x, original: current.x),
              let newY = resolved(y, original: current.y) else {
            refuse("Nokta \(number) için yazdığın X veya Y değeri kullanılamıyor. Milimetre olarak bir sayı yaz, örneğin 12,5." + suffix)
            return .refused
        }
        let moved = DraftPoint(x: newX, y: newY)
        guard moved != current else { return .unchanged }
        var updated = working
        updated.points[selected] = moved
        if let message = DraftContourGeometry.validationError(points: updated.points, closed: updated.closed) {
            refuse("Nokta \(number) bu konuma taşınamadı. " + message + suffix)
            return .refused
        }
        // Keep the change first; the local copy only follows when the drawing accepted it.
        if let reason = onSave(updated) {
            refuse(reason + suffix)
            return .refused
        }
        working = updated
        return .applied
    }

    // After an apply the selection moves on to the next point: corners are usually corrected one
    // after another around the shape, so the next field set is already waiting. The first press is
    // never a surprise because the status line names both points; the last point stays selected
    // (no wrap-around) so the sequence has a visible end. Tapping a corner or the list goes back.
    private func applyAndAdvance() {
        let number = selected + 1
        let outcome = takePending()
        guard outcome != .refused else { return }
        let result = outcome == .applied ? "uygulandı" : "değişmedi"
        if selected < working.points.count - 1 {
            select(selected + 1)
            report("Nokta \(number) \(result). Şimdi Nokta \(number + 1) seçili.")
        } else {
            select(selected)
            report("Nokta \(number) \(result).")
        }
    }

    /// Moves to another point. Typed values for the current point are applied first; if they cannot
    /// be used, the selection stays where it is.
    private func goTo(_ index: Int) {
        guard index != selected, working.points.indices.contains(index) else { return }
        let number = selected + 1
        switch takePending(context: "Nokta \(number) seçili kaldı. " + Self.fixHint) {
        case .refused:
            return
        case .applied:
            select(index)
            report("Nokta \(number) için yazdığın değer uygulandı. Şimdi Nokta \(index + 1) seçili.")
        case .unchanged:
            select(index)
            status = "Nokta \(index + 1) seçili."
        }
    }

    private func addPoint() {
        let number = selected + 1
        let pending = takePending(context: "Nokta eklenmedi. " + Self.fixHint)
        guard pending != .refused else { return }
        let applied = pending == .applied ? "Nokta \(number) için yazdığın değer uygulandı. " : ""
        switch PointEditing.inserting(after: selected, in: working.points, closed: working.closed) {
        case .refused(let reason):
            refuse(applied + reason)
        case .changed(let edit):
            var updated = working
            updated.points = edit.points
            if let reason = onSave(updated) {
                refuse(applied + "Nokta eklenmedi. " + reason)
                return
            }
            working = updated
            select(edit.selected)
            report(applied + "Nokta \(edit.selected + 1) eklendi ve seçildi. Parçada artık \(edit.points.count) nokta var.")
        }
    }

    // Deleting needs no confirmation: the drawing's "Geri al" brings the point back, and the caption
    // under the button says so.
    private func deletePoint() {
        let number = selected + 1
        let hadPending = isDirty
        switch PointEditing.deleting(at: selected, in: working.points, closed: working.closed) {
        case .refused(let reason):
            // Typed text stays in the fields.
            refuse(reason)
        case .changed(let edit):
            var updated = working
            updated.points = edit.points
            if let reason = onSave(updated) {
                refuse("Nokta \(number) silinmedi. " + reason)
                return
            }
            working = updated
            select(edit.selected)
            var message = "Nokta \(number) silindi. Parçada artık \(edit.points.count) nokta var. Şimdi Nokta \(edit.selected + 1) seçili."
            if hadPending { message += " Bu nokta için yazdığın ama uygulamadığın değer de silindi." }
            report(message)
        }
    }

    private func revertTyped() {
        let number = selected + 1
        select(selected)
        report("Yazdığın değer geri alındı. Nokta \(number) eski konumunda.")
    }

    // "Bitti" also takes what was typed but not yet applied; if that is invalid the sheet stays open.
    private func finish() {
        let number = selected + 1
        switch takePending(context: "Sayfa açık kaldı. " + Self.fixHint) {
        case .refused:
            return
        case .applied:
            announce("Nokta \(number) için yazdığın değer uygulandı.")
        case .unchanged:
            break
        }
        dismiss()
    }

    // MARK: - Texts

    private var insertExplanation: String {
        let count = working.points.count
        guard working.points.indices.contains(selected) else { return "" }
        guard PointEditing.canInsert(count: count) else { return PointEditing.limitReason }
        if PointEditing.extendsLine(after: selected, count: count, closed: working.closed) {
            return "Yeni nokta çizginin sonuna, son kenarın yönünde eklenir."
        }
        let next = (selected + 1) % count + 1
        return "Yeni nokta, Nokta \(selected + 1) ve Nokta \(next) arasına, tam ortaya eklenir."
    }

    private var deleteExplanation: String {
        PointEditing.canDelete(count: working.points.count, closed: working.closed)
            ? "Silinen noktayı, Bitti'ye bastıktan sonra çizimdeki Geri al ile geri getirebilirsin."
            : PointEditing.minimumReason(closed: working.closed)
    }

    // MARK: - Views

    private var preview: some View {
        GeometryReader { geometry in
            if let map = DraftCanvasMap(size: geometry.size, minX: working.minX, maxX: working.maxX,
                                        minY: working.minY, maxY: working.maxY, inset: 22) {
                Canvas { context, _ in
                    var path = Path()
                    for (index, point) in working.points.enumerated() {
                        let position = map.screen(point)
                        if index == 0 { path.move(to: position) } else { path.addLine(to: position) }
                    }
                    if working.closed { path.closeSubpath() }
                    context.stroke(path, with: .color(AppTheme.muted), lineWidth: 2)
                    for (index, point) in working.points.enumerated() {
                        let position = map.screen(point)
                        let radius: CGFloat = index == selected ? 7 : 4
                        let marker = CGRect(x: position.x - radius, y: position.y - radius,
                                            width: 2 * radius, height: 2 * radius)
                        context.fill(Path(ellipseIn: marker),
                                     with: .color(index == selected ? Color.accentColor : AppTheme.muted))
                    }
                }
                .contentShape(Rectangle())
                .gesture(SpatialTapGesture().onEnded { tap in
                    if let index = map.nearestVertex(to: tap.location, points: working.points) { goTo(index) }
                })
                .accessibilityHidden(true)
            }
        }
        .frame(height: 190)
        .background(Color.secondary.opacity(0.08), in: RoundedRectangle(cornerRadius: 14))
    }

    private func coordinateField(_ axis: String, text: Binding<String>) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("\(axis) (mm)")
                .font(.subheadline)
                .foregroundStyle(AppTheme.muted)
                .accessibilityHidden(true)
            TextField(axis, text: text)
                .textFieldStyle(.roundedBorder)
                .frame(minHeight: 44)
                .submitLabel(.done)
                #if os(iOS)
                // Digits, minus, comma and point without switching keyboards.
                .keyboardType(.numbersAndPunctuation)
                #endif
                .accessibilityLabel("Nokta \(selected + 1) \(axis) koordinatı, milimetre")
        }
    }

    private var previousButton: some View {
        Button {
            goTo(selected - 1)
        } label: {
            Label("Önceki nokta", systemImage: "chevron.left")
                .frame(maxWidth: .infinity, minHeight: 44)
        }
        .disabled(selected == 0)
    }

    private var nextButton: some View {
        Button {
            goTo(selected + 1)
        } label: {
            Label("Sonraki nokta", systemImage: "chevron.right")
                .frame(maxWidth: .infinity, minHeight: 44)
        }
        .disabled(selected >= working.points.count - 1)
    }

    private var addButton: some View {
        Button(action: addPoint) {
            Label("Nokta ekle", systemImage: "plus.circle")
                .frame(maxWidth: .infinity, minHeight: 44)
        }
        .accessibilityHint(insertExplanation)
    }

    private var deleteButton: some View {
        Button(role: .destructive, action: deletePoint) {
            Label("Noktayı sil", systemImage: "minus.circle")
                .frame(maxWidth: .infinity, minHeight: 44)
        }
        .accessibilityHint(deleteExplanation)
    }

    @ViewBuilder
    private var pendingNotice: some View {
        if isDirty {
            VStack(alignment: .leading, spacing: 8) {
                Label("Yazdığın değer henüz uygulanmadı. Başka bir noktaya geçince, nokta ekleyince ya da Bitti'ye basınca kendiliğinden uygulanır.",
                      systemImage: "pencil.circle")
                    .font(.subheadline)
                    .foregroundStyle(AppTheme.ink)
                    .fixedSize(horizontal: false, vertical: true)
                    .accessibilityIdentifier("point-pending")
                Button("Yazdığımı geri al", action: revertTyped)
                    .buttonStyle(.appSecondary)
                    .accessibilityHint("Kutulara Nokta \(selected + 1) için kayıtlı değerleri geri yazar.")
            }
        }
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    Text(working.name).font(.headline)
                    Text("Bir köşeye dokun ya da listeden nokta seç, yeni konumu milimetre olarak yaz. Uygula'ya, başka bir noktaya ya da Bitti'ye basınca kaydedilir.")
                        .font(.subheadline)
                        .foregroundStyle(AppTheme.muted)
                        .fixedSize(horizontal: false, vertical: true)
                    preview
                    Text("Bu parçada \(working.points.count) nokta var.")
                        .font(.subheadline)
                        .foregroundStyle(AppTheme.ink)
                        .accessibilityIdentifier("point-count")
                    Picker("Nokta", selection: Binding(get: { selected }, set: { goTo($0) })) {
                        ForEach(working.points.indices, id: \.self) { index in
                            Text("Nokta \(index + 1)").tag(index)
                        }
                    }
                    .pickerStyle(.menu)
                    .frame(minHeight: 44)
                    ViewThatFits(in: .horizontal) {
                        HStack {
                            previousButton
                            nextButton
                        }
                        VStack(alignment: .leading) {
                            previousButton
                            nextButton
                        }
                    }
                    .buttonStyle(.appSecondary)
                    coordinateField("X", text: $x)
                    coordinateField("Y", text: $y)
                    pendingNotice
                    if let status = status {
                        Text(status)
                            .font(.subheadline)
                            .foregroundStyle(AppTheme.muted)
                            .fixedSize(horizontal: false, vertical: true)
                            .accessibilityIdentifier("point-status")
                    }
                    Button(action: applyAndAdvance) {
                        Text("Nokta konumunu uygula").frame(maxWidth: .infinity, minHeight: 44)
                    }
                    .buttonStyle(.appPrimary)
                    ViewThatFits(in: .horizontal) {
                        HStack {
                            addButton
                            deleteButton
                        }
                        VStack(alignment: .leading) {
                            addButton
                            deleteButton
                        }
                    }
                    .buttonStyle(.appSecondary)
                    Text(insertExplanation + " " + deleteExplanation)
                        .font(.caption)
                        .foregroundStyle(AppTheme.muted)
                        .fixedSize(horizontal: false, vertical: true)
                        // The same texts are the hints of the two buttons.
                        .accessibilityHidden(true)
                }
                .padding()
            }
            .accessibilityIdentifier("point-editor")
            .navigationTitle("Köşe noktaları")
            .toolbar {
                // Changes are already saved, so there is nothing left to cancel.
                ToolbarItem(placement: .confirmationAction) { Button("Bitti") { finish() } }
            }
        }
        // Do not lose typed-but-unapplied numbers to an accidental swipe-down.
        .interactiveDismissDisabled(isDirty)
        .alert("Bu değişiklik yapılamadı", isPresented: Binding(
            get: { problem != nil }, set: { if !$0 { problem = nil } }
        )) {
            Button("Tamam", role: .cancel) { problem = nil }
        } message: {
            Text(problem ?? "")
        }
    }
}
