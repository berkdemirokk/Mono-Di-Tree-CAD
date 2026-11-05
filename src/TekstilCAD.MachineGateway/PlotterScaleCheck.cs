namespace TekstilCAD.MachineGateway;

/// <summary>Arithmetic for a manually measured 100 mm reference, not device validation.</summary>
public static class PlotterScaleCheck
{
    public const double ReferenceMillimeters = 100;
    public readonly record struct Result(double XDeviationMillimeters, double YDeviationMillimeters);
    public readonly record struct DiagonalResult(double FirstDeviationMillimeters, double SecondDeviationMillimeters, double DifferenceMillimeters);
    public static double ReferenceDiagonalMillimeters => ReferenceMillimeters * Math.Sqrt(2);

    public static Result Compare(double measuredXMillimeters, double measuredYMillimeters)
    {
        if (!double.IsFinite(measuredXMillimeters) || measuredXMillimeters <= 0 || measuredXMillimeters > 1000)
            throw new ArgumentOutOfRangeException(nameof(measuredXMillimeters));
        if (!double.IsFinite(measuredYMillimeters) || measuredYMillimeters <= 0 || measuredYMillimeters > 1000)
            throw new ArgumentOutOfRangeException(nameof(measuredYMillimeters));
        return new Result(measuredXMillimeters - ReferenceMillimeters, measuredYMillimeters - ReferenceMillimeters);
    }

    /// <summary>Compares two manually measured diagonals with the nominal square; no tolerance or device qualification is inferred.</summary>
    public static DiagonalResult CompareDiagonals(double firstMillimeters,double secondMillimeters)
    {
        if(!double.IsFinite(firstMillimeters) || firstMillimeters<=0 || firstMillimeters>1500)
            throw new ArgumentOutOfRangeException(nameof(firstMillimeters));
        if(!double.IsFinite(secondMillimeters) || secondMillimeters<=0 || secondMillimeters>1500)
            throw new ArgumentOutOfRangeException(nameof(secondMillimeters));
        var nominal=ReferenceDiagonalMillimeters;
        return new DiagonalResult(firstMillimeters-nominal,secondMillimeters-nominal,firstMillimeters-secondMillimeters);
    }
}
