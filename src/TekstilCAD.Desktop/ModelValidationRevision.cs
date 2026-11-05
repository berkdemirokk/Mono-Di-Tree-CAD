namespace TekstilCAD.Desktop;

// Revision equality prevents an edit followed by an undo from reviving an old result.
internal readonly record struct ModelValidationRevision(long Question,long Reference,long Chat,long Model)
{
    internal static bool IsCurrent(ModelValidationRevision expected,ModelValidationRevision current,bool closed,bool photoBusy,bool canceled) =>
        !closed && !photoBusy && !canceled && expected==current;

    internal static async Task ValidateAsync(Action validate,CancellationToken cancellationToken)
    {
        // The decoder is synchronous and cannot be interrupted once entered. Stop waiting
        // on cancellation; its eventual result is never applied to the window.
        await Task.Run(validate,CancellationToken.None).WaitAsync(cancellationToken);
    }
}
