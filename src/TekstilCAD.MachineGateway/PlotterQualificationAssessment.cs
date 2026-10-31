namespace TekstilCAD.MachineGateway;

public enum QualificationStatus { Unknown, Active, Suspended, Revoked }
public enum QualificationAssessment { InsufficientEvidence, Inactive, Expired, Mismatch, EligibleForSimulation }

/// <summary>
/// Caller-supplied evidence snapshot. Neither its authority nor its truth is verified.
/// In particular this is not a production qualification registry or security boundary.
/// </summary>
public sealed record PlotterQualificationEvidence(
    string DeviceId, PlotterCapabilityProfile Profile, QualificationStatus Status,
    DateTimeOffset ValidFrom, DateTimeOffset ValidUntil, string EvidenceReference);

public static class PlotterQualificationAssessment
{
    public static QualificationAssessment Evaluate(PlotterQualificationEvidence? evidence,
        string deviceId, PlotterCapabilityProfile? profile, DateTimeOffset observedAt)
    {
        if (evidence is null || profile is null || !profile.IsComplete || evidence.Profile is null ||
            string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 256 ||
            !string.Equals(deviceId, deviceId.Trim(), StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(evidence.EvidenceReference) || evidence.EvidenceReference.Length > 1024 ||
            evidence.ValidFrom == default || evidence.ValidUntil <= evidence.ValidFrom || observedAt == default ||
            !Enum.IsDefined(evidence.Status) || evidence.Status == QualificationStatus.Unknown)
            return QualificationAssessment.InsufficientEvidence;
        if (!string.Equals(deviceId, evidence.DeviceId, StringComparison.Ordinal) || profile != evidence.Profile)
            return QualificationAssessment.Mismatch;
        if (evidence.Status != QualificationStatus.Active) return QualificationAssessment.Inactive;
        // The end instant is exclusive, so a qualification cannot survive its expiry boundary.
        if (observedAt < evidence.ValidFrom || observedAt >= evidence.ValidUntil)
            return QualificationAssessment.Expired;
        return QualificationAssessment.EligibleForSimulation;
    }
}
