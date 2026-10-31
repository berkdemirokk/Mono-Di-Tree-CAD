namespace TekstilCAD.FileFormats;

/// <summary>Copies caller-owned selections while enforcing the real enumeration bound.</summary>
internal static class DxfBoundedSelections
{
    public static T[] Copy<T>(IReadOnlyCollection<T> source,int maximum,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if(maximum<0)throw new ArgumentOutOfRangeException(nameof(maximum));
        var reported=source.Count;
        if(reported<0 || reported>maximum)throw new InvalidDataException("DXF seçim sınırı aşıldı veya sayı geçersiz.");
        var items=new List<T>(Math.Min(reported,256));
        foreach(var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(items.Count>=maximum)throw new InvalidDataException("DXF seçim sınırı aşıldı.");
            items.Add(item);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if(items.Count!=reported)throw new InvalidDataException("DXF seçim koleksiyonu okunurken değişti veya sayısı uyuşmuyor.");
        return items.ToArray();
    }
}
