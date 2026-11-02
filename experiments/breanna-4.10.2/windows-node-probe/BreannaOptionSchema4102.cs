using System.Text.Json;
internal static class BreannaOptionSchema4102
{
    internal static void Validate(JsonElement options)
    {
        CheckBool(options, "shoulderDart");
        CheckBool(options, "waistDart");
        CheckList(options, "primaryBustDart", new[] { "06:00", "07:00", "08:00", "09:00", "10:00", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "15:00", "16:00", "17:00" });
        CheckList(options, "secondaryBustDart", new[] { "none", "06:00", "07:00", "08:00", "09:00", "10:00", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30", "14:00", "15:00", "16:00", "17:00" });
        CheckNumber(options, "acrossBackFactor", 0.93d, 1d);
        CheckNumber(options, "armholeDepthFactor", 0.8d, 1.2d);
        CheckNumber(options, "backNeckCutout", 0.02d, 0.08d);
        CheckNumber(options, "bicepsEase", 0d, 0.5d);
        CheckNumber(options, "shoulderDartSize", 0.04d, 0.1d);
        CheckNumber(options, "shoulderDartLength", 0.6d, 1d);
        CheckNumber(options, "waistDartSize", 0.04d, 0.15d);
        CheckNumber(options, "waistDartLength", 0.6d, 1d);
        CheckNumber(options, "verticalEase", 0d, 0.08d);
        CheckNumber(options, "frontArmholeDeeper", 0d, 0.05d);
        CheckNumber(options, "shoulderEase", 0d, 0.04d);
        CheckNumber(options, "collarEase", 0d, 0.1d);
        CheckNumber(options, "chestEase", 0.05d, 0.2d);
        CheckNumber(options, "waistEase", 0.05d, 0.2d);
        CheckNumber(options, "primaryBustDartShaping", 0.25d, 0.75d);
        CheckNumber(options, "primaryBustDartLength", 0.65d, 0.95d);
        CheckNumber(options, "secondaryBustDartLength", 0.65d, 0.95d);
        CheckNumber(options, "shoulderSlopeReduction", 0d, 1d);
        CheckNumber(options, "frontScyeDart", 0d, 0.45d);
        CheckNumber(options, "sleevecapEase", 0d, 0.025d);
        CheckNumber(options, "sleevecapTopFactorX", 0.25d, 0.75d);
        CheckNumber(options, "sleevecapTopFactorY", 0.35d, 1.65d);
        CheckNumber(options, "sleevecapBackFactorX", 0.35d, 0.55d);
        CheckNumber(options, "sleevecapBackFactorY", 0.3d, 0.65d);
        CheckNumber(options, "sleevecapFrontFactorX", 0.35d, 0.65d);
        CheckNumber(options, "sleevecapFrontFactorY", 0.3d, 0.65d);
        CheckNumber(options, "sleevecapQ1Offset", 0d, 0.07d);
        CheckNumber(options, "sleevecapQ2Offset", 0d, 0.07d);
        CheckNumber(options, "sleevecapQ3Offset", 0d, 0.07d);
        CheckNumber(options, "sleevecapQ4Offset", 0d, 0.07d);
        CheckNumber(options, "sleevecapQ1Spread1", 0.04d, 0.2d);
        CheckNumber(options, "sleevecapQ1Spread2", 0.04d, 0.2d);
        CheckNumber(options, "sleevecapQ2Spread1", 0.04d, 0.2d);
        CheckNumber(options, "sleevecapQ2Spread2", 0.04d, 0.2d);
        CheckNumber(options, "sleevecapQ3Spread1", 0.04d, 0.2d);
        CheckNumber(options, "sleevecapQ3Spread2", 0.04d, 0.2d);
        CheckNumber(options, "sleevecapQ4Spread1", 0.04d, 0.2d);
        CheckNumber(options, "sleevecapQ4Spread2", 0.04d, 0.2d);
        CheckNumber(options, "cuffEase", 0d, 0.5d);
        CheckNumber(options, "sleeveLengthBonus", -0.4d, 0.1d);
    }
    private static void CheckNumber(JsonElement o,string key,double min,double max) { var v=o.GetProperty(key);if(v.ValueKind!=JsonValueKind.Number||!v.TryGetDouble(out var x)||!double.IsFinite(x)||x<min||x>max)throw new InvalidDataException("Geçersiz deneysel yanıt."); }
    private static void CheckBool(JsonElement o,string key) { if(o.GetProperty(key).ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidDataException("Geçersiz deneysel yanıt."); }
    private static void CheckList(JsonElement o,string key,string[] values) { var v=o.GetProperty(key);if(v.ValueKind!=JsonValueKind.String||!values.Contains(v.GetString(),StringComparer.Ordinal))throw new InvalidDataException("Geçersiz deneysel yanıt."); }
}
