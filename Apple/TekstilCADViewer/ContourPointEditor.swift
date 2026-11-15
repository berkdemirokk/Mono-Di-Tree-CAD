import SwiftUI

/// Moves the corner points of one shape, one point at a time.
///
/// The sheet stays open while points are applied: every "Nokta konumunu uygula" press reports the
/// edited shape through `onSave` straight away, so fixing four corners is one visit, not four.
/// The caller's `onSave` / init contract is unchanged.
struct ContourPointEditor: View {
    @Environment(\.dismiss) private var dismiss
    /// Returns nil when the change was kept, otherwise the reason it was not.
    let onSave: (DraftPiece) -> String?

    // The sheet is handed a snapshot of the shape. Because the sheet no longer closes after each
    // apply, it keeps its own working copy so the next point is edited on top of the last one.
    @State private var working: DraftPiece
    @State private var selected = 0
    @State private var x: String
    @State private var y: String
    @State private var problem: String?
    @State private var status: String?

    init(piece: DraftPiece, onSave: @escaping (DraftPiece) -> String?) {
        self.onSave = onSave
        _working = State(initialValue: piece)
        _x = State(initialValue: piece.points.first.map { DraftPiece.display($0.x) } ?? "")
        _y = State(initialValue: piece.points.first.map { DraftPiece.display($0.y) } ?? "")
    }

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

    // After an apply the selection moves on to the next point: corners are usually corrected one
    // after another around the shape, so the next field set is already waiting. The first press is
    // never a surprise because the status line names both points; the last point stays selected
    // (no wrap-around) so the sequence has a visible end. Tapping a corner or the list goes back.
    @discardableResult
    private func apply(advance: Bool) -> Bool {
        guard working.points.indices.contains(selected) else { return false }
        let current = working.points[selected]
        guard let newX = resolved(x, original: current.x),
              let newY = resolved(y, original: current.y) else {
            problem = "X ve Y için geçerli milimetre değerleri girin."
            return false
        }
        let moved = DraftPoint(x: newX, y: newY)
        let number = selected + 1
        if moved != current {
            var updated = working
            updated.points[selected] = moved
            if let message = DraftContourGeometry.validationError(points: updated.points, closed: updated.closed) {
                problem = message
                return false
            }
            // Keep the change first; the local copy only follows when the drawing accepted it.
            if let reason = onSave(updated) {
                problem = reason
                return false
            }
            working = updated
        }
        let result = moved != current ? "uygulandı" : "değişmedi"
        if advance && selected < working.points.count - 1 {
            select(selected + 1)
            status = "Nokta \(number) \(result). Şimdi Nokta \(number + 1) seçili."
        } else {
            select(selected)
            status = "Nokta \(number) \(result)."
        }
        return true
    }

    // "Bitti" also takes what was typed but not yet applied; if that is invalid the sheet stays open.
    private func finish() {
        if isDirty && !apply(advance: false) { return }
        dismiss()
    }

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
                    if let index = map.nearestVertex(to: tap.location, points: working.points) { select(index) }
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

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    Text(working.name).font(.headline)
                    Text("Bir köşeye dokunun veya listeden nokta seçin. Yeni konumu milimetre olarak girin; uyguladığınızda sıradaki köşe seçilir.")
                        .font(.subheadline)
                        .foregroundStyle(AppTheme.muted)
                    preview
                    Picker("Nokta", selection: Binding(get: { selected }, set: { select($0) })) {
                        ForEach(working.points.indices, id: \.self) { index in
                            Text("Nokta \(index + 1)").tag(index)
                        }
                    }
                    .pickerStyle(.menu)
                    .frame(minHeight: 44)
                    coordinateField("X", text: $x)
                    coordinateField("Y", text: $y)
                    if let status = status {
                        Text(status)
                            .font(.subheadline)
                            .foregroundStyle(AppTheme.muted)
                    }
                    Button(action: { _ = apply(advance: true) }) {
                        Text("Nokta konumunu uygula").frame(maxWidth: .infinity, minHeight: 44)
                    }
                    .buttonStyle(.appPrimary)
                }
                .padding()
            }
            .navigationTitle("Köşe noktaları")
            .toolbar {
                // Applied points are already saved, so there is nothing left to cancel.
                ToolbarItem(placement: .confirmationAction) { Button("Bitti") { finish() } }
            }
        }
        // Do not lose typed-but-unapplied numbers to an accidental swipe-down.
        .interactiveDismissDisabled(isDirty)
        .alert("Nokta değiştirilemedi", isPresented: Binding(
            get: { problem != nil }, set: { if !$0 { problem = nil } }
        )) {
            Button("Tamam", role: .cancel) { problem = nil }
        } message: {
            Text(problem ?? "")
        }
    }
}
