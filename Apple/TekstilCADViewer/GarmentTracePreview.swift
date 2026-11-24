import SwiftUI

/// Plays the cutting outline in document order. The slider also works with VoiceOver.
struct GarmentTracePreview: View {
    let project: DraftProject
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var progress = 1.0
    @State private var playing = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Canvas { context, size in
                let pieces = project.pieces.filter { !$0.points.isEmpty }
                guard let left = pieces.map(\.minX).min(),
                      let right = pieces.map(\.maxX).max(),
                      let top = pieces.map(\.minY).min(),
                      let bottom = pieces.map(\.maxY).max() else { return }
                let scale = max(0, min((size.width - 24) / max(1, right - left),
                                       (size.height - 24) / max(1, bottom - top)))
                let offsetX = (size.width - (right - left) * scale) / 2
                let offsetY = (size.height - (bottom - top) * scale) / 2
                func position(_ point: DraftPoint) -> CGPoint {
                    CGPoint(x: offsetX + (point.x - left) * scale,
                            y: offsetY + (point.y - top) * scale)
                }
                for (index, piece) in pieces.enumerated() {
                    guard let first = piece.points.first else { continue }
                    var path = Path()
                    path.move(to: position(first))
                    for point in piece.points.dropFirst() { path.addLine(to: position(point)) }
                    if piece.closed { path.closeSubpath() }
                    context.stroke(path, with: .color(.secondary.opacity(0.35)), lineWidth: 1)
                    let amount = min(1, max(0, progress * Double(pieces.count) - Double(index)))
                    context.stroke(path.trimmedPath(from: 0, to: amount),
                                   with: .color(.accentColor), lineWidth: 3)
                }
            }
            .frame(minHeight: 140, idealHeight: 220)
            .accessibilityLabel("Kalıp önizlemesi")
            .accessibilityValue("\(project.name), \(project.pieces.count) parça")
            HStack {
                Button {
                    if !playing && progress >= 1 { progress = 0 }
                    playing.toggle()
                } label: {
                    Image(systemName: playing ? "pause.fill" : "play.fill")
                        .frame(width: 44, height: 44)
                }
                .accessibilityLabel(playing ? "Çizimi duraklat" : "Çizimi oynat")
                Slider(value: $progress, in: 0...1, onEditingChanged: { editing in
                    if editing { playing = false }
                })
                .accessibilityLabel("Çizim ilerlemesi")
                Text(progress, format: .percent.precision(.fractionLength(0)))
                    .monospacedDigit()
            }
        }
        .task(id: playing) {
            guard playing else { return }
            let steps = reduceMotion ? max(1, project.pieces.count) : 100
            while progress < 1 && !Task.isCancelled {
                do { try await Task.sleep(nanoseconds: reduceMotion ? 600_000_000 : 40_000_000) }
                catch { return }
                progress = min(1, progress + 1 / Double(steps))
            }
            if !Task.isCancelled { playing = false }
        }
        .onDisappear { playing = false }
    }
}
