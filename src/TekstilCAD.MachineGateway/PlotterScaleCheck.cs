namespace TekstilCAD.MachineGateway;

/// <summary>Arithmetic for a manually measured 100 mm reference, not device validation.</summary>
public static class PlotterScaleCheck
{
    public const double ReferenceMillimeters = 100;
    public readonly record struct Result(double XDeviationMillimeters, double YDeviationMillimeters);

    public static Result Compare(double measuredXMillimeters, double measuredYMillimeters)
    {
        if (!double.IsFinite(measuredXMillimeters) || measuredXMillimeters <= 0 || measuredXMillimeters > 1000)
            throw new ArgumentOutOfRangeException(nameof(measuredXMillimeters));
        if (!double.IsFinite(measuredYMillimeters) || measuredYMillimeters <= 0 || measuredYMillimeters > 1000)
            throw new ArgumentOutOfRangeException(nameof(measuredYMillimeters));
        return new Result(measuredXMillimeters - ReferenceMillimeters, measuredYMillimeters - ReferenceMillimeters);
    }
}
