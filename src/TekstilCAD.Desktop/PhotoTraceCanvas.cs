using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TekstilCAD.Desktop;

/// <summary>
/// Manual point selection on a detached, already-oriented preview. Coordinates are
/// continuous pixel-centre coordinates (0..width-1, 0..height-1), with Y downward.
/// Optional contrast suggestions are previews; no physical scale or garment construction is performed here.
/// </summary>
internal sealed class PhotoTraceCanvas : FrameworkElement
{
    internal const int MaximumPoints=512;
    internal int ReferencePointCount { get; private set; } = 2;
    private readonly List<Point> points=[];
    private BitmapSource? image;
    private int sourceWidth,sourceHeight;
    private Point cursor;
    private Point? edgeSuggestion;
    private List<Point>? pathSuggestion,pathUndo;
    private Point[]? pathOriginal;
    private int pathStartIndex;
    private int pathOriginalTotalCount;
    private Point? pathClosingTarget;
    private Point[]? topologyWarningEdges;
    private bool topologyWarning;
    private bool photoOnly;
    internal bool PhotoOnly
    {
        get=>photoOnly;
        set { if(photoOnly==value)return;photoOnly=value;InvalidateVisual(); }
    }
    private double zoom=1;
    private Point zoomCenter=new(0.5,0.5);

    internal PhotoTraceCanvas()
    {
        Focusable=true;MinHeight=160;ClipToBounds=true;
        AutomationProperties.SetName(this,"Referans fotoğrafta elle nokta seçimi");
        AutomationProperties.SetHelpText(this,"Sol tık nokta ekler; sağ tık yalnız inceleme konumunu seçer ve yakınlaştırılmış görünümü oraya taşır. Sarı halka Düzelt penceresinde ilk seçilecek en yakın çizgi noktasını gösterir. Klavyeyle ok tuşları imleci taşır, Shift ile on piksel ilerler, Enter veya boşluk nokta ekler, Backspace son noktayı kaldırır. Yakınlaştırmada imleç kenara ulaşınca görünüm onu izler. Çizim otomatik kalıp değildir.");
    }

