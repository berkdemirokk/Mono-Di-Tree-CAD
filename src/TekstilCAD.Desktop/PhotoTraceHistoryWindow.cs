using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Read-only historical evidence captured when a photo trace was first added.</summary>
internal sealed class PhotoTraceHistoryWindow : Window
{
    internal ListBox RecordsForVerification { get; } = new();
    internal TextBox DetailsForVerification { get; } = new() { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    internal PhotoTraceHistoryWindow(Window owner,ProjectDocument document)
    {
        Owner=owner;Title="Fotoğraf taslağı geçmişi";Width=740;Height=680;MinWidth=480;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var snapshot=document.Clone();var records=snapshot.PhotoTraceHistory ?? [];
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var close=new Button { Content="Kapat",Padding=new Thickness(12,7,12,7),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };close.Click+=(_,_)=>Close();DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);
        var intro=new TextBlock { Text="Bu, pencere açıldığında alınmış tarihsel kayıttır; fotoğraf dosyası projede saklanmaz. İlk çizgiyle eşleşme ölçü, beden veya üretim onayı değildir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) };DockPanel.SetDock(intro,Dock.Top);root.Children.Add(intro);
        RecordsForVerification.Height=100;RecordsForVerification.ItemsSource=records.Select((record,index)=>new Row(index+1,record)).ToArray();
        AutomationProperties.SetName(RecordsForVerification,"Fotoğraf taslağı geçmiş kayıtları");DockPanel.SetDock(RecordsForVerification,Dock.Top);root.Children.Add(RecordsForVerification);
        AutomationProperties.SetName(DetailsForVerification,"Seçili fotoğraf geçmişi ayrıntıları");root.Children.Add(DetailsForVerification);
        RecordsForVerification.SelectionChanged+=(_,_)=>
        {
            if(RecordsForVerification.SelectedItem is not Row row) { DetailsForVerification.Text="Kayıt seçin.";return; }
            var r=row.Record;var piece=snapshot.Pieces.FirstOrDefault(x=>x.Id==r.PieceId);
            var same=piece is not null && !piece.Closed && piece.Points.SequenceEqual(r.GeneratedPoints);
            var b=new StringBuilder();b.Append("Parça kimliği: ").Append(r.PieceId.ToString("D")).Append("\n");
            b.Append(same ? "İlk açık geometriyle eşleşiyor; doğrulama değildir." : "İlk geometriyle eşleşmiyor veya kaynak parça silinmiş.").Append("\n");
            b.Append("Fotoğraf SHA-256: ").Append(r.SourceSha256).Append("\nBayt: ").Append(r.SourceByteLength).Append(" · EXIF yönü: ").Append(r.SourceOrientation).Append("\n");
            b.Append("Yönlendirilmiş kaynak: ").Append(r.SourceWidth).Append(" × ").Append(r.SourceHeight).Append(" px · Önizleme: ").Append(r.PreviewWidth).Append(" × ").Append(r.PreviewHeight).Append(" px\n");
            b.Append("Referans: (").Append(MeasurementInput.Format(r.ReferenceStart.X)).Append(", ").Append(MeasurementInput.Format(r.ReferenceStart.Y)).Append(") → (").Append(MeasurementInput.Format(r.ReferenceEnd.X)).Append(", ").Append(MeasurementInput.Format(r.ReferenceEnd.Y)).Append(") px · ").Append(MeasurementInput.Format(r.ReferenceLengthMm)).Append(" mm\n");
            b.Append("İlk çizim noktaları (önizleme px → CAD mm):\n");
            for(int i=0;i<r.TracePixels.Count;i++)b.Append(i+1).Append(": (").Append(MeasurementInput.Format(r.TracePixels[i].X)).Append(", ").Append(MeasurementInput.Format(r.TracePixels[i].Y)).Append(") → (").Append(MeasurementInput.Format(r.GeneratedPoints[i].X)).Append(", ").Append(MeasurementInput.Format(r.GeneratedPoints[i].Y)).Append(")\n");
            DetailsForVerification.Text=b.ToString();
        };
        if(records.Count>0)RecordsForVerification.SelectedIndex=0;else DetailsForVerification.Text="Fotoğraf taslağı geçmişi yok.";
    }
    private sealed record Row(int Number,PhotoTraceProvenance Record)
    { public override string ToString()=> $"Kayıt {Number} · {Record.PieceId:D}"; }
}
