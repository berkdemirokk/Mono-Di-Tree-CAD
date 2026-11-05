namespace PhotoRegionSharpPrototype;

/// <summary>Experimental, lossless 8-connected foreground selection; no geometry is generated.</summary>
internal sealed class MaskComponents
{
    internal readonly record struct Component(int Id,int Pixels,int Left,int Top,int Right,int Bottom);
    private readonly int[] labels;
    private readonly Component[] components;
    private MaskComponents(int width,int height,int[] labels,Component[] components)
    { Width=width;Height=height;this.labels=labels;this.components=components; }
    internal int Width { get; }
    internal int Height { get; }
    internal IReadOnlyList<Component> Components => Array.AsReadOnly(components);

    internal static MaskComponents Analyze(byte[] mask,int width,int height,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if(width<1 || height<1 || width>RegionProposal.MaxDimension || height>RegionProposal.MaxDimension ||
           (long)width*height>RegionProposal.MaxPixels || mask.Length!=(long)width*height)
            throw new ArgumentException("Maske boyutu geçersiz.");
        token.ThrowIfCancellationRequested();
        var data=(byte[])mask.Clone();
        var labels=new int[data.Length];var queue=new int[data.Length];var results=new List<Component>();
        for(var start=0;start<data.Length;start++)
        {
            if((start & 4095)==0)token.ThrowIfCancellationRequested();
            if(data[start]>1)throw new ArgumentException("Maske yalnızca 0 ve 1 içermelidir.");
            if(data[start]==0 || labels[start]!=0)continue;
            var id=results.Count+1;var head=0;var tail=0;queue[tail++]=start;labels[start]=id;
            var left=width;var top=height;var right=0;var bottom=0;
            while(head<tail)
            {
                if((head & 4095)==0)token.ThrowIfCancellationRequested();
                var index=queue[head++];var x=index%width;var y=index/width;
                left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
                for(var ny=Math.Max(0,y-1);ny<=Math.Min(height-1,y+1);ny++)
                    for(var nx=Math.Max(0,x-1);nx<=Math.Min(width-1,x+1);nx++)
                    {
                        var next=ny*width+nx;
                        if(data[next]==1 && labels[next]==0) { labels[next]=id;queue[tail++]=next; }
                    }
            }
            results.Add(new Component(id,tail,left,top,right,bottom));
        }
        token.ThrowIfCancellationRequested();
        return new MaskComponents(width,height,labels,results.ToArray());
    }

    internal int ComponentAt(int x,int y)
    {
        if(x<0 || x>=Width || y<0 || y>=Height)throw new ArgumentOutOfRangeException();
        return labels[y*Width+x];
    }

    // Explicit selection preserves every selected pixel and every background hole.
    internal byte[] CopySelection(IReadOnlyCollection<int> ids,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(ids);token.ThrowIfCancellationRequested();
        var selected=new bool[components.Length+1];
        foreach(var id in ids)
        {
            if(id<1 || id>=selected.Length)throw new ArgumentOutOfRangeException(nameof(ids));
            selected[id]=true;
        }
        var result=new byte[labels.Length];
        for(var i=0;i<labels.Length;i++)
        {
            if((i & 4095)==0)token.ThrowIfCancellationRequested();
            if(selected[labels[i]])result[i]=1;
        }
        token.ThrowIfCancellationRequested();return result;
    }
}
