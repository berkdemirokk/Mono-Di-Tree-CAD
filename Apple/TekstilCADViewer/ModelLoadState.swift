import Foundation

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
