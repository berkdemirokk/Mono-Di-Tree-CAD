using TekstilCAD.FileFormats;

namespace TekstilCAD.MachineGateway;

public enum SimulatedResolution { Unresolved, ReportedProduced, ReportedNotProduced }

public sealed record SimulatedAttempt(Guid AttemptId, string DeviceId, string OutputSha256, int OutputByteCount, PlotterCapabilityProfile Profile,
    SimulatedTransferState TransferState, SimulatedResolution Resolution, string? ResolutionNote);

/// <summary>
/// Process-local simulation only. It has no persistence or cross-process locking and
/// must never be used as a physical dispatch lock or production recovery ledger.
/// Device IDs must already be canonical and uniquely mapped by the caller.
/// </summary>
public sealed class PlotterSimulationLedger
{
    private const int MaximumAttempts = 10_000;
    private readonly object gate = new();
    private readonly Dictionary<Guid, SimulatedAttempt> attempts = new();
    private readonly Dictionary<string, Guid> busyDevices = new(StringComparer.Ordinal);

    public SimulatedAttempt Start(Guid attemptId, string deviceId, PlotterCapabilityProfile profile,
        HpglPreparedOutput output, PlotterSimulationReview review, string sourceRevision, bool interrupted,
        PlotterQualificationEvidence qualification, DateTimeOffset observedAt)
    {
        PlotterSimulationReview.ValidateDeviceId(deviceId);
        if (PlotterQualificationAssessment.Evaluate(qualification, deviceId, profile, observedAt) != QualificationAssessment.EligibleForSimulation)
            throw new InvalidOperationException("Simülasyon yeterlilik kaydı geçerli veya eşleşmiş değil.");
        lock (gate)
        {
            if (attempts.ContainsKey(attemptId)) throw new InvalidOperationException("Deneme kimliği daha önce kullanıldı.");
            if (busyDevices.ContainsKey(deviceId)) throw new InvalidOperationException("Cihaz simülasyon sonucu mutabakat bekliyor.");
            if (attempts.Count >= MaximumAttempts) throw new InvalidOperationException("Simülasyon kayıt sınırına ulaşıldı.");
            // A review is consumed only after slot and duplicate checks. All work here
            // is memory-only; no physical device is touched.
            var simulation = new PlotterTransferSimulation();
            simulation.Submit(attemptId, profile, output, review, sourceRevision, deviceId, interrupted);
            // Preflight has already required ASCII, therefore UTF-8 byte count equals text length.
            var attempt = new SimulatedAttempt(attemptId, deviceId, review.Sha256, output.Text.Length, profile, simulation.State, SimulatedResolution.Unresolved, null);
            attempts.Add(attemptId, attempt);
            busyDevices.Add(deviceId, attemptId);
            return attempt;
        }
    }

    public SimulatedAttempt Resolve(Guid attemptId, SimulatedResolution resolution, string note)
    {
        if (resolution is not (SimulatedResolution.ReportedProduced or SimulatedResolution.ReportedNotProduced))
            throw new ArgumentOutOfRangeException(nameof(resolution));
        if (string.IsNullOrWhiteSpace(note) || note.Length > 1024)
            throw new ArgumentException("Mutabakat açıklaması gerekli ve en fazla 1024 karakter olmalıdır.", nameof(note));
        lock (gate)
        {
            if (!attempts.TryGetValue(attemptId, out var attempt)) throw new KeyNotFoundException("Deneme bulunamadı.");
            if (attempt.Resolution != SimulatedResolution.Unresolved) throw new InvalidOperationException("Deneme zaten sonuçlandırıldı.");
            var resolved = attempt with { Resolution = resolution, ResolutionNote = note };
            attempts[attemptId] = resolved;
            busyDevices.Remove(attempt.DeviceId);
            return resolved;
        }
    }

    public SimulatedAttempt Get(Guid attemptId)
    {
        lock (gate)
            return attempts.TryGetValue(attemptId, out var attempt) ? attempt : throw new KeyNotFoundException("Deneme bulunamadı.");
    }
}
