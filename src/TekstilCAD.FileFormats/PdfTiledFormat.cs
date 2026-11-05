using System.Globalization;
using System.Text;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>A4 portrait inspection sheets, ordered across each row from the top left.</summary>
public static class PdfTiledFormat
{
    // Reserve a 10 mm printable perimeter plus separate 10 mm strips for
    // labels and calibration; neither strip overlaps the joining frame.
    private const double Scale=72.0/25.4, Margin=10, FrameY=20, TileWidth=190, TileHeight=257;
    private const int MaximumPages=100;
    private const long MaximumPointPages=2_000_000;
    private static string F(double value)=>value.ToString("0.######",CultureInfo.InvariantCulture);

    public static void Export(ProjectDocument project,string path,ProjectFileVersion expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(project);ArgumentNullException.ThrowIfNull(expectedVersion);
        ProjectStorage.Validate(project);var copy=project.Clone();ProjectStorage.Validate(copy);
        if(copy.Pieces.Count==0)throw new InvalidDataException("A4 PDF için en az bir parça gerekir.");
        // Include every grainline endpoint in the physical sheet bounds.
        // A line outside its contour must never be silently clipped away.
        var points=copy.Pieces.SelectMany(p=>p.Grainline is { } line
            ? p.Points.Concat([line.Start,line.End]) : p.Points).ToArray();
        var hasGrainlines=copy.Pieces.Any(p=>p.Grainline is not null);
        var reviewCount=copy.Pieces.Count(p=>p.Grainline?.NeedsReview==true);
        // The arrowhead extends at most 0.7 mm sideways; the existing 1 mm
        // perimeter covers it, including the 0.5 pt stroke. These are annotations.
        var arrows=new Dictionary<Guid,GrainlineDisplay.Arrow>();
        foreach(var piece in copy.Pieces.Where(p=>p.Grainline is not null))
        {
            try { arrows.Add(piece.Id,GrainlineDisplay.CreateArrow(piece.Grainline!.Start,piece.Grainline.End,2)); }
            catch(ArgumentException ex) { throw new InvalidDataException("A4 PDF iplik yönü oku güvenilir gösterilemiyor; çıktı hazırlanmadı.",ex); }
        }
        var markCount=copy.Pieces.Sum(p=>(long)(p.Notches?.Count ?? 0));
        // One millimetre clearance keeps exterior contours away from joins.
        var minX=points.Min(p=>p.X)-1;var minY=points.Min(p=>p.Y)-1;
        var width=points.Max(p=>p.X)-minX+1;var height=points.Max(p=>p.Y)-minY+1;
        if(!double.IsFinite(width) || !double.IsFinite(height) || width>10000 || height>10000)
            throw new InvalidDataException("A4 PDF sınırları aşıldı; kalıpları daha küçük gruplar halinde dışa aktarın.");
        var cols=(int)Math.Ceiling(width/TileWidth);var rows=(int)Math.Ceiling(height/TileHeight);
        var pageCount=(long)cols*rows;
        if(pageCount>MaximumPages || pageCount*(points.Length+4*markCount+3L*arrows.Count)>MaximumPointPages)
            throw new InvalidDataException("A4 PDF sayfa veya nokta sınırını aşıyor; kalıpları daha küçük gruplar halinde dışa aktarın.");
        var streams=new List<string>((int)pageCount);
        for(var row=0;row<rows;row++)for(var col=0;col<cols;col++)
        {
            var body=new StringBuilder();
            // The grey frame is the exact joining edge; only contour ink inside
            // the frame is printed. Neighbouring frames share the same model edge.
            body.Append($"0.65 G 0.3 w {F(Margin*Scale)} {F(FrameY*Scale)} {F(TileWidth*Scale)} {F(TileHeight*Scale)} re S\n");
            var identity=copy.Pieces.Count==1 ? $"  ID {copy.Pieces[0].Id:D}" : "";
            body.Append($"BT /F1 8 Tf {F(Margin*Scale)} {F(282*Scale)} Td (SATIR {row+1}/{rows}  SUTUN {col+1}/{cols}  SAYFA {row*cols+col+1}/{pageCount}  YAZDIRMA 100%{identity}) Tj ET\n");
            body.Append($"0 G 0.5 w {F(10*Scale)} {F(15*Scale)} m {F(60*Scale)} {F(15*Scale)} l S BT /F1 8 Tf {F(63*Scale)} {F(14*Scale)} Td (50 MM KONTROL) Tj ET\n");
            if(hasGrainlines || markCount>0)
            {
                var legend=hasGrainlines && markCount>0 ? "KESIK:IPLIK  ARTI:ISARET KESIM DEGIL" : hasGrainlines ? "KESIK: IPLIK YONU" : "ARTI: GORSEL ISARET KESIM DEGIL";
                body.Append($"BT /F1 8 Tf {F(110*Scale)} {F(14*Scale)} Td ({legend}) Tj ET\n");
            }
            if(hasGrainlines)
                body.Append($"BT /F1 8 Tf {F(10*Scale)} {F(10*Scale)} Td (OK: BASLANGICTAN BITISE. KONTROL BEKLEYEN: {reviewCount}. URETIM ONAYI DEGIL.) Tj ET\n");
            body.Append($"q {F(Margin*Scale)} {F(FrameY*Scale)} {F(TileWidth*Scale)} {F(TileHeight*Scale)} re W n 0 G 0.5 w 1 J 1 j\n");
            foreach(var piece in copy.Pieces)
            {
                string? first=null,previous=null;
                foreach(var p in piece.Points)
                {
                    var x=(Margin+p.X-minX-col*TileWidth)*Scale;
                    var y=(FrameY+TileHeight+row*TileHeight-(p.Y-minY))*Scale;
                    var point=F(x)+" "+F(y);
                    if(previous==point)throw new InvalidDataException("A4 PDF koordinat hassasiyeti bir kenarı sıfıra indirdi; çıktı oluşturulmadı.");
                    body.Append(point).Append(first is null ? " m\n" : " l\n");first??=point;previous=point;
                }
                if(piece.Closed && piece.Points[0]!=piece.Points[^1] && first==previous)
                    throw new InvalidDataException("A4 PDF koordinat hassasiyeti kapanış kenarını sıfıra indirdi; çıktı oluşturulmadı.");
                if(piece.Closed)body.Append("h\n");body.Append("S\n");
            }
            if(hasGrainlines)
            {
                body.Append("[4 2] 0 d 0 G 0.5 w\n");
                foreach(var piece in copy.Pieces)
                {
                    if(piece.Grainline is not { } line)continue;
                    string Map(Point2 p) => F((Margin+p.X-minX-col*TileWidth)*Scale)+" "+F((FrameY+TileHeight+row*TileHeight-(p.Y-minY))*Scale);
                    var first=Map(line.Start);var last=Map(line.End);
                    if(first==last)throw new InvalidDataException("A4 PDF koordinat hassasiyeti iplik yönünü sıfıra indirdi; çıktı oluşturulmadı.");
                    body.Append(first).Append(" m ").Append(last).Append(" l S\n");
                    var arrow=arrows[piece.Id];var left=Map(arrow.Left);var right=Map(arrow.Right);
                    if(left==last || right==last || left==right)
                        throw new InvalidDataException("A4 PDF iplik oku çıktı hassasiyetinde kayboluyor; dosya hazırlanmadı.");
                    body.Append("[] 0 d ").Append(left).Append(" m ").Append(last).Append(" l ").Append(right).Append(" l S\n[4 2] 0 d\n");
                    if(line.NeedsReview)
                    {
                        // Inside the same page clip. Every sheet also carries the
                        // uncropped total above, even when this local label is clipped.
                        var label=Map(line.Start);
                        body.Append("BT /F1 8 Tf ").Append(label).Append(" Td (KONTROL BEKLIYOR) Tj ET\n");
                    }
                }
                body.Append("[] 0 d\n");
            }
            if(markCount>0)
            {
                // Crosses are fixed-size visual location indicators, not notch geometry.
                // They use the same model transform and page clip as the contour.
                body.Append("0 G 0.5 w\n");
                const double radius=0.75;
                foreach(var piece in copy.Pieces)
                foreach(var mark in piece.Notches ?? [])
                {
                    var x=Margin+mark.Position.X-minX-col*TileWidth;
                    var y=FrameY+TileHeight+row*TileHeight-(mark.Position.Y-minY);
                    body.Append(F((x-radius)*Scale)).Append(" ").Append(F(y*Scale)).Append(" m ")
                        .Append(F((x+radius)*Scale)).Append(" ").Append(F(y*Scale)).Append(" l S\n")
                        .Append(F(x*Scale)).Append(" ").Append(F((y-radius)*Scale)).Append(" m ")
                        .Append(F(x*Scale)).Append(" ").Append(F((y+radius)*Scale)).Append(" l S\n");
                }
            }
            body.Append("Q\n");streams.Add(body.ToString());
        }
        var pdf=new StringBuilder("%PDF-1.4\n");var offsets=new List<int> { 0 };
        void Object(string value) { offsets.Add(pdf.Length);pdf.Append(offsets.Count-1).Append(" 0 obj\n").Append(value).Append("\nendobj\n"); }
        Object("<< /Type /Catalog /Pages 2 0 R >>");
        var kids=string.Join(" ",Enumerable.Range(0,streams.Count).Select(i=>$"{4+2*i} 0 R"));
        Object($"<< /Type /Pages /Kids [{kids}] /Count {streams.Count} >>");
        Object("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        for(var i=0;i<streams.Count;i++)
        {
            Object($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(210*Scale)} {F(297*Scale)}] /Resources << /Font << /F1 3 0 R >> >> /Contents {5+2*i} 0 R >>");
            Object($"<< /Length {streams[i].Length} >>\nstream\n{streams[i]}endstream");
        }
        var xref=pdf.Length;pdf.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach(var offset in offsets.Skip(1))pdf.Append(offset.ToString("D10",CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        ProjectStorage.AtomicWriteIfUnchanged(path,pdf.ToString(),expectedVersion);
    }
}
