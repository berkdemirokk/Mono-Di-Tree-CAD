using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TekstilCAD.Desktop;

/// <summary>
/// Experimental RGB-contrast shortest-path proposal between two user-supplied pixels.
/// A contrast path is not a garment boundary or a validated pattern.
/// </summary>
internal static class PhotoColorEdgePath
{
    private const int Margin=16;
    private const int MaximumSearchPixels=160_000;
    private const int MaximumPathPoints=4096;
    private static readonly (int X,int Y)[] Steps=[(-1,-1),(0,-1),(1,-1),(-1,0),(1,0),(-1,1),(0,1),(1,1)];

    internal static bool TryFind(BitmapSource image,Point start,Point end,out IReadOnlyList<Point> path,CancellationToken token=default)
    {
        path=Array.Empty<Point>();token.ThrowIfCancellationRequested();
        if(image.PixelWidth<3 || image.PixelHeight<3 || !Valid(start,image) || !Valid(end,image))return false;
        int sx=(int)Math.Round(start.X,MidpointRounding.AwayFromZero),sy=(int)Math.Round(start.Y,MidpointRounding.AwayFromZero);
        int ex=(int)Math.Round(end.X,MidpointRounding.AwayFromZero),ey=(int)Math.Round(end.Y,MidpointRounding.AwayFromZero);
        if(sx==ex && sy==ey)return false;
        int left=Math.Max(0,Math.Min(sx,ex)-Margin),top=Math.Max(0,Math.Min(sy,ey)-Margin);
        int right=Math.Min(image.PixelWidth-1,Math.Max(sx,ex)+Margin),bottom=Math.Min(image.PixelHeight-1,Math.Max(sy,ey)+Margin);
        int width=right-left+1,height=bottom-top+1;
        if((long)width*height>MaximumSearchPixels)return false;
        int count=width*height;
        var bgr=PhotoRasterCapture.CaptureBgr(image,new Int32Rect(left,top,width,height),token);
        var blue=new byte[count];var green=new byte[count];var red=new byte[count];
        for(int i=0;i<count;i++)
        {
            if((i&4095)==0)token.ThrowIfCancellationRequested();
            int p=i*3;blue[i]=bgr[p];green[i]=bgr[p+1];red[i]=bgr[p+2];
        }
        var cost=new double[count];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int i=y*width+x;if((i&4095)==0)token.ThrowIfCancellationRequested();
            int leftIndex=y*width+Math.Max(x-1,0),rightIndex=y*width+Math.Min(x+1,width-1),upIndex=Math.Max(y-1,0)*width+x,downIndex=Math.Min(y+1,height-1)*width+x;
            static int Power(byte[] channel,int left,int right,int up,int down) { int dx=channel[right]-channel[left],dy=channel[down]-channel[up];return dx*dx+dy*dy; }
            var strength=Math.Sqrt((29d*Power(blue,leftIndex,rightIndex,upIndex,downIndex)+150d*Power(green,leftIndex,rightIndex,upIndex,downIndex)+77d*Power(red,leftIndex,rightIndex,upIndex,downIndex))/256d);
            cost[i]=0.12+64/(64+strength);
        }
        int from=(sy-top)*width+sx-left,to=(ey-top)*width+ex-left;
        var distance=new double[count];Array.Fill(distance,double.PositiveInfinity);
        var previous=new int[count];Array.Fill(previous,-1);var settled=new bool[count];
        var queue=new PriorityQueue<int,(double,int)>();distance[from]=0;queue.Enqueue(from,(0,from));
        while(queue.TryDequeue(out var i,out _))
        {
            token.ThrowIfCancellationRequested();if(settled[i])continue;settled[i]=true;if(i==to)break;
            int x=i%width,y=i/width;
            foreach(var (dx,dy) in Steps)
            {
                int nx=x+dx,ny=y+dy;if(nx<0 || ny<0 || nx>=width || ny>=height)continue;
                int j=ny*width+nx;if(settled[j])continue;
                var candidate=distance[i]+(dx!=0 && dy!=0 ? Math.Sqrt(2) : 1)*(cost[i]+cost[j])/2;
                if(candidate>=distance[j])continue;distance[j]=candidate;previous[j]=i;queue.Enqueue(j,(candidate,j));
            }
        }
        if(!settled[to])return false;
        var points=new List<Point>();
        for(int i=to;i!=-1;i=previous[i])
        {
            token.ThrowIfCancellationRequested();if(points.Count>=MaximumPathPoints)return false;
            points.Add(new Point(left+i%width,top+i/width));if(i==from)break;
        }
        if(points.Count<2 || points[^1]!=new Point(sx,sy))return false;
        points.Reverse();path=points.AsReadOnly();return true;
    }

    private static bool Valid(Point p,BitmapSource image)=>double.IsFinite(p.X) && double.IsFinite(p.Y) && p.X>=0 && p.Y>=0 && p.X<=image.PixelWidth-1 && p.Y<=image.PixelHeight-1;
}
