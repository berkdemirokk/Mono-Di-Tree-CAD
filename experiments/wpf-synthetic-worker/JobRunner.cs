using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace SyntheticBridge;

// Derived from the synthetic windows-node-probe; job is assigned before the suspended process runs.
internal static class JobRunner
{
    static async Task<byte[]> ReadBounded(Stream stream,int limit,CancellationToken token){using var b=new MemoryStream();byte[] block=new byte[8192];while(true){int n=await stream.ReadAsync(block,token);if(n==0)return b.ToArray();if(b.Length+n>limit)throw new InvalidDataException("output limit");b.Write(block,0,n);}}
    internal static async Task<byte[]> Run(Command command,byte[] input,CancellationToken token)
    {
        if(!Path.IsPathFullyQualified(command.Exe)||command.Args.Any(x=>x.Contains('"')||x.EndsWith('\\'))||command.Exe.Contains('"')||command.Exe.EndsWith('\\'))throw new InvalidOperationException("invalid command");
        using var job=N.CreateJobObject(IntPtr.Zero,null);if(job.IsInvalid)throw new Win32Exception();
        var limits=new N.EXTENDED();limits.Basic.Flags=0x2000;int len=Marshal.SizeOf<N.EXTENDED>();IntPtr block=Marshal.AllocHGlobal(len);
        try{Marshal.StructureToPtr(limits,block,false);if(!N.SetInformationJobObject(job,9,block,(uint)len))throw new Win32Exception();}finally{Marshal.FreeHGlobal(block);}
        using var stdin=new AnonymousPipeServerStream(PipeDirection.Out,HandleInheritability.Inheritable);
        using var stdout=new AnonymousPipeServerStream(PipeDirection.In,HandleInheritability.Inheritable);
        using var stderr=new AnonymousPipeServerStream(PipeDirection.In,HandleInheritability.Inheritable);
        var hin=stdin.ClientSafePipeHandle.DangerousGetHandle();var hout=stdout.ClientSafePipeHandle.DangerousGetHandle();var herr=stderr.ClientSafePipeHandle.DangerousGetHandle();
        IntPtr size=IntPtr.Zero;N.InitializeProcThreadAttributeList(IntPtr.Zero,2,0,ref size);if(size==IntPtr.Zero)throw new Win32Exception();
        IntPtr attrs=Marshal.AllocHGlobal(size),handles=Marshal.AllocHGlobal(3*IntPtr.Size),jobs=Marshal.AllocHGlobal(IntPtr.Size);var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);if(string.IsNullOrEmpty(windows))throw new InvalidOperationException();IntPtr env=Marshal.StringToHGlobalUni("SystemRoot="+windows+"\0\0");bool initialized=false;
        try{
            if(!N.InitializeProcThreadAttributeList(attrs,2,0,ref size))throw new Win32Exception();initialized=true;
            Marshal.WriteIntPtr(handles,0,hin);Marshal.WriteIntPtr(handles,IntPtr.Size,hout);Marshal.WriteIntPtr(handles,2*IntPtr.Size,herr);Marshal.WriteIntPtr(jobs,job.DangerousGetHandle());
            if(!N.UpdateProcThreadAttribute(attrs,0,(IntPtr)0x20002,handles,(IntPtr)(3*IntPtr.Size),IntPtr.Zero,IntPtr.Zero)||!N.UpdateProcThreadAttribute(attrs,0,(IntPtr)0x2000d,jobs,(IntPtr)IntPtr.Size,IntPtr.Zero,IntPtr.Zero))throw new Win32Exception();
            var si=new N.STARTUPINFOEX{Startup=new N.STARTUPINFO{cb=(uint)Marshal.SizeOf<N.STARTUPINFOEX>(),dwFlags=0x100,hStdInput=hin,hStdOutput=hout,hStdError=herr},attributes=attrs};
            var commandLine=new StringBuilder(string.Join(" ",new[]{command.Exe}.Concat(command.Args).Select(x=>"\""+x+"\"")));
            if(!N.CreateProcess(command.Exe,commandLine,IntPtr.Zero,IntPtr.Zero,true,0x00000004|0x00080000|0x08000000|0x00000400,env,null,ref si,out var pi))throw new Win32Exception();
            using var process=new SafeFileHandle(pi.hProcess,true);using var thread=new SafeFileHandle(pi.hThread,true);using var ioCancel=CancellationTokenSource.CreateLinkedTokenSource(token);Task[] tasks=[];
            try{
                stdin.DisposeLocalCopyOfClientHandle();stdout.DisposeLocalCopyOfClientHandle();stderr.DisposeLocalCopyOfClientHandle();
                if(!N.IsProcessInJob(process,job,out bool assigned)||!assigned||N.ResumeThread(thread)==uint.MaxValue)throw new InvalidOperationException("job assignment failed");
                async Task Send(){try{await stdin.WriteAsync(input,ioCancel.Token);await stdin.FlushAsync(ioCancel.Token);}finally{stdin.Dispose();}}
                async Task Exit(){while(true){uint status=N.WaitForSingleObject(process,0);if(status==0)return;if(status!=0x102)throw new Win32Exception();await Task.Delay(20,ioCancel.Token);}}
                var send=Send();var output=ReadBounded(stdout,2_000_000,ioCancel.Token);var error=ReadBounded(stderr,16_384,ioCancel.Token);var exit=Exit();tasks=[send,output,error,exit];
                var pending=tasks.ToList();while(pending.Count>0){var completed=await Task.WhenAny(pending).WaitAsync(token);await completed;pending.Remove(completed);}
                token.ThrowIfCancellationRequested();if(!N.GetExitCodeProcess(process,out uint code)||code!=0||error.Result.Length!=0)throw new InvalidDataException("worker failed");return output.Result;
            }finally{
                bool cleanupFailed=false;try{ioCancel.Cancel();}catch{cleanupFailed=true;}
                try{if(!N.TerminateJobObject(job,1))cleanupFailed=true;}catch{cleanupFailed=true;}
                foreach(var stream in new IDisposable[]{stdin,stdout,stderr})try{stream.Dispose();}catch{cleanupFailed=true;}
                try{if(N.WaitForSingleObject(process,3000)!=0)cleanupFailed=true;}catch{cleanupFailed=true;}
                try{var timer=System.Diagnostics.Stopwatch.StartNew();while(true){if(!N.QueryInformationJobObject(job,1,out var accounting,(uint)Marshal.SizeOf<N.ACCOUNTING>(),IntPtr.Zero)){cleanupFailed=true;break;}if(accounting.ActiveProcesses==0)break;if(timer.ElapsedMilliseconds>=3000){cleanupFailed=true;break;}await Task.Delay(20);}}catch{cleanupFailed=true;}
                try{await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(3));}catch(TimeoutException){cleanupFailed=true;}catch{ /* operation failures are handled separately */ }
                if(cleanupFailed)throw new InvalidOperationException("Synthetic process cleanup unconfirmed.");
            }
        }finally{if(initialized)N.DeleteProcThreadAttributeList(attrs);Marshal.FreeHGlobal(attrs);Marshal.FreeHGlobal(handles);Marshal.FreeHGlobal(jobs);Marshal.FreeHGlobal(env);}
    }
}
