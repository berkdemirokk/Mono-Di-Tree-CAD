using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
if(args.Length!=3 || (args[2]!="keep" && args[2]!="exit" && args[2]!="cancel"))throw new ArgumentException("Synthetic test: node path and script path required");
var node=Path.GetFullPath(args[0]);var script=Path.GetFullPath(args[1]);
if(node.Contains('"')||script.Contains('"'))throw new ArgumentException("Invalid path");
using var job=Native.CreateJobObject(IntPtr.Zero,null);if(job.IsInvalid)throw new Win32Exception();
var limits=new Native.EXTENDED_LIMIT();limits.Basic.LimitFlags=0x2000;
var size=Marshal.SizeOf<Native.EXTENDED_LIMIT>();var data=Marshal.AllocHGlobal(size);
try{Marshal.StructureToPtr(limits,data,false);if(!Native.SetInformationJobObject(job,9,data,(uint)size))throw new Win32Exception();}finally{Marshal.FreeHGlobal(data);}
using var stdout=new AnonymousPipeServerStream(PipeDirection.In,HandleInheritability.Inheritable);
using var stdin=new AnonymousPipeServerStream(PipeDirection.Out,HandleInheritability.Inheritable);
var si=new Native.STARTUPINFO { cb=(uint)Marshal.SizeOf<Native.STARTUPINFO>(),dwFlags=0x100,hStdInput=stdin.ClientSafePipeHandle.DangerousGetHandle(),hStdOutput=stdout.ClientSafePipeHandle.DangerousGetHandle(),hStdError=stdout.ClientSafePipeHandle.DangerousGetHandle() };
var command=new StringBuilder($"\"{node}\" \"{script}\" {args[2]}");
if(!Native.CreateProcess(node,command,IntPtr.Zero,IntPtr.Zero,true,0x00000004|0x08000000,IntPtr.Zero,null,ref si,out var pi))throw new Win32Exception();
using var proc=new SafeFileHandle(pi.hProcess,true);using var thread=new SafeFileHandle(pi.hThread,true);
stdout.DisposeLocalCopyOfClientHandle();stdin.DisposeLocalCopyOfClientHandle();
if(!Native.AssignProcessToJobObject(job,proc)) {var error=Marshal.GetLastWin32Error();Native.TerminateProcess(proc,1);Native.WaitForSingleObject(proc,3000);throw new Win32Exception(error,"Job assignment rejected; process was not resumed.");}
if(Native.ResumeThread(thread)==uint.MaxValue){Native.TerminateJobObject(job,1);throw new Win32Exception("Could not resume.");}
using var reader=new StreamReader(stdout,new UTF8Encoding(false,true));
string? line;using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10)))line=await reader.ReadLineAsync(timeout.Token);
if(line is null||!line.StartsWith("CHILD=")||!int.TryParse(line.AsSpan(6),out var childId))throw new InvalidOperationException("No child identity.");
using var child=Process.GetProcessById(childId);
using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5))) { var ready=await reader.ReadLineAsync(timeout.Token);if(ready!="CHILD_READY")throw new InvalidOperationException("Child did not become ready: "+ready); }
if(args[2]=="exit" && Native.WaitForSingleObject(proc,5000)!=0)throw new TimeoutException("Root did not exit.");
if(child.HasExited)throw new InvalidOperationException("Child already exited; test inconclusive.");
if(args[2]=="cancel") { using var cancel=new CancellationTokenSource(200);try{await Task.Delay(Timeout.Infinite,cancel.Token);}catch(OperationCanceledException){} }
job.Dispose();
await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
if(Native.WaitForSingleObject(proc,5000)!=0)throw new TimeoutException("Root remained after job close.");
Console.WriteLine($"PASS: suspended launch assigned before resume; mode={args[2]}; closing job terminated synthetic child and root.");

internal static class Native
{
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]internal struct STARTUPINFO {public uint cb;public string? lpReserved,lpDesktop,lpTitle;public uint dwX,dwY,dwXSize,dwYSize,dwXCountChars,dwYCountChars,dwFillAttribute,dwFlags;public ushort wShowWindow,cbReserved2;public IntPtr lpReserved2,hStdInput,hStdOutput,hStdError;}
 [StructLayout(LayoutKind.Sequential)]internal struct PROCESS_INFORMATION {public IntPtr hProcess,hThread;public uint dwProcessId,dwThreadId;}
 [StructLayout(LayoutKind.Sequential)]internal struct BASIC_LIMIT {public long PerProcessUserTimeLimit,PerJobUserTimeLimit;public uint LimitFlags;public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;}
 [StructLayout(LayoutKind.Sequential)]internal struct IO_COUNTERS {public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount;}
 [StructLayout(LayoutKind.Sequential)]internal struct EXTENDED_LIMIT {public BASIC_LIMIT Basic;public IO_COUNTERS Io;public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern SafeFileHandle CreateJobObject(IntPtr attrs,string? name);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool SetInformationJobObject(SafeFileHandle job,int info,IntPtr data,uint size);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool CreateProcess(string app,StringBuilder cmd,IntPtr processAttrs,IntPtr threadAttrs,[MarshalAs(UnmanagedType.Bool)]bool inherit,uint flags,IntPtr env,string? cwd,ref STARTUPINFO startup,out PROCESS_INFORMATION info);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool AssignProcessToJobObject(SafeFileHandle job,SafeFileHandle process);
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern uint ResumeThread(SafeFileHandle thread);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool TerminateProcess(SafeFileHandle process,uint code);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool TerminateJobObject(SafeFileHandle job,uint code);
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern uint WaitForSingleObject(SafeFileHandle handle,uint milliseconds);
}
