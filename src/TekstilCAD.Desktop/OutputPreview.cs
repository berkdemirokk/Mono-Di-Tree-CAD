using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Desktop;

internal sealed record PreviewPath(IReadOnlyList<Point2> Points, bool Closed);
internal sealed record PreviewDrawing(IReadOnlyList<PreviewPath> Paths, Bounds2 Bounds);

/// <summary>Reads the actual prepared artifact. Never substitutes source geometry or sends device commands.</summary>
internal sealed class OutputPreview : FrameworkElement
{
    private const int MaximumBytes=8*1024*1024, MaximumPoints=50_000;
    private PreviewDrawing? drawing;
    internal bool HasGeometry => drawing is not null;
    internal Bounds2? PreviewBounds => drawing?.Bounds;
    internal int PathCount => drawing?.Paths.Count??0;
    public OutputPreview()
    {
        MinHeight=180;ClipToBounds=true;Focusable=true;
        IsKeyboardFocusedChanged+=(_,_)=>InvalidateVisual();
        AutomationProperties.SetName(this,"Hazırlanan dosyanın geometrik önizlemesi. Geometri yok.");
        AutomationProperties.SetHelpText(this,"Çıktı dosyasından okunur, ekrana sığdırılır. Fiziksel baskı veya makine uygunluk onayı değildir.");
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new PreviewPeer(this);
    private sealed class PreviewPeer(OutputPreview owner):FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore()=>nameof(OutputPreview);
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Custom;
    }
    internal void Clear() { drawing=null;SetAccessibleSummary("Hazırlanan dosyanın geometrik önizlemesi. Geometri yok.");InvalidateVisual(); }
    internal string ShowJob(SimulationJob job)
    {
        Clear();drawing=Read(job);InvalidateVisual();
        SetAccessibleSummary($"Hazırlanan dosyanın geometrik önizlemesi. {drawing.Paths.Count} çizim yolu. Genişlik {MeasurementInput.Format(drawing.Bounds.Width)} mm, yükseklik {MeasurementInput.Format(drawing.Bounds.Height)} mm.");
        return $"Gerçek çıktı · {drawing.Paths.Count} çizim yolu · {MeasurementInput.Format(drawing.Bounds.Width)} × {MeasurementInput.Format(drawing.Bounds.Height)} mm\nEkrana sığdırılmış geometri; renklerin üretim anlamı ve fiziksel 1:1 baskı doğrulanmaz.";
    }
    private void SetAccessibleSummary(string value)
    {
        var previous=AutomationProperties.GetName(this);
        if(previous==value)return;
        AutomationProperties.SetName(this,value);
        if(UIElementAutomationPeer.CreatePeerForElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty,previous,value);
    }
    internal static PreviewDrawing Read(SimulationJob job)
    {
        job.Profile.Validate();
        if(job.PointCount>MaximumPoints)throw new InvalidDataException("Görsel önizleme 50.000 nokta sınırını aşıyor; kısmi çizim gösterilmedi.");
        using var snapshot=new FileStream(job.OutputPath,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(snapshot.Length>MaximumBytes)throw new InvalidDataException("Görsel önizleme 8 MB sınırını aşıyor; dosya değiştirilmedi.");
        using var bytes = new MemoryStream();
        var buffer=new byte[81920];
        int read;
        while((read=snapshot.Read(buffer,0,Math.Min(buffer.Length,MaximumBytes+1-(int)bytes.Length)))>0)
        {
            bytes.Write(buffer,0,read);
            if(bytes.Length>MaximumBytes)throw new InvalidDataException("Görsel önizleme 8 MB sınırını aşıyor; dosya değiştirilmedi.");
        }
        if(bytes.Length>MaximumBytes)throw new InvalidDataException("Görsel önizleme 8 MB sınırını aşıyor; dosya değiştirilmedi.");
        bytes.Position=0;
        var hash=Convert.ToHexString(SHA256.HashData(bytes));
        if(!string.Equals(hash,job.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Dosya iş kaydından sonra değişmiş. Görsel önizleme gösterilmedi; dosyayı yeniden hazırlayın.");
        bytes.Position=0;
        List<PreviewPath> paths;
        if(job.Format==JobFormat.Dxf)
        {
            // DXF emitted by this application is explicitly in millimetres. Do not apply Profile.Scale again.
            var imported=DxfFormat.Import(bytes,Path.GetFileNameWithoutExtension(job.OutputPath),1);
            if(imported.Warnings.Any(w=>!w.StartsWith("DXF birim başlığı mevcut;",StringComparison.Ordinal)
                && !w.StartsWith("DXF renk bilgileri projede korunmaz;",StringComparison.Ordinal)
                && !string.Equals(w,"ASTM D6673 tarzı alışverişte serileme tablosu ayrı bir ASCII dosyasında olabilir; bu DXF işlemi o dosyayı aramaz veya içe almaz.",StringComparison.Ordinal)
                && !string.Equals(w,"DXF kaynak geometri tanıtıcıları veya nesne bağlantıları projede korunmaz; kaynak kayıt ilişkilerini doğrulayın.",StringComparison.Ordinal)))
                throw new InvalidDataException("Bu DXF için kayıpsız görsel önizleme desteklenmiyor. Aktarım notları: "+string.Join(" ",imported.Warnings));
            paths=imported.Project.Pieces.Select(p=>new PreviewPath(p.Points.ToArray(),p.Closed)).ToList();
            if(paths.Sum(p=>p.Points.Count)!=job.PointCount)throw new InvalidDataException("Önizleme nokta sayısı iş kaydıyla eşleşmiyor.");
        }
        else if(job.Format==JobFormat.Hpgl)
        {
            using var reader=new StreamReader(bytes,Encoding.UTF8,true,4096,leaveOpen:true);
            paths=ReadGeneratedHpgl(reader.ReadToEnd(),job.Profile.UnitsPerMillimeter);
        }
        else throw new InvalidDataException("Bu dosya biçimi için görsel önizleme desteklenmiyor.");
        if(paths.Count!=job.PieceCount || paths.Count==0)throw new InvalidDataException("Önizleme parça sayısı iş kaydıyla eşleşmiyor.");
        var pointCount=paths.Sum(p=>p.Points.Count);
        if(pointCount>MaximumPoints)throw new InvalidDataException("Görsel önizleme 50.000 nokta sınırını aşıyor; kısmi çizim gösterilmedi.");
        var all=paths.SelectMany(p=>p.Points).ToArray();
        if(all.Any(p=>!p.IsFinite || Math.Abs(p.X)>1e8 || Math.Abs(p.Y)>1e8))throw new InvalidDataException("Önizleme koordinatı desteklenen mm alanını aşıyor.");
        return new(paths,TekstilCAD.Core.Geometry.Bounds(all));
    }
    private static List<PreviewPath> ReadGeneratedHpgl(string text,double unitsPerMillimeter)
    {
        var tokens=text.Split(';',MaximumPoints*3+6,StringSplitOptions.None);var index=0;var count=0;var paths=new List<PreviewPath>();
        string Next()=>index<tokens.Length?tokens[index++].Trim():throw new InvalidDataException("HPGL komutu eksik.");
        if(Next()!="IN"||Next()!="SP1"||Next()!="PA")throw new InvalidDataException("Görsel önizleme yalnız TekstilCAD mutlak koordinatlı HPGL alt kümesini destekler.");
        while(index<tokens.Length)
        {
            var token=Next();
            if(token=="SP0")
            {
                if(tokens.Skip(index).Any(s=>!string.IsNullOrWhiteSpace(s)))throw new InvalidDataException("HPGL sonunda desteklenmeyen komut var.");
                return paths;
            }
            if(!token.StartsWith("PU",StringComparison.Ordinal)||token.Length==2)throw new InvalidDataException("HPGL başlangıç noktası desteklenmiyor.");
            var start=Coordinates(token[2..]);
            if(start.Count!=1)throw new InvalidDataException("HPGL başlangıç noktası geçersiz.");
            var pen=Next();
            if(!pen.StartsWith("PD",StringComparison.Ordinal))throw new InvalidDataException("HPGL çizim komutu desteklenmiyor.");
            var points=new List<Point2>(start);points.AddRange(Coordinates(pen[2..]));
            if(points.Count<2||Next()!="PU")throw new InvalidDataException("HPGL çizim yolu tamamlanmamış.");
            count+=points.Count;
            if(count>MaximumPoints)throw new InvalidDataException("Görsel önizleme 50.000 nokta sınırını aşıyor.");
            // The exporter writes the final return segment explicitly; retain it as actual pen motion.
            paths.Add(new(points,false));
        }
        throw new InvalidDataException("HPGL bitiş komutu bulunamadı.");

        List<Point2> Coordinates(string value)
        {
            var values=value.Split(',',MaximumPoints*2+2,StringSplitOptions.None);
            if(values.Length<2||values.Length%2!=0||values.Length>MaximumPoints*2)throw new InvalidDataException("HPGL koordinat listesi geçersiz veya çok büyük.");
            var points=new List<Point2>(values.Length/2);
            for(var i=0;i<values.Length;i+=2)
            {
                if(!int.TryParse(values[i],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var x)
                    ||!int.TryParse(values[i+1],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var y))throw new InvalidDataException("HPGL yalnız tamsayı koordinatlarla önizlenebilir.");
                points.Add(new(x/unitsPerMillimeter,y/unitsPerMillimeter));
            }
            return points;
        }
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(StudioTheme.Background,new Pen(StudioTheme.Border,1),new Rect(RenderSize));
        if(IsKeyboardFocused && ActualWidth>8 && ActualHeight>8)
            dc.DrawRectangle(null,new Pen(StudioTheme.Accent,2) { DashStyle=DashStyles.Dash },new Rect(3,3,ActualWidth-6,ActualHeight-6));
        if(drawing is null||ActualWidth<=48||ActualHeight<=48)return;
        var b=drawing.Bounds;var zoom=Math.Min((ActualWidth-48)/Math.Max(b.Width,0.001),(ActualHeight-48)/Math.Max(b.Height,0.001));
        var left=(ActualWidth-b.Width*zoom)/2;var top=(ActualHeight-b.Height*zoom)/2;
        Point Screen(Point2 p)=>new(left+(p.X-b.MinX)*zoom,top+(p.Y-b.MinY)*zoom);
        var pen=new Pen(StudioTheme.Accent,1.4);
        foreach(var path in drawing.Paths)
        {
            var geometry=new StreamGeometry();
            using(var context=geometry.Open())
            {
                context.BeginFigure(Screen(path.Points[0]),false,path.Closed);
                context.PolyLineTo(path.Points.Skip(1).Select(Screen).ToArray(),true,false);
            }
            geometry.Freeze();dc.DrawGeometry(null,pen,geometry);
        }
    }
}
