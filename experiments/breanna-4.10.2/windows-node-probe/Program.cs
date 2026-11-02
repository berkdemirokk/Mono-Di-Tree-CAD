using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Synthetic-only launch experiment. Never pass customer data to this program.
try
{
    if (!OperatingSystem.IsWindows() || args.Length < 3 || args.Length > 5 ||
        !Path.IsPathFullyQualified(args[0]) || !Path.IsPathFullyQualified(args[1]) ||
        !int.TryParse(args[2], out int timeoutMs) || timeoutMs < 1 || timeoutMs > 60000)
        throw new ArgumentException();
    bool invalidJob = false; int? cancelAfterMs = null;
    foreach (string option in args.Skip(3))
    {
        if (option == "--invalid-job" && !invalidJob) invalidJob = true;
        else if (option.StartsWith("--cancel-after-ms=", StringComparison.Ordinal) && cancelAfterMs is null &&
                 int.TryParse(option.AsSpan("--cancel-after-ms=".Length), out int value) && value >= 1 && value <= 60000) cancelAfterMs = value;
        else throw new ArgumentException();
    }
    using var deadline = new CancellationTokenSource(timeoutMs);
    using var syntheticCancel = new CancellationTokenSource();
    if (cancelAfterMs.HasValue) syntheticCancel.CancelAfter(cancelAfterMs.Value);
    using var combined = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, syntheticCancel.Token);
    byte[] request = await ReadBounded(Console.OpenStandardInput(), 32768, combined.Token);
    if (request.Length == 0) throw new InvalidOperationException();
    byte[] response = await Run(args[0], args[1], invalidJob, request, combined.Token);
    PreviewResponseValidator.Validate(request,response);
    await Console.OpenStandardOutput().WriteAsync(response, combined.Token);
}
catch { Console.Error.WriteLine("Synthetic worker failed."); Environment.ExitCode = 1; }

static async Task<byte[]> ReadBounded(Stream stream, int limit, CancellationToken token)
{
    using var result = new MemoryStream();
    byte[] buffer = new byte[8192];
    while (true)
    {
        int n = await stream.ReadAsync(buffer, token);
        if (n == 0) return result.ToArray();
        if (result.Length + n > limit) throw new InvalidOperationException();
        result.Write(buffer, 0, n);
    }
}

