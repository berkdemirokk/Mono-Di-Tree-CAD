namespace MeasurementSlice;

internal sealed record UnitPlan(IReadOnlyDictionary<string,string> Fields,string Tolerance);
internal static class UnitConversion
{
    internal static UnitPlan Prepare(IReadOnlyDictionary<string,string> fields,string tolerance,string from,string to)
    {
        if(from is not ("mm" or "cm")||to is not ("mm" or "cm")||from==to)throw new InvalidOperationException("Geçersiz birim dönüşümü.");
        var keys=TekstilCAD.Patterns.BreannaMeasurementGate.Definitions.Select(x=>"upper."+x.Key).Concat(Contract.LowerKeys.Select(x=>"lower."+x)).ToArray();
        if(fields.Count!=keys.Length||keys.Any(x=>!fields.ContainsKey(x)))throw new InvalidOperationException("Form alanları değişti.");
        string ConvertLength(string text,bool allowZero,decimal maxMm)
        {
            if(string.IsNullOrWhiteSpace(text))return text;
            if(!ExplicitNumber.TryParse(text,out decimal n)||n<0||(!allowZero&&n==0))throw new InvalidOperationException("Dolu alan geçersiz veya belirsiz; önce düzeltin.");
            decimal mm=checked(from=="cm"?n*10m:n);if(from=="cm"&&mm/10m!=n)throw new InvalidOperationException("Kayıpsız dönüşüm mümkün değil.");if(mm>maxMm)throw new InvalidOperationException("Dolu alan teknik sınır dışında.");
            decimal converted=to=="cm"?mm/10m:mm;if((to=="cm"?checked(converted*10m):converted)!=mm)throw new InvalidOperationException("Kayıpsız dönüşüm mümkün değil.");
            return ExplicitNumber.Format(converted);
        }
        var output=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var key in keys){string value=fields[key];if(key=="upper.shoulderSlope"){
            if(!string.IsNullOrWhiteSpace(value)&&(!ExplicitNumber.TryParse(value,out var angle)||angle<=-90||angle>=90))throw new InvalidOperationException("Omuz açısı geçersiz veya belirsiz.");output.Add(key,value);continue;}
            bool isUpper=key.StartsWith("upper.",StringComparison.Ordinal);int lowerIndex=Array.IndexOf(Contract.LowerKeys,key.StartsWith("lower.",StringComparison.Ordinal)?key[6..]:"");
            bool zeroAllowed=!isUpper&&lowerIndex is >=4 and <=7;output.Add(key,ConvertLength(value,zeroAllowed,isUpper?100000m:10000m));}
        return new(output,ConvertLength(tolerance,true,10000m));
    }
}
