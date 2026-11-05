namespace TekstilCAD.FileFormats;

/// <summary>Explicit request totals only. No front/back allocation or fit inference.</summary>
public sealed record ALineBodyTotals(decimal? WaistMm,decimal? HipMm)
{
    public static ALineBodyTotals Review(string prompt,string measurements)
    {
        var review=DressMeasurementParser.Review(prompt,measurements);
        decimal? Read(string label)=>review.Questions.Any(q=>q.StartsWith(label+":",StringComparison.Ordinal))
            ? null : review.Measurements.FirstOrDefault(m=>m.Label==label)?.Millimeters;
        return new(Read("bel çevresi"),Read("kalça çevresi"));
    }
}
