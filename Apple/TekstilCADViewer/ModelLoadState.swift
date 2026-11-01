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

    mutating func install(_ model: Model, data: Data) {
        self = LoadedModelState(model: model, sourceData: data, revision: UUID())
    }

    init() {}
    private init(model: Model, sourceData: Data, revision: UUID) {
        self.model = model
        self.sourceData = sourceData
        self.revision = revision
    }
}
