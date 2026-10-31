namespace TekstilCAD.Nesting;

/// <summary>Structured source identity for a piece-specific validation failure.
/// Error text is a fixed explanation and never includes the user-provided name.</summary>
public static class NestingPieceError
{
    private const string PieceKey="TekstilCAD.Nesting.PieceId.v1";
    private const string ReasonKey="TekstilCAD.Nesting.Reason.v1";

    internal static ArgumentException Create(Guid pieceId,string reason,string? parameterName=null,Exception? inner=null)
    {
        var error=new ArgumentException(reason,parameterName,inner);
        error.Data[PieceKey]=pieceId;error.Data[ReasonKey]=reason;
        return error;
    }

    /// <summary>Retrieve piece identity and a fixed, name-free reason when available.</summary>
    public static bool TryGet(Exception exception,out Guid pieceId,out string reason)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if(exception is ArgumentException && exception.Data[PieceKey] is Guid id && id!=Guid.Empty &&
           exception.Data[ReasonKey] is string message && !string.IsNullOrWhiteSpace(message))
        { pieceId=id;reason=message;return true; }
        pieceId=Guid.Empty;reason="";return false;
    }
}
