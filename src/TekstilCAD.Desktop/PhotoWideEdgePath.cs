using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TekstilCAD.Desktop;

/// <summary>Optional bounded 32-pixel margin. Falls back to the existing 16-pixel search when the wider rectangle exceeds the work budget.
/// A path remains an unapproved image proposal; a wider area may follow a different edge.</summary>
internal static class PhotoWideEdgePath
{
    private const int Margin=32,MaximumSearchPixels=160_000,MaximumPathPoints=4096;
    private static readonly (int X,int Y)[] Steps=[(-1,-1),(0,-1),(1,-1),(-1,0),(1,0),(-1,1),(0,1),(1,1)];
    internal static bool TryFind(BitmapSource image,Point start,Point end,bool color,out IReadOnlyList<Point> path,out bool usedStandard,CancellationToken token=default)
    {
        path=Array.Empty<Point>();usedStandard=false;token.ThrowIfCancellationRequested();
        if(image.PixelWidth<3 || image.PixelHeight<3 || !Valid(start,image) || !Valid(end,image))return false;
        int sx=(int)Math.Round(start.X,MidpointRounding.AwayFromZero),sy=(int)Math.Round(start.Y,MidpointRounding.AwayFromZero);
        int ex=(int)Math.Round(end.X,MidpointRounding.AwayFromZero),ey=(int)Math.Round(end.Y,MidpointRounding.AwayFromZero);
        if(sx==ex && sy==ey)return false;
        int left=Math.Max(0,Math.Min(sx,ex)-Margin),top=Math.Max(0,Math.Min(sy,ey)-Margin);
        int right=Math.Min(image.PixelWidth-1,Math.Max(sx,ex)+Margin),bottom=Math.Min(image.PixelHeight-1,Math.Max(sy,ey)+Margin);
        int width=right-left+1,height=bottom-top+1;
        if((long)width*height>MaximumSearchPixels)
        {
            usedStandard=true;
            return color ? PhotoColorEdgePath.TryFind(image,start,end,out path,token) : PhotoEdgePath.TryFind(image,start,end,out path,token);
        }
        int count=width*height;
        var bitmap=image.Format==PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
        int stride=checked(width*4);var pixels=new byte[checked(stride*height)];bitmap.CopyPixels(new Int32Rect(left,top,width,height),pixels,stride,0);
        var gray=color ? null : new byte[count];var blue=color ? new byte[count] : null;var green=color ? new byte[count] : null;var red=color ? new byte[count] : null;
        for(int i=0;i<count;i++)
        {
            if((i&4095)==0)token.ThrowIfCancellationRequested();int p=i*4,a=pixels[p+3];
            if(color)
            {
                blue![i]=(byte)((pixels[p]*a+255*(255-a)+127)/255);green![i]=(byte)((pixels[p+1]*a+255*(255-a)+127)/255);red![i]=(byte)((pixels[p+2]*a+255*(255-a)+127)/255);
            }
            else
            {
                int v=(29*pixels[p]+150*pixels[p+1]+77*pixels[p+2]+128)>>8;gray![i]=(byte)((v*a+255*(255-a)+127)/255);
            }
        }
        var cost=new double[count];
        static int Power(byte[] channel,int l,int r,int u,int d) { int dx=channel[r]-channel[l],dy=channel[d]-channel[u];return dx*dx+dy*dy; }
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int i=y*width+x;if((i&4095)==0)token.ThrowIfCancellationRequested();
            int l=y*width+Math.Max(x-1,0),r=y*width+Math.Min(x+1,width-1),u=Math.Max(y-1,0)*width+x,d=Math.Min(y+1,height-1)*width+x;
            var strength=color ? Math.Sqrt((29d*Power(blue!,l,r,u,d)+150d*Power(green!,l,r,u,d)+77d*Power(red!,l,r,u,d))/256d) : Math.Sqrt(Power(gray!,l,r,u,d));
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
