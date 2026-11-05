using System.Runtime.InteropServices;
using System.IO;
using OpenCvSharp;

namespace PhotoRegionSharpPrototype;

/// <summary>Unmodified pixel-centre contours for review, not CAD geometry.</summary>
internal sealed class MaskContours
{
    internal sealed class Loop
    {
        private readonly Point[] points;
        internal Loop(Point[] points,int parent) { this.points=points;Parent=parent; }
        internal int Parent { get; }
        internal bool IsHole => Parent>=0;
        internal int Count => points.Length;
        internal Point[] CopyPoints() => (Point[])points.Clone();
    }
    private readonly Loop[] loops;
    private MaskContours(Loop[] loops) => this.loops=loops;
    internal IReadOnlyList<Loop> Loops => Array.AsReadOnly(loops);

    internal static MaskContours Extract(byte[] binary,int width,int height,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(binary);
        if(width<1 || height<1 || width>RegionProposal.MaxDimension || height>RegionProposal.MaxDimension ||
           (long)width*height>RegionProposal.MaxPixels || binary.Length!=(long)width*height)
            throw new ArgumentException("Maske boyutu geçersiz.");
        token.ThrowIfCancellationRequested();
        var copy=(byte[])binary.Clone();
        for(var i=0;i<copy.Length;i++)
        {
            if((i & 4095)==0)token.ThrowIfCancellationRequested();
            if(copy[i]>1)throw new ArgumentException("Maske yalnızca 0 ve 1 içermelidir.");
        }
        using var mask=new Mat(height,width,MatType.CV_8UC1);Marshal.Copy(copy,0,mask.Data,copy.Length);
        token.ThrowIfCancellationRequested();
        Cv2.FindContours(mask,out Point[][] contours,out HierarchyIndex[] hierarchy,RetrievalModes.CComp,ContourApproximationModes.ApproxNone);
        token.ThrowIfCancellationRequested();
        if(contours.Length!=hierarchy.Length)throw new InvalidDataException("Kontur hiyerarşisi geçersiz.");
        var loops=new Loop[contours.Length];
        for(var i=0;i<loops.Length;i++)
        {
            token.ThrowIfCancellationRequested();
            var parent=hierarchy[i].Parent;
            if(parent < -1 || parent>=loops.Length || parent==i || (parent>=0 && hierarchy[parent].Parent!=-1))
                throw new InvalidDataException("Kontur üst bilgisi geçersiz.");
            loops[i]=new Loop(contours[i],parent);
        }
        return new MaskContours(loops);
    }
}
