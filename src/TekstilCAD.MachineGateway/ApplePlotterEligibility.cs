namespace TekstilCAD.MachineGateway;

/// <summary>Conservative prerequisites only; never a device or App Store approval.</summary>
public static class ApplePlotterEligibility
{
    public enum Decision { PrerequisitesMet, Unsupported, Unknown }
    public enum IPadProcessor { Unknown, AppleMSeries, Other }

    public static Decision EvaluateIPadDriverKit(int? osMajorVersion, IPadProcessor processor,
        CapabilitySupport distributionEntitlement, CapabilitySupport userEnabledDriver)
    {
        if (!Enum.IsDefined(processor) || !Enum.IsDefined(distributionEntitlement) || !Enum.IsDefined(userEnabledDriver))
            return Decision.Unknown;
        if (osMajorVersion is <= 0) return Decision.Unknown;
        if (osMajorVersion is < 16 || processor == IPadProcessor.Other ||
            distributionEntitlement == CapabilitySupport.Unsupported || userEnabledDriver == CapabilitySupport.Unsupported)
            return Decision.Unsupported;
        if (osMajorVersion is null || processor == IPadProcessor.Unknown ||
            distributionEntitlement == CapabilitySupport.Unknown || userEnabledDriver == CapabilitySupport.Unknown)
            return Decision.Unknown;
        return Decision.PrerequisitesMet;
    }
}
