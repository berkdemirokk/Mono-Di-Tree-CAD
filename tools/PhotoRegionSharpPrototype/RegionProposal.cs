using System.Runtime.InteropServices;
using System.IO;
using System.Security.Cryptography;
using OpenCvSharp;

namespace PhotoRegionSharpPrototype;

/// <summary>Experimental local region proposal. No CAD geometry or production approval is produced.</summary>
internal static class RegionProposal
{
    internal const int MaxPixels=1_048_576,MaxDimension=2048,Iterations=5;
    internal readonly record struct Mark(int X,int Y,bool Foreground);
    internal sealed class Snapshot
    {
        private readonly byte[] labels;
        internal Snapshot(int width,int height,string imageHash,byte[] source)
        { Width=width;Height=height;ImageHash=imageHash;labels=(byte[])source.Clone(); }
        internal int Width { get; }
        internal int Height { get; }
        internal string ImageHash { get; }
        internal byte[] CopyLabels() => (byte[])labels.Clone();
        internal byte[] CopyBinaryMask() => labels.Select(x=>(byte)((x==1 || x==3) ? 1 : 0)).ToArray();
    }

    internal static Snapshot Start(byte[] bgr,int width,int height,Rect rectangle,CancellationToken token=default,Action<int>? iterationFinished=null,ulong seed=12345678)
        => Run(bgr,width,height,rectangle,null,[],token,iterationFinished,seed);
    internal static Snapshot Refine(byte[] bgr,int width,int height,Snapshot previous,IReadOnlyList<Mark> marks,CancellationToken token=default,Action<int>? iterationFinished=null,ulong seed=12345678)
    {
        ArgumentNullException.ThrowIfNull(previous);ArgumentNullException.ThrowIfNull(marks);
        return Run(bgr,width,height,default,previous,marks,token,iterationFinished,seed);
    }
    private static Snapshot Run(byte[] bgr,int width,int height,Rect rectangle,Snapshot? previous,IReadOnlyList<Mark> marks,CancellationToken token,Action<int>? iterationFinished,ulong seed)
    {
        token.ThrowIfCancellationRequested();ArgumentNullException.ThrowIfNull(bgr);
        if(width<3 || height<3 || width>MaxDimension || height>MaxDimension || (long)width*height>MaxPixels || bgr.Length!=(long)width*height*3)
            throw new ArgumentException("Görsel boyutu veya BGR veri uzunluğu geçersiz.");
        if(marks.Count>MaxPixels)throw new ArgumentException("Düzeltme noktası sınırı aşıldı.");
        var pixels=(byte[])bgr.Clone();var hash=Convert.ToHexString(SHA256.HashData(pixels));
        var count=checked(width*height);byte[] labels;
        if(previous is null)
        {
            if(rectangle.X<0 || rectangle.Y<0 || rectangle.Width<2 || rectangle.Height<2 ||
               (long)rectangle.X+rectangle.Width>width || (long)rectangle.Y+rectangle.Height>height ||
               (long)rectangle.Width*rectangle.Height<5 || (long)count-(long)rectangle.Width*rectangle.Height<5)
                throw new ArgumentException("Dikdörtgen geçersiz veya yeterli ön/arka plan alanı yok.");
            labels=new byte[count];
        }
        else
        {
            if(previous.Width!=width || previous.Height!=height || previous.ImageHash!=hash)
                throw new InvalidOperationException("Düzeltme farklı bir görsel veya eski maske için yapılamaz.");
            labels=previous.CopyLabels();
            var seen=new Dictionary<int,bool>();
            foreach(var mark in marks)
            {
                token.ThrowIfCancellationRequested();
                if(mark.X<0 || mark.Y<0 || mark.X>=width || mark.Y>=height)throw new ArgumentException("Düzeltme görüntü dışında.");
                var index=mark.Y*width+mark.X;
                if(seen.TryGetValue(index,out var existing) && existing!=mark.Foreground)throw new ArgumentException("Aynı piksel için çelişkili düzeltmeler var.");
                seen[index]=mark.Foreground;
                labels[index]=(byte)(mark.Foreground ? 1 : 0);
            }
        }
        token.ThrowIfCancellationRequested();
        using var image=new Mat(height,width,MatType.CV_8UC3);Marshal.Copy(pixels,0,image.Data,pixels.Length);
        using var mask=new Mat(height,width,MatType.CV_8UC1);Marshal.Copy(labels,0,mask.Data,labels.Length);
        using var background=new Mat();using var foreground=new Mat();
        // OpenCV RNG is thread-local. Restore it even after cancellation/failure.
        var oldRng=Cv2.GetTheRNG().State;
        try
        {
            Cv2.SetTheRNG(seed);
            for(int iteration=0;iteration<Iterations;iteration++)
            {
                token.ThrowIfCancellationRequested();
                var mode=iteration==0 ? (previous is null ? GrabCutModes.InitWithRect : GrabCutModes.InitWithMask) : GrabCutModes.Eval;
                Cv2.GrabCut(image,mask,rectangle,background,foreground,1,mode);
                iterationFinished?.Invoke(iteration+1);
                token.ThrowIfCancellationRequested();
            }
        }
        finally { Cv2.SetTheRNG(oldRng); }
        Marshal.Copy(mask.Data,labels,0,count);token.ThrowIfCancellationRequested();
        if(labels.Any(x=>x>3))throw new InvalidDataException("Bölge maskesi geçersiz.");
        return new Snapshot(width,height,hash,labels);
    }
}
