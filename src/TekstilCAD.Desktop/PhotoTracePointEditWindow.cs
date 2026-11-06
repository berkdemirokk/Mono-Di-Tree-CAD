using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Edits one trace point in a captured draft; reference points cannot be moved here.</summary>
internal sealed class PhotoTracePointEditWindow : Window
{
    private readonly PhotoTraceWindow owner;
    private readonly long revision;
    private readonly Point[] snapshot;
    private int lastDuplicateIndex=-1;
    internal ComboBox PointSelector { get; } = new() { MinWidth=240,Margin=new Thickness(0,2,0,6) };
    internal TextBox XInput { get; } = new() { Width=100,MaxLength=24,Margin=new Thickness(4,0,12,0) };
    internal TextBox YInput { get; } = new() { Width=100,MaxLength=24,Margin=new Thickness(4,0,0,0) };
    internal TextBlock Status { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,6) };
    internal Button FindDuplicateForVerification { get; } = new() { Content="Tekrarı bul",Padding=new Thickness(8,4,8,4),Margin=new Thickness(8,8,0,0) };
    private sealed record Choice(int Index,Point Pixel)
    {
        public override string ToString()=>$"Nokta {Index+1} · X {MeasurementInput.Format(Pixel.X)}, Y {MeasurementInput.Format(Pixel.Y)}";
    }
    internal PhotoTracePointEditWindow(PhotoTraceWindow owner)
    {
        this.owner=owner;Owner=owner;revision=owner.DraftRevision;snapshot=owner.CanvasForVerification.Points.ToArray();
        Title="Çizgi noktasını düzelt";Width=500;Height=440;MinWidth=440;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        var cancel=new Button { Content="Vazgeç",IsCancel=true,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0) };
        var apply=new Button { Content="Noktayı değiştir",Padding=new Thickness(12,6,12,6) };
        var range=new Button { Content="Aralığı düzenle…",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0) };
        AutomationProperties.SetHelpText(range,"İki uç arasında yol önerir veya aradaki noktaları düz kenarla değiştirir; referanslar değişmez.");
        range.Click+=(_,_)=> { if(!IsCurrent())return;var dialog=new PhotoTraceRangeWindow(this,owner);if(dialog.ShowDialog()!=true)return;
            if(dialog.RequestedPath is { } request)owner.Dispatcher.BeginInvoke(new Action(()=>_ = owner.StartReplacementPathAsync(request.First,request.Last,request.Revision)));
            DialogResult=true; };
        apply.Click+=(_,_)=> { if(TryApplyEdit())DialogResult=true; };
        actions.Children.Add(range);actions.Children.Add(cancel);actions.Children.Add(apply);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var body=new StackPanel();root.Children.Add(body);
        body.Children.Add(new TextBlock { Text="Çizgi noktasını seçin; yeni önizleme piksel koordinatlarını girin. Referans noktaları değişmez.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) });
        body.Children.Add(new Label { Content="Çizgi noktası",Target=PointSelector,Padding=new Thickness(0),Foreground=StudioTheme.Text });body.Children.Add(PointSelector);
        AutomationProperties.SetName(PointSelector,"Düzeltilecek çizgi noktası");
        var coords=new WrapPanel();coords.Children.Add(new Label { Content="X",Target=XInput,Foreground=StudioTheme.Text });coords.Children.Add(XInput);coords.Children.Add(new Label { Content="Y",Target=YInput,Foreground=StudioTheme.Text });coords.Children.Add(YInput);
        AutomationProperties.SetName(XInput,"Yeni önizleme piksel X");AutomationProperties.SetName(YInput,"Yeni önizleme piksel Y");body.Children.Add(coords);
        var cursorButton=new Button { Content="İnceleme imlecini kullan",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,8,0,0) };
        AutomationProperties.SetHelpText(cursorButton,"Pencere açılmadan önce fotoğrafta sağ tıklanan veya klavyeyle taşınan imleç konumunu yeni koordinatlara yazar.");
        var cursor=owner.CanvasForVerification.CursorForVerification;
        cursorButton.Click+=(_,_)=> { XInput.Text=MeasurementInput.Format(cursor.X);YInput.Text=MeasurementInput.Format(cursor.Y); };
        var inspectActions=new WrapPanel();inspectActions.Children.Add(cursorButton);inspectActions.Children.Add(FindDuplicateForVerification);body.Children.Add(inspectActions);
        AutomationProperties.SetHelpText(FindDuplicateForVerification,"Aynı önizleme piksel koordinatını kullanan çizgi noktalarını sırayla seçer; referans noktalarını ve projeyi değiştirmez.");
        FindDuplicateForVerification.Click+=(_,_)=>FindNextDuplicate();
        var editActions=new WrapPanel { Margin=new Thickness(0,8,0,0) };
        var insert=new Button { Content="Seçilenden sonra ekle",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,6,4) };
        var remove=new Button { Content="Seçili noktayı sil",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,0,4) };
        AutomationProperties.SetHelpText(insert,"Girilen koordinatlarda seçili çizgi noktasından sonra yeni nokta ekler.");
        AutomationProperties.SetHelpText(remove,"Yalnız seçili çizgi noktasını siler; en az iki çizgi noktası kalmalıdır.");
        insert.Click+=(_,_)=> { if(TryInsert())DialogResult=true; };remove.Click+=(_,_)=> { if(TryRemove())DialogResult=true; };
        editActions.Children.Add(insert);editActions.Children.Add(remove);body.Children.Add(editActions);
        Status.Text="Son nokta düzenlemesi ana pencerede geri alınabilir; bekleyen öneri kaldırılır.";AutomationProperties.SetName(Status,"Nokta düzenleme durumu");AutomationProperties.SetLiveSetting(Status,AutomationLiveSetting.Polite);body.Children.Add(Status);
        PointSelector.SelectionChanged+=(_,_)=> { if(PointSelector.SelectedItem is Choice c) { XInput.Text=MeasurementInput.Format(c.Pixel.X);YInput.Text=MeasurementInput.Format(c.Pixel.Y); } };
        for(int i=owner.CanvasForVerification.ReferencePointCount;i<snapshot.Length;i++)PointSelector.Items.Add(new Choice(i,snapshot[i]));
        if(PointSelector.Items.Count>0)
        {
            var nearest=owner.CanvasForVerification.NearestTracePointIndex();
            PointSelector.SelectedItem=PointSelector.Items.Cast<Choice>().FirstOrDefault(c=>c.Index==nearest);
        }
    }
    internal bool FindNextDuplicate()
    {
        if(!IsCurrent())return false;
        var seen=new Dictionary<Point,int>();var duplicates=new List<(int Index,int First)>();
        for(var i=owner.CanvasForVerification.ReferencePointCount;i<snapshot.Length;i++)
        {
            if(seen.TryGetValue(snapshot[i],out var first))duplicates.Add((i,first));else seen.Add(snapshot[i],i);
        }
        if(duplicates.Count==0) { Status.Text="Çizgi noktalarında aynı koordinat bulunmadı. Bu, bütün kesişimlerin veya kalıp doğruluğunun denetlendiği anlamına gelmez.";return false; }
        var chosen=duplicates.FirstOrDefault(x=>x.Index>lastDuplicateIndex,duplicates[0]);lastDuplicateIndex=chosen.Index;
        PointSelector.SelectedItem=PointSelector.Items.Cast<Choice>().Single(c=>c.Index==chosen.Index);
        Status.Text=$"Nokta {chosen.Index+1}, nokta {chosen.First+1} ile aynı koordinatta. Seçili noktayı inceleyip taşıyabilir veya silebilirsiniz. Tekrarı bul diğer eşleşmeye gider.";
        return true;
    }
    internal bool TryApplyEdit()
    {
        if(!IsCurrent())return false;
        if(PointSelector.SelectedItem is not Choice choice || !MeasurementInput.TryParseMillimeters(XInput.Text,out var x) || !MeasurementInput.TryParseMillimeters(YInput.Text,out var y) ||
            !owner.CanvasForVerification.TryMoveTracePoint(choice.Index,new Point(x,y)))
        { Status.Text="Nokta değişmedi. Görüntü içindeki yeni koordinatları seçin; komşu noktayla aynı konum kullanılamaz.";return false; }
        return true;
    }
    internal bool TryInsert()
    {
        if(!IsCurrent())return false;
        if(PointSelector.SelectedItem is not Choice choice || !MeasurementInput.TryParseMillimeters(XInput.Text,out var x) || !MeasurementInput.TryParseMillimeters(YInput.Text,out var y) ||
            !owner.CanvasForVerification.TryInsertTracePointAfter(choice.Index,new Point(x,y)))
        { Status.Text="Nokta eklenemedi. Görüntü içindeki farklı bir konum seçin ve nokta sınırını kontrol edin.";return false; }
        return true;
    }
    internal bool TryRemove()
    {
        if(!IsCurrent())return false;
        if(PointSelector.SelectedItem is not Choice choice || !owner.CanvasForVerification.TryRemoveTracePoint(choice.Index))
        { Status.Text="Nokta silinemedi. En az iki çizgi noktası kalmalı ve komşular aynı konumda olmamalıdır.";return false; }
        return true;
    }
    private bool IsCurrent()
    {
        if(owner.DraftRevision==revision && owner.CanvasForVerification.Points.SequenceEqual(snapshot))return true;
        Status.Text="Taslak değişti; pencereyi kapatıp yeniden açın.";return false;
    }
}
