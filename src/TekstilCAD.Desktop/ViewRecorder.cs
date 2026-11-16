using System.Windows;
using System.Windows.Controls;

namespace TekstilCAD.Desktop;

/// <summary>
/// Records everything the simple view touches so that leaving it restores the detailed layout exactly: for a dependency
/// property the local value (or its absence), for a panel the children that were added. Restoring runs in reverse order.
/// </summary>
internal sealed class ViewRecorder
{
    private readonly List<Action> restores = [];

    internal int Count => restores.Count;

    /// <summary>Sets a property and remembers its previous local value (or that there was none).</summary>
    internal void Set(DependencyObject target, DependencyProperty property, object? value)
    {
        var local = target.ReadLocalValue(property);
        restores.Add(() =>
        {
            if (ReferenceEquals(local, DependencyProperty.UnsetValue)) target.ClearValue(property);
            else target.SetValue(property, local);
        });
        target.SetValue(property, value);
    }

    /// <summary>Adds a child (at the end or at the index) and removes it again on restore.</summary>
    internal void Add(Panel parent, UIElement child, int index = -1)
    {
        if (index < 0 || index > parent.Children.Count) parent.Children.Add(child); else parent.Children.Insert(index, child);
        restores.Add(() => parent.Children.Remove(child));
    }

    /// <summary>Runs an action that is not a property or child change and remembers how to undo it.</summary>
    internal void Do(Action apply, Action restore)
    {
        apply();
        restores.Add(restore);
    }

    internal void Restore()
    {
        for (var i = restores.Count - 1; i >= 0; i--) restores[i]();
        restores.Clear();
    }
}
