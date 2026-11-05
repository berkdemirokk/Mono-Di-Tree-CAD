import Foundation
import CoreGraphics
import SwiftUI

/// Small, local-only preview; source bytes are never edited or interpreted.
struct RequestReferenceThumbnail: View {
    let bytes: Data
    @State private var activeID: UUID?
    @State private var representedBytes: Data?
    @State private var thumbnail: CGImage?
    @State private var loading = true
    @State private var failed = false

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            if let thumbnail, representedBytes == bytes {
                Image(decorative: thumbnail, scale: 1, orientation: .up)
                    .resizable().scaledToFit().frame(maxWidth: .infinity, maxHeight: 200)
                    .accessibilityHidden(false)
                    .accessibilityLabel("Seçilen referans görselinin yerel önizlemesi")
                Text("Bu cihazdaki önizlemedir; Windows’ta yön farklı görünebilir. Otomatik kalıp veya ölçü oluşturmaz.")
                    .font(.caption).foregroundStyle(.secondary)
            } else if loading {
                ProgressView("Görsel önizlemesi hazırlanıyor…")
            } else if failed {
                Text("Önizleme gösterilemedi. Seçilen görsel değiştirilmedi; dosya denetimi ayrıca yapılır.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .task(id: bytes) {
            let ticket = PhotoObservation.ThumbnailTicket(bytes: bytes)
            activeID = ticket.id; representedBytes = nil; thumbnail = nil; failed = false; loading = true
            let worker = Task.detached(priority: .userInitiated) { try PhotoObservation.thumbnail(ticket.bytes) }
            do {
                let result = try await withTaskCancellationHandler(operation: { try await worker.value }, onCancel: { worker.cancel() })
                guard !Task.isCancelled, ticket.matches(activeID: activeID, currentBytes: bytes) else { return }
                thumbnail = result; representedBytes = ticket.bytes; loading = false
            } catch {
                guard !Task.isCancelled, ticket.matches(activeID: activeID, currentBytes: bytes) else { return }
                thumbnail = nil; representedBytes = nil; failed = true; loading = false
            }
        }
        .onDisappear { activeID = nil; thumbnail = nil; representedBytes = nil }
    }
}
