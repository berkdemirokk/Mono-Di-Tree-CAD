import Foundation

// A read may run off the main thread while a newer selection supersedes it.
final class ModelReadCancellation {
    private let lock = NSLock()
    private var cancelled = false
    func cancel() { lock.lock(); cancelled = true; lock.unlock() }
    var isCancelled: Bool { lock.lock(); defer { lock.unlock() }; return cancelled }
}

// Tracks the most recent explicit model choice. A rejected selection still
// supersedes an earlier asynchronous read; cancelling the picker does not.
struct ModelLoadState {
    private(set) var request = UUID()
    private(set) var isLoading = false

    mutating func begin() -> UUID {
        request = UUID()
        isLoading = true
        return request
    }

    mutating func invalidate() {
        request = UUID()
        isLoading = false
    }

    // A picker failure supersedes an older pending read; cancelling does not.
    mutating func rejectPickerFailure(_ error: Error) -> Bool {
        guard !isFilePickerCancellation(error) else { return false }
        invalidate()
        return true
    }

    mutating func finish(_ completedRequest: UUID) -> Bool {
        guard request == completedRequest, isLoading else { return false }
        isLoading = false
        return true
    }
}

// View state is installed as one value, so the drawing, its original bytes and
// revision cannot temporarily describe different successfully opened models.
struct LoadedModelState<Model> {
    private(set) var model: Model?
    private(set) var sourceData: Data?
    private(set) var revision = UUID()
    var load = ModelLoadState()
    var selectedPiece: Int?

    #if DEBUG
    struct EditIdentity {
        let revision: UUID
        let loadRequest: UUID
        let selectedPiece: Int
    }

    /// The identity must be captured from the same value that supplies the
    /// original bytes. Starting or cancelling a load changes its request token.
    func editIdentity() -> EditIdentity? {
        guard !load.isLoading, model != nil, sourceData != nil, let selectedPiece else { return nil }
        return EditIdentity(revision: revision, loadRequest: load.request, selectedPiece: selectedPiece)
    }

    func isCurrent(_ identity: EditIdentity, data: Data) -> Bool {
        !load.isLoading && model != nil && sourceData == data && revision == identity.revision &&
            load.request == identity.loadRequest && selectedPiece == identity.selectedPiece
    }
    #endif
    /// A successful asynchronous preview can install only while its load is current.
    mutating func completePreview(_ request: UUID, model: Model, data: Data, selectedPiece: Int?) -> Bool {
        guard load.finish(request) else { return false }
        install(model, data: data, selectedPiece: selectedPiece)
        return true
    }
    mutating func install(_ model: Model, data: Data, selectedPiece: Int? = nil) {
        self.model = model
        self.sourceData = data
        self.selectedPiece = selectedPiece
        revision = UUID()
    }
}
