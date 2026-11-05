using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace SyntheticBridge;

// Establish managed ownership immediately after each successful native allocation.
internal sealed class NativeAllocation : SafeHandleZeroOrMinusOneIsInvalid
{
    private static int active;
    internal static int ActiveCount => Volatile.Read(ref active);
    private NativeAllocation():base(true){}
    internal static NativeAllocation Allocate(IntPtr size){var owner=new NativeAllocation();owner.SetHandle(Marshal.AllocHGlobal(size));Interlocked.Increment(ref active);return owner;}
    internal static NativeAllocation Unicode(string value){var owner=new NativeAllocation();owner.SetHandle(Marshal.StringToHGlobalUni(value));Interlocked.Increment(ref active);return owner;}
    protected override bool ReleaseHandle(){Marshal.FreeHGlobal(handle);Interlocked.Decrement(ref active);return true;}
}
