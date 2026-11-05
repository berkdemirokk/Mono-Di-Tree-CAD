using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
internal static class N {
 [StructLayout(LayoutKind.Sequential)] internal struct ACCOUNTING { public long a,b,c,d; public uint e,f,ActiveProcesses,g; }
 [DllImport("kernel32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool QueryInformationJobObject(SafeFileHandle job, int info, out ACCOUNTING data, uint length, IntPtr returned);
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]internal struct STARTUPINFO {public uint cb;public string? lpReserved,lpDesktop,lpTitle;public uint dwX,dwY,dwXSize,dwYSize,dwXCountChars,dwYCountChars,dwFillAttribute,dwFlags;public ushort wShowWindow,cbReserved2;public IntPtr lpReserved2,hStdInput,hStdOutput,hStdError;}
 [StructLayout(LayoutKind.Sequential)]internal struct STARTUPINFOEX {public STARTUPINFO Startup;public IntPtr attributes;}
 [StructLayout(LayoutKind.Sequential)]internal struct PROCESS_INFO {public IntPtr hProcess,hThread;public uint pid,tid;}
 [StructLayout(LayoutKind.Sequential)]internal struct BASIC {public long a,b;public uint Flags;public UIntPtr c,d;public uint e;public UIntPtr f;public uint g,h;}
 [StructLayout(LayoutKind.Sequential)]internal struct IO {public ulong a,b,c,d,e,f;}
 [StructLayout(LayoutKind.Sequential)]internal struct EXTENDED {public BASIC Basic;public IO Io;public UIntPtr a,b,c,d;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern SafeFileHandle CreateJobObject(IntPtr a,string? b);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool SetInformationJobObject(SafeFileHandle a,int b,IntPtr c,uint d);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool InitializeProcThreadAttributeList(IntPtr a,int b,int c,ref IntPtr d);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool UpdateProcThreadAttribute(IntPtr a,uint b,IntPtr c,IntPtr d,IntPtr e,IntPtr f,IntPtr g);
 [DllImport("kernel32.dll")]internal static extern void DeleteProcThreadAttributeList(IntPtr a);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool CreateProcess(string a,StringBuilder b,IntPtr c,IntPtr d,[MarshalAs(UnmanagedType.Bool)]bool e,uint f,IntPtr g,string? h,ref STARTUPINFOEX i,out PROCESS_INFO j);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool IsProcessInJob(SafeFileHandle p,SafeFileHandle j,[MarshalAs(UnmanagedType.Bool)]out bool result);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool GetHandleInformation(IntPtr h,out uint flags);
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern uint ResumeThread(SafeFileHandle a);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool TerminateJobObject(SafeFileHandle a,uint b);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool TerminateProcess(SafeFileHandle p,uint code);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool GetExitCodeProcess(SafeFileHandle p,out uint code);
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern uint WaitForSingleObject(SafeFileHandle a,uint b);
}
