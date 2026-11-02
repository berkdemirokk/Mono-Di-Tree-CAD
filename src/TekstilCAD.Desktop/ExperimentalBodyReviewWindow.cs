using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>In-memory measurement snapshot only. It has no runtime or export capability.</summary>
internal sealed class ExperimentalBodyDraftRequest
{
    internal IReadOnlyDictionary<string,decimal> Values { get; }
    private ExperimentalBodyDraftRequest(IReadOnlyDictionary<string,decimal> values) => Values=new ReadOnlyDictionary<string,decimal>(new Dictionary<string,decimal>(values,StringComparer.Ordinal));
    internal static bool TryCreate(IEnumerable<BreannaMeasurementInput> input,out ExperimentalBodyDraftRequest? request,out IReadOnlyList<BreannaMeasurementError> errors)
    {
        var result=BreannaMeasurementGate.Review(input);errors=result.Errors;request=result.IsValid ? new ExperimentalBodyDraftRequest(result.Values!) : null;return request is not null;
    }
    public override string ToString()=>"Deneysel ölçü özeti (değerler gizli)";
}

internal sealed class ExperimentalBodyReviewWindow : Window
{
    internal ListBox ValuesForVerification { get; } = new() { MinHeight=120 };
    internal ExperimentalBodyReviewWindow(Window owner,ExperimentalBodyDraftRequest request)
    {
        Owner=owner;Title="Deneysel ölçü özeti";Width=550;Height=550;MinWidth=430;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("DesignRequestTheme.xaml",UriKind.Relative) });Background=DesignRequestWindow.BackgroundColor;Foreground=DesignRequestWindow.Ink;
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var close=new Button { Content="Kapat",IsCancel=true,Padding=new Thickness(12,6,12,6),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);close.Click+=(_,_)=>Close();
        var title=new TextBlock { Text="Ölçü özeti · deneysel",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8) };DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);
        var message=new TextBlock { Text="15 değer yalnız teknik olarak denetlendi; ölçüm yöntemini ve değerleri kontrol edin. Henüz beden görüntüsü veya kalıp üretilmedi. Bu geçici özet dış bir programa gönderilmez, projeye eklenmez ve pencere kapanınca saklanmaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) };DockPanel.SetDock(message,Dock.Top);root.Children.Add(message);
        ValuesForVerification.Background=DesignRequestWindow.Surface;ValuesForVerification.Foreground=DesignRequestWindow.Ink;ValuesForVerification.BorderBrush=DesignRequestWindow.BorderColor;
        ValuesForVerification.ItemsSource=BreannaMeasurementGate.Definitions.Select(d=>$"{d.Label}: {request.Values[d.Key].ToString(System.Globalization.CultureInfo.CurrentCulture)} {(d.IsAngle ? "derece" : "mm")}").ToArray();
        AutomationProperties.SetName(ValuesForVerification,"Denetlenmiş deneysel ölçü özeti");root.Children.Add(ValuesForVerification);
    }
}