    internal BitmapSource? Image
    {
        get=>image;
        set
        {
            if(ReferenceEquals(image,value))return;
            if(value is null)image=null;
            else
            {
                // Never retain mutable external bitmap ownership. Frozen sources
                // are safe to share; otherwise clone and freeze a snapshot.
                var snapshot=value.IsFrozen ? value : value.CloneCurrentValue();
                if(!snapshot.CanFreeze)throw new ArgumentException("Görsel güvenli şekilde sabitlenemedi.",nameof(value));
                if(!snapshot.IsFrozen)snapshot.Freeze();
                if(snapshot.PixelWidth<1 || snapshot.PixelHeight<1)throw new ArgumentException("Görsel boyutu geçersiz.",nameof(value));
                image=snapshot;
            }
            sourceWidth=image?.PixelWidth ?? 0;sourceHeight=image?.PixelHeight ?? 0;
            points.Clear();edgeSuggestion=null;pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;pathUndo=null;cursor=image is null ? default : new Point((image.PixelWidth-1)/2d,(image.PixelHeight-1)/2d);
            zoom=1;zoomCenter=new Point(0.5,0.5);
            InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    internal void SetSourceAspect(int width,int height)
    {
        if(image is null || width<image.PixelWidth || height<image.PixelHeight || width>12_000 || height>12_000 || (long)width*height>24_000_000)
            throw new ArgumentOutOfRangeException(nameof(width),"Kaynak görsel boyutu geçersiz.");
        sourceWidth=width;sourceHeight=height;InvalidateVisual();
    }

    internal IReadOnlyList<Point> Points=>points.AsReadOnly();
    internal Point CursorForVerification=>cursor;
    internal int? NearestTracePointIndex()
    {
        if(image is null || points.Count<=ReferencePointCount)return null;
        // ImageBounds uses the source aspect, which can differ slightly from the
        // rounded preview aspect. Compare in the same proportions as the view.
        var xScale=(double)sourceWidth/image.PixelWidth;var yScale=(double)sourceHeight/image.PixelHeight;
        var best=ReferencePointCount;var bestDistance=double.PositiveInfinity;
        for(var i=ReferencePointCount;i<points.Count;i++)
        {
            var dx=(points[i].X-cursor.X)*xScale;var dy=(points[i].Y-cursor.Y)*yScale;
            var distance=dx*dx+dy*dy;
            if(distance<bestDistance) { bestDistance=distance;best=i; }
        }
        return best;
    }
    internal Point? EdgeSuggestion=>edgeSuggestion;
    internal IReadOnlyList<Point>? PathSuggestion=>pathSuggestion?.AsReadOnly();
    internal bool ProposedPathHasClosingEdge=>pathSuggestion is not null && pathClosingTarget.HasValue;
    internal double Zoom=>zoom;
    internal bool DenseTraceView=>points.Count-ReferencePointCount>80;
    internal event EventHandler? PointsChanged;
    internal void SetReferencePointCount(int count)
    {
        if(count is not (2 or 4) || points.Count!=0)throw new InvalidOperationException("Referans türü yalnız noktalar temizken değiştirilebilir.");
        ReferencePointCount=count;edgeSuggestion=null;pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;pathUndo=null;InvalidateVisual();
    }

    // View-only state: never change selected pixel coordinates or draft revision.
    internal void ZoomAtCursor(double factor)
    {
        if(image is null || !double.IsFinite(factor) || factor<=0)return;
        var next=Math.Clamp(zoom*factor,1,8);
        if(next==1)zoomCenter=new Point(0.5,0.5);
        else zoomCenter=new Point((cursor.X+0.5)/image.PixelWidth,(cursor.Y+0.5)/image.PixelHeight);
        zoom=next;InvalidateVisual();
    }
    internal void ResetZoom() { zoom=1;zoomCenter=new Point(0.5,0.5);InvalidateVisual(); }
    internal bool TryInspectAtView(Point viewPoint,Size viewport)
    {
        if(image is null || !TryMapViewToPixel(viewPoint,viewport,out var pixel))return false;
        cursor=pixel;
        if(zoom>1)zoomCenter=new Point((pixel.X+0.5)/image.PixelWidth,(pixel.Y+0.5)/image.PixelHeight);
        InvalidateVisual();return true;
    }
    internal void ClearPathSuggestion() { pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;InvalidateVisual(); }
    internal bool SuggestLastEdge()
    {
        edgeSuggestion=null;pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;
        if(image is not null && points.Count>ReferencePointCount && PhotoEdgeSuggestion.TryFind(image,points[^1],out var found))edgeSuggestion=found;
        InvalidateVisual();return edgeSuggestion.HasValue;
    }
    internal bool ApplyEdgeSuggestion()
    {
        if(edgeSuggestion is not Point proposed || points.Count<=ReferencePointCount || proposed==points[^1] || (points.Count>ReferencePointCount+1 && proposed==points[^2]))return false;
        pathUndo=points.ToList();points[^1]=proposed;cursor=proposed;edgeSuggestion=null;pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);return true;
    }

    internal bool SetPathSuggestion(Point start,Point end,IReadOnlyList<Point> candidate)
        =>SetPathSegments(points.Count-2,[start,end],[candidate]);

    internal bool SetAllPathSuggestions(IReadOnlyList<Point> anchors,IReadOnlyList<IReadOnlyList<Point>> candidates,bool includeClosing=false)
        =>SetPathSegments(ReferencePointCount,anchors,candidates,includeClosing);

    /// <summary>
    /// Shows one explicitly chosen pixel contour as a detached proposal. It does
    /// not silently replace existing trace points or claim that the contour is valid.
    /// </summary>
    internal bool SetRegionContourSuggestion(IReadOnlyList<Point> contour)
    {
        ArgumentNullException.ThrowIfNull(contour);
        if(image is null || points.Count!=ReferencePointCount || contour.Count<3 ||
           contour.Count>MaximumPoints-ReferencePointCount)return false;
        var candidate=contour.ToArray();
        if(candidate[0]==candidate[^1])return false; // closing edge is held separately
        for(var i=0;i<candidate.Length;i++)
        {
            var p=candidate[i];
            if(!double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X<0 || p.Y<0 ||
               p.X>image.PixelWidth-1 || p.Y>image.PixelHeight-1 || (i>0 && p==candidate[i-1]))return false;
        }
        pathSuggestion=candidate.ToList();pathOriginal=[];pathStartIndex=ReferencePointCount;
        pathOriginalTotalCount=points.Count;pathClosingTarget=candidate[0];edgeSuggestion=null;
        topologyWarningEdges=null;topologyWarning=false;InvalidateVisual();return true;
    }

    private bool SetPathSegments(int startIndex,IReadOnlyList<Point> anchors,IReadOnlyList<IReadOnlyList<Point>> candidates,bool includeClosing=false)
    {
        pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;edgeSuggestion=null;InvalidateVisual();
        if(image is null || startIndex<ReferencePointCount || anchors.Count<(includeClosing ? 3 : 2) || anchors.Count>17 || candidates.Count!=anchors.Count-1+(includeClosing ? 1 : 0) ||
           points.Count!=startIndex+anchors.Count || !points.Skip(startIndex).SequenceEqual(anchors))return false;
        Point Rounded(Point p)=>new(Math.Round(p.X,MidpointRounding.AwayFromZero),Math.Round(p.Y,MidpointRounding.AwayFromZero));
        var simplified=new List<Point> { anchors[0] };
        for(int segment=0;segment<candidates.Count;segment++)
        {
            var candidate=candidates[segment];var closing=includeClosing && segment==candidates.Count-1;var end=closing ? anchors[0] : anchors[segment+1];
            if(candidate is null || candidate.Count<2 || candidate.Count>4096 || candidate[0]!=Rounded(anchors[segment]) || candidate[^1]!=Rounded(end))return false;
            for(int i=1;i<candidate.Count-1;i++)
            {
                var p=candidate[i];var a=candidate[i-1];var b=candidate[i+1];
                if(!double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X<0 || p.Y<0 || p.X>image.PixelWidth-1 || p.Y>image.PixelHeight-1)return false;
                // The search uses rounded pixel endpoints, but the accepted anchor can be subpixel.
                // Keep its adjacent pixel so collinearity is never inferred from a different endpoint.
                var nextToSubpixelAnchor=(i==1 && anchors[segment]!=candidate[0]) || (i==candidate.Count-2 && end!=candidate[^1]);
                if(!nextToSubpixelAnchor && (p.X-a.X)*(b.Y-p.Y)==(p.Y-a.Y)*(b.X-p.X) && (p.X-a.X)*(b.X-p.X)+(p.Y-a.Y)*(b.Y-p.Y)>0)continue;
                if(p==simplified[^1] || (closing && p==anchors[0]))return false;simplified.Add(p);
                if(startIndex+simplified.Count>MaximumPoints)return false;
            }
            if(end==simplified[^1])return false;if(!closing)simplified.Add(end);
        }
        if(simplified.Count<=anchors.Count || startIndex+simplified.Count>MaximumPoints)return false;
        pathSuggestion=simplified;pathOriginal=anchors.ToArray();pathStartIndex=startIndex;pathOriginalTotalCount=points.Count;pathClosingTarget=includeClosing ? anchors[0] : null;InvalidateVisual();return true;
    }
    internal bool SetReplacementPath(int first,int last,IReadOnlyList<Point> candidate)
    {
        pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;edgeSuggestion=null;InvalidateVisual();
        if(image is null || candidate is null || first<ReferencePointCount || first>=points.Count || last<=first || last>=points.Count ||
            candidate.Count<2 || candidate.Count>4096 || points[first]==points[last])return false;
        Point Rounded(Point p)=>new(Math.Round(p.X,MidpointRounding.AwayFromZero),Math.Round(p.Y,MidpointRounding.AwayFromZero));
        if(candidate[0]!=Rounded(points[first]) || candidate[^1]!=Rounded(points[last]))return false;
        var replacement=new List<Point> { points[first] };
        for(int i=1;i<candidate.Count-1;i++)
        {
            var p=candidate[i];var a=candidate[i-1];var b=candidate[i+1];
            if(!double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X<0 || p.Y<0 || p.X>image.PixelWidth-1 || p.Y>image.PixelHeight-1)return false;
            var nextToSubpixelAnchor=(i==1 && points[first]!=candidate[0]) || (i==candidate.Count-2 && points[last]!=candidate[^1]);
            if(!nextToSubpixelAnchor && (p.X-a.X)*(b.Y-p.Y)==(p.Y-a.Y)*(b.X-p.X) && (p.X-a.X)*(b.X-p.X)+(p.Y-a.Y)*(b.Y-p.Y)>0)continue;
            if(p==replacement[^1] || p==points[last])return false;replacement.Add(p);
        }
        if(replacement[^1]==points[last])return false;replacement.Add(points[last]);
        var original=points.Skip(first).Take(last-first+1).ToArray();
        if(points.Count-original.Length+replacement.Count>MaximumPoints || replacement.SequenceEqual(original))return false;
        pathOriginal=original;pathOriginalTotalCount=points.Count;pathStartIndex=first;pathSuggestion=replacement;InvalidateVisual();return true;
    }
    internal bool ApplyPathSuggestion()
    {
        if(pathSuggestion is not { Count:>=2 } path || pathOriginal is null || pathStartIndex<ReferencePointCount ||
           points.Count!=pathOriginalTotalCount || pathStartIndex+pathOriginal.Length>points.Count || !points.Skip(pathStartIndex).Take(pathOriginal.Length).SequenceEqual(pathOriginal))return false;
        pathUndo=points.ToList();points.RemoveRange(pathStartIndex,pathOriginal.Length);points.InsertRange(pathStartIndex,path);
        cursor=path[^1];pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;edgeSuggestion=null;InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);return true;
    }
    internal Point[]? ProposedTraceSnapshot()
    {
        if(pathSuggestion is not { Count:>=2 } path || pathOriginal is null || pathStartIndex<ReferencePointCount ||
            points.Count!=pathOriginalTotalCount || pathStartIndex+pathOriginal.Length>points.Count || !points.Skip(pathStartIndex).Take(pathOriginal.Length).SequenceEqual(pathOriginal))return null;
        var snapshot=points.ToList();snapshot.RemoveRange(pathStartIndex,pathOriginal.Length);snapshot.InsertRange(pathStartIndex,path);
        return snapshot.Skip(ReferencePointCount).ToArray();
    }
    internal Point[]? TopologyWarningEdgesForVerification=>topologyWarningEdges?.ToArray();
    internal bool HasTopologyWarning=>topologyWarning;
    internal bool HasTopologyWarningEdges=>topologyWarningEdges is not null;
    internal bool SetTopologyWarning(Point[] expected,Point[]? edges,bool invalid)
    {
        var current=ProposedTraceSnapshot();if(current is null || !current.SequenceEqual(expected) || (edges is not null && (edges.Length!=4 || !invalid)))return false;
        topologyWarning=invalid;topologyWarningEdges=edges?.ToArray();InvalidateVisual();return true;
    }
    internal bool SetCurrentTopologyWarning(Point[] expected,Point[]? edges,bool invalid)
    {
        if(pathSuggestion is not null || !points.Skip(ReferencePointCount).SequenceEqual(expected) || (edges is not null && (edges.Length!=4 || !invalid)))return false;
        topologyWarning=invalid;topologyWarningEdges=edges?.ToArray();InvalidateVisual();return true;
    }
    internal bool UndoPath()
    {
        if(pathUndo is null)return false;
        points.Clear();points.AddRange(pathUndo);pathUndo=null;pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;edgeSuggestion=null;cursor=points[^1];
        InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);return true;
    }

    internal bool TryAddPixelPoint(Point point)
    {
        if(image is null || points.Count>=MaximumPoints || !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            point.X<0 || point.Y<0 || point.X>image.PixelWidth-1 || point.Y>image.PixelHeight-1 ||
            (points.Count>0 && points.Count!=ReferencePointCount && points[^1]==point))return false;
        points.Add(point);edgeSuggestion=null;pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;pathUndo=null;cursor=point;InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);return true;
    }

    internal bool TryMoveTracePoint(int index,Point point)
    {
        if(image is null || index<ReferencePointCount || index>=points.Count || !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            point.X<0 || point.Y<0 || point.X>image.PixelWidth-1 || point.Y>image.PixelHeight-1 || point==points[index] ||
            (index>ReferencePointCount && point==points[index-1]) || (index+1<points.Count && point==points[index+1]))return false;
        pathUndo=points.ToList();points[index]=point;cursor=point;edgeSuggestion=null;pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;
        InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);return true;
    }

    internal bool TryInsertTracePointAfter(int index,Point point)
    {
        if(image is null || points.Count>=MaximumPoints || index<ReferencePointCount || index>=points.Count ||
            !double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X<0 || point.Y<0 || point.X>image.PixelWidth-1 || point.Y>image.PixelHeight-1 ||
            point==points[index] || (index+1<points.Count && point==points[index+1]))return false;
        pathUndo=points.ToList();points.Insert(index+1,point);cursor=point;edgeSuggestion=null;pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;
        InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);return true;
    }
    internal bool TryRemoveTracePoint(int index)
    {
        if(index<ReferencePointCount || index>=points.Count || points.Count-ReferencePointCount<=2 ||
            (index>ReferencePointCount && index+1<points.Count && points[index-1]==points[index+1]))return false;
        pathUndo=points.ToList();points.RemoveAt(index);cursor=points[Math.Min(index,points.Count-1)];edgeSuggestion=null;pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;
        InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);return true;
    }
    internal bool TryFlattenTraceRange(int first,int last)
    {
        if(first<ReferencePointCount || first>=points.Count || last<0 || last>=points.Count || last<=first+1 || points[first]==points[last])return false;
        pathUndo=points.ToList();points.RemoveRange(first+1,last-first-1);cursor=points[first+1];
        edgeSuggestion=null;pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);return true;
    }

    internal bool UndoPoint()
    {
        if(points.Count==0)return false;
        points.RemoveAt(points.Count-1);edgeSuggestion=null;pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;pathUndo=null;InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);return true;
    }
    internal void ClearPoints()
    {
        if(points.Count==0)return;
        points.Clear();edgeSuggestion=null;pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;pathUndo=null;InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);
    }
    internal void ClearTraceKeepReferences()
    {
        if(points.Count<=ReferencePointCount)return;
        points.RemoveRange(ReferencePointCount,points.Count-ReferencePointCount);cursor=points[^1];
        edgeSuggestion=null;pathSuggestion=null;pathOriginal=null;pathClosingTarget=null;topologyWarningEdges=null;topologyWarning=false;pathUndo=null;
        InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);
    }

    // Rectangle is the actual uniform-fit image, excluding letterbox regions.
    internal Rect ImageBounds(Size viewport)
    {
        if(image is null || !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height) || viewport.Width<=0 || viewport.Height<=0)return Rect.Empty;
        var scale=Math.Min(viewport.Width/sourceWidth,viewport.Height/sourceHeight);
        if(!double.IsFinite(scale) || scale<=0)return Rect.Empty;
        var width=sourceWidth*scale*zoom;var height=sourceHeight*scale*zoom;
        return new Rect(viewport.Width/2-zoomCenter.X*width,viewport.Height/2-zoomCenter.Y*height,width,height);
    }
    internal bool TryMapViewToPixel(Point viewPoint,Size viewport,out Point pixel)
    {
        pixel=default;
        if(image is null || !double.IsFinite(viewPoint.X) || !double.IsFinite(viewPoint.Y) || viewPoint.X<0 || viewPoint.Y<0 || viewPoint.X>=viewport.Width || viewPoint.Y>=viewport.Height)return false;
        var bounds=ImageBounds(viewport);
        if(bounds.IsEmpty || viewPoint.X<bounds.Left || viewPoint.Y<bounds.Top || viewPoint.X>=bounds.Right || viewPoint.Y>=bounds.Bottom)return false;
        var scaleX=bounds.Width/image.PixelWidth;var scaleY=bounds.Height/image.PixelHeight;
        pixel=new Point(Math.Clamp((viewPoint.X-bounds.Left)/scaleX-0.5,0,image.PixelWidth-1),
                        Math.Clamp((viewPoint.Y-bounds.Top)/scaleY-0.5,0,image.PixelHeight-1));
        return true;
    }
    internal bool TryMapPixelToView(Point pixel,Size viewport,out Point viewPoint)
    {
        viewPoint=default;
        if(image is null || !double.IsFinite(pixel.X) || !double.IsFinite(pixel.Y) || pixel.X<0 || pixel.Y<0 || pixel.X>image.PixelWidth-1 || pixel.Y>image.PixelHeight-1)return false;
        var bounds=ImageBounds(viewport);if(bounds.IsEmpty)return false;
        var scaleX=bounds.Width/image.PixelWidth;var scaleY=bounds.Height/image.PixelHeight;
        viewPoint=new Point(bounds.Left+(pixel.X+0.5)*scaleX,bounds.Top+(pixel.Y+0.5)*scaleY);return true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);Focus();
        if(TryMapViewToPixel(e.GetPosition(this),RenderSize,out var pixel))
        { cursor=pixel;TryAddPixelPoint(pixel);InvalidateVisual();e.Handled=true; }
    }
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if(TryInspectAtView(e.GetPosition(this),RenderSize)) { Focus();e.Handled=true; }
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e);InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e);InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);if(image is null)return;
        var step=(Keyboard.Modifiers&ModifierKeys.Shift)!=0 ? 10d : 1d;
        switch(e.Key)
        {
            case Key.Left: cursor.X=Math.Max(0,cursor.X-step);break;
            case Key.Right: cursor.X=Math.Min(image.PixelWidth-1,cursor.X+step);break;
            case Key.Up: cursor.Y=Math.Max(0,cursor.Y-step);break;
            case Key.Down: cursor.Y=Math.Min(image.PixelHeight-1,cursor.Y+step);break;
            case Key.Enter: case Key.Space: TryAddPixelPoint(cursor);break;
            case Key.Back: UndoPoint();break;
            default:return;
        }
        if(zoom>1 && TryMapPixelToView(cursor,RenderSize,out var view) && (view.X<16 || view.Y<16 || view.X>RenderSize.Width-16 || view.Y>RenderSize.Height-16))
            zoomCenter=new Point((cursor.X+0.5)/image.PixelWidth,(cursor.Y+0.5)/image.PixelHeight);
        InvalidateVisual();e.Handled=true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White,new Pen(DesignRequestWindow.BorderColor,1),new Rect(RenderSize));
        var bounds=ImageBounds(RenderSize);if(image is null || bounds.IsEmpty)return;
        dc.DrawImage(image,bounds);
        if(photoOnly)return;
        if(pathSuggestion is { Count:>=2 } suggestedPath)
        {
            var proposalPen=new Pen(Brushes.DeepSkyBlue,3) { DashStyle=DashStyles.Dash };
            for(int i=1;i<suggestedPath.Count;i++)
                if(TryMapPixelToView(suggestedPath[i-1],RenderSize,out var a) && TryMapPixelToView(suggestedPath[i],RenderSize,out var b))dc.DrawLine(proposalPen,a,b);
            if(pathClosingTarget is Point closing && TryMapPixelToView(suggestedPath[^1],RenderSize,out var last) && TryMapPixelToView(closing,RenderSize,out var first))dc.DrawLine(proposalPen,last,first);
        }
        var stroke=new Pen(DesignRequestWindow.Accent,2);Point? previous=null;
        var dip=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for(var i=0;i<points.Count;i++)
        {
            if(!TryMapPixelToView(points[i],RenderSize,out var point))continue;
            if(previous is { } last && i!=ReferencePointCount)dc.DrawLine(stroke,last,point);
            // Keep the full polyline visible when hundreds of numbered markers overlap it.
            // Reference points and the final trace point retain their numbered markers.
            var prominent=!DenseTraceView || i<ReferencePointCount || i==points.Count-1;
            if(prominent)
            {
                dc.DrawEllipse(DesignRequestWindow.Accent,new Pen(Brushes.White,1),point,7,7);
                var label=new FormattedText((i+1).ToString(CultureInfo.InvariantCulture),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,Brushes.White,dip);
                dc.DrawText(label,new Point(point.X-label.Width/2,point.Y-label.Height/2));
            }
            else dc.DrawEllipse(DesignRequestWindow.Accent,null,point,2,2);
            previous=point;
        }
        if(ReferencePointCount==4 && points.Count>=4 && TryMapPixelToView(points[0],RenderSize,out var firstCorner) && TryMapPixelToView(points[3],RenderSize,out var lastCorner))
            dc.DrawLine(new Pen(Brushes.DarkOrange,2) { DashStyle=DashStyles.Dash },lastCorner,firstCorner);
        if(edgeSuggestion is Point suggested && TryMapPixelToView(suggested,RenderSize,out var suggestedView))
            dc.DrawEllipse(null,new Pen(Brushes.DeepSkyBlue,3),suggestedView,11,11);
        if(topologyWarningEdges is { Length:4 } warning)
        {
            var warningPen=new Pen(Brushes.DarkOrange,5);
            for(int i=0;i<4;i+=2)
                if(TryMapPixelToView(warning[i],RenderSize,out var a) && TryMapPixelToView(warning[i+1],RenderSize,out var b))dc.DrawLine(warningPen,a,b);
        }
        if(IsKeyboardFocused && TryMapPixelToView(cursor,RenderSize,out var current))
        {
            if(NearestTracePointIndex() is int selected && TryMapPixelToView(points[selected],RenderSize,out var nearest))
            {
                dc.DrawEllipse(null,new Pen(Brushes.Gold,3),nearest,10,10);
                var number=new FormattedText((selected+1).ToString(CultureInfo.InvariantCulture),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.Black,dip);
                var labelOrigin=new Point(nearest.X+12,nearest.Y-12);
                dc.DrawRectangle(Brushes.Gold,null,new Rect(labelOrigin,new Size(number.Width+4,number.Height)));
                dc.DrawText(number,new Point(labelOrigin.X+2,labelOrigin.Y));
            }
            var highlight=new Pen(Brushes.White,2);dc.DrawLine(highlight,new Point(current.X-8,current.Y),new Point(current.X+8,current.Y));
            dc.DrawLine(highlight,new Point(current.X,current.Y-8),new Point(current.X,current.Y+8));
        }
    }
}
