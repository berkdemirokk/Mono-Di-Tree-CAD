import SwiftUI

struct ContourPointEditor: View {
    @Environment(\.dismiss) private var dismiss
    let piece: DraftPiece
    let onSave: (DraftPiece) -> Void

    @State private var selected = 0
    @State private var x: String
    @State private var y: String
    @State private var problem: String?

    init(piece: DraftPiece, onSave: @escaping (DraftPiece) -> Void) {
        self.piece = piece
        self.onSave = onSave
        _x = State(initialValue: piece.points.first.map { String($0.x) } ?? "")
        _y = State(initialValue: piece.points.first.map { String($0.y) } ?? "")
    }

    private func select(_ index: Int) {
        guard piece.points.indices.contains(index) else { return }
        selected = index
        x = String(piece.points[index].x)
        y = String(piece.points[index].y)
        problem = nil
    }

    private func apply() {
        guard piece.points.indices.contains(selected),
              let newX = DraftContourGeometry.coordinate(x),
              let newY = DraftContourGeometry.coordinate(y) else {
            problem = "X ve Y için geçerli milimetre değerleri girin."
            return
        }
        var updated = piece
        updated.points[selected] = DraftPoint(x: newX, y: newY)
        if let message = DraftContourGeometry.validationError(points: updated.points, closed: updated.closed) {
            problem = message
            return
        }
        onSave(updated)
        dismiss()
    }

    private var preview: some View {
        GeometryReader { geometry in
            if let map = DraftCanvasMap(size: geometry.size, minX: piece.minX, maxX: piece.maxX,
                                        minY: piece.minY, maxY: piece.maxY, inset: 22) {
                Canvas { context, _ in
                    var path = Path()
                    for (index, point) in piece.points.enumerated() {
                        let position = map.screen(point)
                        if index == 0 { path.move(to: position) } else { path.addLine(to: position) }
                    }
                    if piece.closed { path.closeSubpath() }
                    context.stroke(path, with: .color(.secondary), lineWidth: 2)
                    for (index, point) in piece.points.enumerated() {
                        let position = map.screen(point)
                        let radius: CGFloat = index == selected ? 7 : 4
                        let marker = CGRect(x: position.x - radius, y: position.y - radius,
                                            width: 2 * radius, height: 2 * radius)
                        context.fill(Path(ellipseIn: marker), with: .color(index == selected ? .accentColor : .secondary))
                    }
                }
                .contentShape(Rectangle())
                .gesture(SpatialTapGesture().onEnded { tap in
                    if let index = map.nearestVertex(to: tap.location, points: piece.points) { select(index) }
                })
                .accessibilityHidden(true)
            }
        }
        .frame(height: 190)
        .background(Color.secondary.opacity(0.08), in: RoundedRectangle(cornerRadius: 14))
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    Text(piece.name).font(.headline)
                    Text("Bir köşeye dokunun veya listeden nokta seçin. Yeni konumu milimetre olarak girin.")
                        .font(.subheadline)
                    preview
                    Picker("Nokta", selection: Binding(get: { selected }, set: { select($0) })) {
                        ForEach(piece.points.indices, id: \.self) { index in
                            Text("Nokta \(index + 1)").tag(index)
                        }
                    }
                    .pickerStyle(.menu)
                    .frame(minHeight: 44)
                    Text("X (mm)")
                    TextField("X", text: $x)
                        .accessibilityLabel("Noktanın X koordinatı")
                        .textFieldStyle(.roundedBorder)
                    Text("Y (mm)")
                    TextField("Y", text: $y)
                        .accessibilityLabel("Noktanın Y koordinatı")
                        .textFieldStyle(.roundedBorder)
                    Button(action: apply) {
                        Text("Nokta konumunu uygula").frame(maxWidth: .infinity, minHeight: 44)
                    }
                    .buttonStyle(.appPrimary)
                }
                .padding()
            }
            .navigationTitle("Kontur noktaları")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Vazgeç") { dismiss() } }
            }
        }
        .alert("Nokta değiştirilemedi", isPresented: Binding(
            get: { problem != nil }, set: { if !$0 { problem = nil } }
        )) {
            Button("Tamam", role: .cancel) { problem = nil }
        } message: {
            Text(problem ?? "")
        }
    }
}
