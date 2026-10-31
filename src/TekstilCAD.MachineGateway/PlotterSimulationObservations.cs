namespace TekstilCAD.MachineGateway;

public enum SimulatedObservationKind
{
    BeforeSend, Interrupted, BytesWritten, SpoolerAccepted, DeviceAccepted, DeviceReportsCompletion
}

public enum SimulatedDeliveryAssessment
{
    InvalidEvidence, OutcomeUnknown, BytesWritten, SpoolerAccepted,
    DeviceAccepted, DeviceReportsCompletion
}

/// <summary>
/// An invented observation for simulation, not an authenticated response from a device.
/// BytesWritten is the count reported by the simulated transport, not physical output.
/// </summary>
public sealed record SimulatedDeliveryObservation(Guid AttemptId, string DeviceId, string OutputSha256,
    SimulatedObservationKind Kind, long BytesWritten, long TotalBytes);

public static class PlotterSimulationObservations
{
    /// <summary>
    /// Correlates a simulated observation to an attempt. No output unlocks the ledger:
    /// physical completion requires a future real adapter and independently verified evidence.
    /// </summary>
    public static SimulatedDeliveryAssessment Assess(SimulatedAttempt attempt, PlotterCapabilityProfile profile,
        SimulatedDeliveryObservation observation)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(observation);
        var expectedTotalBytes = attempt.OutputByteCount;
        if (attempt.AttemptId == Guid.Empty || profile != attempt.Profile || observation.AttemptId != attempt.AttemptId ||
            !string.Equals(observation.DeviceId, attempt.DeviceId, StringComparison.Ordinal) ||
            !string.Equals(observation.OutputSha256, attempt.OutputSha256, StringComparison.Ordinal) ||
            !Enum.IsDefined(observation.Kind) || expectedTotalBytes <= 0 ||
            observation.TotalBytes != expectedTotalBytes || observation.BytesWritten < 0 ||
            observation.BytesWritten > expectedTotalBytes)
            return SimulatedDeliveryAssessment.InvalidEvidence;
        if (observation.Kind == SimulatedObservationKind.BeforeSend)
            return observation.BytesWritten == 0 ? SimulatedDeliveryAssessment.OutcomeUnknown : SimulatedDeliveryAssessment.InvalidEvidence;
        if (observation.Kind == SimulatedObservationKind.Interrupted || observation.BytesWritten != expectedTotalBytes)
            return SimulatedDeliveryAssessment.OutcomeUnknown;
        return observation.Kind switch
        {
            SimulatedObservationKind.BytesWritten => SimulatedDeliveryAssessment.BytesWritten,
            SimulatedObservationKind.SpoolerAccepted => SimulatedDeliveryAssessment.SpoolerAccepted,
            SimulatedObservationKind.DeviceAccepted => SimulatedDeliveryAssessment.DeviceAccepted,
            SimulatedObservationKind.DeviceReportsCompletion when profile.PhysicalCompletionEvidence == CapabilitySupport.Supported
                => SimulatedDeliveryAssessment.DeviceReportsCompletion,
            SimulatedObservationKind.DeviceReportsCompletion => SimulatedDeliveryAssessment.OutcomeUnknown,
            _ => SimulatedDeliveryAssessment.InvalidEvidence
        };
    }
}
