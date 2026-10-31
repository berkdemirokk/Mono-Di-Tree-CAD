namespace TekstilCAD.Desktop;

/// <summary>
/// Tracks independent recovery warnings so resolving one operation cannot hide
/// a still-active warning from another. The owner controls asynchronous request
/// ordering before recording a success or failure here.
/// </summary>
internal sealed class RecoveryWarningState
{
    internal enum Kind { Save, Clear, General }
    private readonly record struct Warning(string Message,string Detail);
    private Warning? save,clear;
    private readonly Dictionary<string,Warning> general = new(StringComparer.Ordinal);

    internal void Set(Kind kind,string message,string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var warning=new Warning(message,detail ?? "");
        switch(kind)
        {
            case Kind.Save: save=warning;break;
            case Kind.Clear: clear=warning;break;
            case Kind.General: general[message]=warning;break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    internal void Clear(Kind kind)
    {
        switch(kind)
        {
            case Kind.Save: save=null;break;
            case Kind.Clear: clear=null;break;
            case Kind.General: general.Clear();break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    internal bool ClearGeneral(string message)=>general.Remove(message);

    internal bool Has(Kind kind)=>kind switch
    {
        Kind.Save=>save.HasValue,
        Kind.Clear=>clear.HasValue,
        Kind.General=>general.Count>0,
        _=>throw new ArgumentOutOfRangeException(nameof(kind))
    };
    internal bool HasAny=>save.HasValue || clear.HasValue || general.Count>0;

    // Update only the advice while retaining the last recorded error detail.
    internal bool UpdateMessage(Kind kind,string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if(kind!=Kind.Save)throw new ArgumentOutOfRangeException(nameof(kind));
        if(save is not { } current || current.Message==message)return false;
        save=current with { Message=message };return true;
    }

    // Include every still-active message and label each diagnostic so the detail
    // button cannot accidentally show only whichever operation failed last.
    internal string Message=>string.Join("\n",Active().Select(item=>item.Warning.Message));
    internal string Detail=>string.Join("\n\n",Active().Select(item=>$"{item.Label}:\n{item.Warning.Detail}"));
    private IEnumerable<(string Label,Warning Warning)> Active()
    {
        if(save is { } s)yield return ("Otomatik kurtarma kaydı",s);
        if(clear is { } c)yield return ("Kurtarma kaydını temizleme",c);
        foreach(var warning in general.Values)yield return ("Diğer kurtarma uyarısı",warning);
    }
}