static async Task<byte[]> Run(string exe, string script, bool invalidJob, byte[] input, CancellationToken token)
{
    using var job = N.CreateJobObject(IntPtr.Zero, null);
    if (job.IsInvalid) throw new Win32Exception();
    var limits = new N.EXTENDED(); limits.Basic.Flags = 0x2000;
    int len = Marshal.SizeOf<N.EXTENDED>(); IntPtr limit = Marshal.AllocHGlobal(len);
    try { Marshal.StructureToPtr(limits, limit, false); if (!N.SetInformationJobObject(job, 9, limit, (uint)len)) throw new Win32Exception(); }
    finally { Marshal.FreeHGlobal(limit); }
    using var stdin = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
    using var stdout = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
    using var stderr = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
    IntPtr hin = stdin.ClientSafePipeHandle.DangerousGetHandle(), hout = stdout.ClientSafePipeHandle.DangerousGetHandle(), herr = stderr.ClientSafePipeHandle.DangerousGetHandle();
    IntPtr size = IntPtr.Zero; N.InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref size);
    if (size == IntPtr.Zero) throw new Win32Exception();
    IntPtr attrs = Marshal.AllocHGlobal(size), handles = Marshal.AllocHGlobal(3 * IntPtr.Size), jobs = Marshal.AllocHGlobal(IntPtr.Size);
    string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    if (string.IsNullOrEmpty(windowsDirectory)) throw new InvalidOperationException();
    IntPtr environment = Marshal.StringToHGlobalUni("SystemRoot=" + windowsDirectory + "\0\0");
    bool initialized = false;
    try
    {
        if (!N.InitializeProcThreadAttributeList(attrs, 2, 0, ref size)) throw new Win32Exception(); initialized = true;
        Marshal.WriteIntPtr(handles, 0, hin); Marshal.WriteIntPtr(handles, IntPtr.Size, hout); Marshal.WriteIntPtr(handles, 2 * IntPtr.Size, herr);
        Marshal.WriteIntPtr(jobs, invalidJob ? IntPtr.Zero : job.DangerousGetHandle());
        if (!N.UpdateProcThreadAttribute(attrs, 0, (IntPtr)0x20002, handles, (IntPtr)(3 * IntPtr.Size), IntPtr.Zero, IntPtr.Zero) ||
            !N.UpdateProcThreadAttribute(attrs, 0, (IntPtr)0x2000d, jobs, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
        var si = new N.STARTUPINFOEX { Startup = new N.STARTUPINFO { cb = (uint)Marshal.SizeOf<N.STARTUPINFOEX>(), dwFlags = 0x100, hStdInput = hin, hStdOutput = hout, hStdError = herr }, attributes = attrs };
        // Windows paths cannot contain quotes; quoting prevents spaces being interpreted as argument boundaries.
        var command = new StringBuilder($"\"{exe}\" \"{script}\"");
        if (!N.CreateProcess(exe, command, IntPtr.Zero, IntPtr.Zero, true, 0x00000004 | 0x00080000 | 0x08000000 | 0x00000400, environment, null, ref si, out var pi)) throw new Win32Exception();
        using var process = new SafeFileHandle(pi.hProcess, true); using var thread = new SafeFileHandle(pi.hThread, true);
        try
        {
            if (invalidJob) throw new InvalidOperationException();
            stdin.DisposeLocalCopyOfClientHandle(); stdout.DisposeLocalCopyOfClientHandle(); stderr.DisposeLocalCopyOfClientHandle();
            if (!N.IsProcessInJob(process, job, out bool assigned) || !assigned || N.ResumeThread(thread) == uint.MaxValue) throw new InvalidOperationException();
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var registration = cancel.Token.Register(() => { stdin.Dispose(); stdout.Dispose(); stderr.Dispose(); });
            async Task Send() { try { await stdin.WriteAsync(input, cancel.Token); await stdin.FlushAsync(cancel.Token); } finally { stdin.Dispose(); } }
            Task send = Send(); Task<byte[]> readOut = ReadBounded(stdout, 1024 * 1024, cancel.Token); Task<byte[]> readErr = ReadBounded(stderr, 65536, cancel.Token);
            foreach (Task task in new Task[] { send, readOut, readErr }) _ = task.ContinueWith(t => { if (t.IsFaulted) cancel.Cancel(); }, TaskScheduler.Default);
            var all = Task.WhenAll(send, readOut, readErr);
            try { await all.WaitAsync(token); }
            catch { cancel.Cancel(); throw; }
            while (N.WaitForSingleObject(process, 50) == 0x102) token.ThrowIfCancellationRequested();
            if (!N.GetExitCodeProcess(process, out uint code) || code != 0) throw new InvalidOperationException();
            return await readOut;
        }
        finally
        {
            if (!N.TerminateJobObject(job, 1)) throw new Win32Exception();
            if (N.WaitForSingleObject(process, 3000) != 0) { N.TerminateProcess(process, 1); if (N.WaitForSingleObject(process, 3000) != 0) throw new InvalidOperationException(); }
            var until = System.Diagnostics.Stopwatch.StartNew();
            do { if (!N.QueryInformationJobObject(job, 1, out var accounting, (uint)Marshal.SizeOf<N.ACCOUNTING>(), IntPtr.Zero)) throw new Win32Exception(); if (accounting.ActiveProcesses == 0) break; if (until.ElapsedMilliseconds >= 3000) throw new InvalidOperationException(); await Task.Delay(20); } while (true);
        }
    }
    finally { if (initialized) N.DeleteProcThreadAttributeList(attrs); Marshal.FreeHGlobal(attrs); Marshal.FreeHGlobal(handles); Marshal.FreeHGlobal(jobs); Marshal.FreeHGlobal(environment); }
}




