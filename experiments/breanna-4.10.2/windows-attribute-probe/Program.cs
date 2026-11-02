using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
if(args.Length==2&&args[0]=="child") {var h=new IntPtr(long.Parse(args[1]));Console.Write(N.GetHandleInformation(h,out _) ? "EXTRA_PRESENT" : "EXTRA_BLOCKED");return;}
if(args.Length!=2 || (args[1]!="deny" && args[1]!="allow" && args[1]!="reject"))throw new ArgumentException("Synthetic test needs dotnet path and test mode.");
string exe=Path.GetFullPath(args[0]),dll=System.Reflection.Assembly.GetExecutingAssembly().Location;
using var job=N.CreateJobObject(IntPtr.Zero,null);if(job.IsInvalid)throw new Win32Exception();
var limits=new N.EXTENDED();limits.Basic.Flags=0x2000;int n=Marshal.SizeOf<N.EXTENDED>();var limit=Marshal.AllocHGlobal(n);
try{Marshal.StructureToPtr(limits,limit,false);if(!N.SetInformationJobObject(job,9,limit,(uint)n))throw new Win32Exception();}finally{Marshal.FreeHGlobal(limit);}
using var stdout=new AnonymousPipeServerStream(PipeDirection.In,HandleInheritability.Inheritable);
using var stdin=new AnonymousPipeServerStream(PipeDirection.Out,HandleInheritability.Inheritable);
using var extra=new AnonymousPipeServerStream(PipeDirection.In,HandleInheritability.Inheritable);
var hin=stdin.ClientSafePipeHandle.DangerousGetHandle();var hout=stdout.ClientSafePipeHandle.DangerousGetHandle();var hextra=extra.ClientSafePipeHandle.DangerousGetHandle();
IntPtr size=IntPtr.Zero;N.InitializeProcThreadAttributeList(IntPtr.Zero,2,0,ref size);if(size==IntPtr.Zero)throw new Win32Exception();
var attrs=Marshal.AllocHGlobal(size);bool initialized=false;var handles=Marshal.AllocHGlobal(3*IntPtr.Size);var jobs=Marshal.AllocHGlobal(IntPtr.Size);
try {
 if(!N.InitializeProcThreadAttributeList(attrs,2,0,ref size))throw new Win32Exception();initialized=true;
 Marshal.WriteIntPtr(handles,0,hin);Marshal.WriteIntPtr(handles,IntPtr.Size,hout);Marshal.WriteIntPtr(handles,2*IntPtr.Size,hextra);Marshal.WriteIntPtr(jobs,args[1]=="reject" ? IntPtr.Zero : job.DangerousGetHandle());
 if(!N.UpdateProcThreadAttribute(attrs,0,(IntPtr)0x20002,handles,(IntPtr)((args[1]=="allow"?3:2)*IntPtr.Size),IntPtr.Zero,IntPtr.Zero))throw new Win32Exception("Handle list unavailable; refusing process launch.");
 if(!N.UpdateProcThreadAttribute(attrs,0,(IntPtr)0x2000d,jobs,(IntPtr)IntPtr.Size,IntPtr.Zero,IntPtr.Zero))throw new Win32Exception("Job list unavailable; refusing process launch.");
 var si=new N.STARTUPINFOEX {Startup=new N.STARTUPINFO { cb=(uint)Marshal.SizeOf<N.STARTUPINFOEX>(),dwFlags=0x100,hStdInput=hin,hStdOutput=hout,hStdError=hout },attributes=attrs};
 var cmd=new StringBuilder($"\"{exe}\" \"{dll}\" child {hextra.ToInt64()}");
 if(!N.CreateProcess(exe,cmd,IntPtr.Zero,IntPtr.Zero,true,0x00000004|0x00080000|0x08000000,IntPtr.Zero,null,ref si,out var pi)) {if(args[1]=="reject") {Console.WriteLine("PASS: invalid job rejected; no process launched.");return;}throw new Win32Exception("Atomic job list launch failed; no fallback.");}
 using var proc=new SafeFileHandle(pi.hProcess,true);using var thread=new SafeFileHandle(pi.hThread,true);
 try {
  if(args[1]=="reject")throw new InvalidOperationException("Invalid job unexpectedly accepted.");
  stdout.DisposeLocalCopyOfClientHandle();stdin.DisposeLocalCopyOfClientHandle();
  if(!N.IsProcessInJob(proc,job,out bool assigned)||!assigned)throw new InvalidOperationException("Suspended process is not in required job.");
  if(N.ResumeThread(thread)==uint.MaxValue)throw new Win32Exception();
  using var reader=new StreamReader(stdout,new UTF8Encoding(false,true));using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(10));var result=await reader.ReadToEndAsync(cancel.Token);
  if(N.WaitForSingleObject(proc,5000)!=0)throw new TimeoutException();
  if(!N.GetExitCodeProcess(proc,out var code)||code!=0)throw new InvalidOperationException("Sentinel child failed.");
  if(result!=(args[1]=="allow"?"EXTRA_PRESENT":"EXTRA_BLOCKED"))throw new InvalidOperationException("Sentinel handle result unexpected or reused: "+result);
  Console.WriteLine($"PASS: atomic job membership before resume; mode={args[1]}; sentinel={result}.");
 } finally { N.TerminateJobObject(job,1);if(N.WaitForSingleObject(proc,100)!=0) { N.TerminateProcess(proc,1);if(N.WaitForSingleObject(proc,3000)!=0)throw new InvalidOperationException("Synthetic process cleanup unconfirmed.");} }
} finally {if(initialized)N.DeleteProcThreadAttributeList(attrs);Marshal.FreeHGlobal(attrs);Marshal.FreeHGlobal(handles);Marshal.FreeHGlobal(jobs);}
internal static class N {
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
