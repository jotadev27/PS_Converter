using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PSConverter
{
    internal sealed class ProcessRunner
    {
        public async Task RunAsync(ToolLease tool, IEnumerable<string> arguments, string workspace,
            int timeoutSeconds, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var job = new ProcessJob())
            using (var process = new Process())
            {
                var elapsed = Stopwatch.StartNew();
                using (var timer = new System.Threading.Timer(delegate {
                    if (timeoutSeconds > 0 && elapsed.Elapsed.TotalSeconds >= timeoutSeconds)
                        try { deadline.Cancel(); } catch (ObjectDisposedException) { }
                }, null, timeoutSeconds > 0 ? 1000 : Timeout.Infinite, timeoutSeconds > 0 ? 1000 : Timeout.Infinite))
                {
                    var start = new ProcessStartInfo {
                        FileName = tool.Executable, Arguments = string.Join(" ", arguments.Select(CodecArguments.Quote)),
                        WorkingDirectory = workspace, UseShellExecute = false, CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    // Keep codec DLL lookup away from the input/output folder and unrelated PATH entries.
                    start.EnvironmentVariables["PATH"] = System.IO.Path.GetDirectoryName(tool.Executable) + ";" +
                        Environment.GetFolderPath(Environment.SpecialFolder.System);
                    process.StartInfo = start;
                    try { if (!process.Start()) throw new InvalidOperationException("Codec process did not start."); }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        if (ex.NativeErrorCode == 623)
                            throw new InvalidOperationException("Windows could not load this legacy encoder (DLL relocation). Conversion did not create an audio file.",ex);
                        if (ex.NativeErrorCode == 193 || ex.NativeErrorCode == 14001 || ex.NativeErrorCode == 126)
                            throw new InvalidOperationException(RuntimeRequirement(tool.Executable),ex);
                        throw;
                    }
                    try { job.Attach(process); }
                    catch { if (!process.HasExited) process.Kill(); throw; }
                    Task output = DrainAsync(process.StandardOutput);
                    Task error = DrainAsync(process.StandardError);
                    using (deadline.Token.Register(job.Terminate))
                    {
                        await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
                        job.Terminate();
                        await Task.WhenAll(output, error).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (deadline.IsCancellationRequested) throw new TimeoutException("The codec deadline expired. Increase it or use 0 for no deadline.");
                        if (process.ExitCode == unchecked((int)0xc0000135) || process.ExitCode == unchecked((int)0xc000007b))
                            throw new InvalidOperationException(RuntimeRequirement(tool.Executable));
                        if (process.ExitCode != 0) throw new InvalidOperationException("Codec failed with exit code " + process.ExitCode + ". Check bitrate and audio settings.");
                    }
                }
            }
        }

        internal static string RuntimeRequirement(string executable)
        {
            string name = System.IO.Path.GetFileName(executable);
            string runtime = name == "PS4_at9tool.exe" ? "Visual C++ 2013 (x86)" :
                name == "PSVita_at9tool.exe" ? "Visual C++ 2008 (x86)" :
                name == "PS3_at3tool.exe" ? "Visual C++ 2005 (x86)" : "the encoder's 32-bit runtime";
            return "The encoder runtime is missing or has the wrong architecture. Required: " + runtime + ". See docs/THIRD_PARTY.md.";
        }

        private static async Task DrainAsync(System.IO.StreamReader reader)
        {
            // Discard tool output in bounded chunks; codecs can print user paths and unbounded diagnostic text.
            var buffer = new char[4096];
            while (await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false) != 0) { }
        }
    }

    internal sealed class ProcessJob : IDisposable
    {
        private IntPtr handle;
        public ProcessJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            var limits = new ExtendedLimits();
            limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            int size = Marshal.SizeOf(typeof(ExtendedLimits));
            IntPtr memory = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limits, memory, false);
                if (!SetInformationJobObject(handle, 9, memory, (uint)size)) throw new System.ComponentModel.Win32Exception();
            }
            catch { Dispose(); throw; }
            finally { Marshal.FreeHGlobal(memory); }
        }
        public void Attach(Process process)
        { if (!AssignProcessToJobObject(handle, process.Handle)) throw new System.ComponentModel.Win32Exception(); }
        public void Terminate() { if (handle != IntPtr.Zero) TerminateJobObject(handle, 1); }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
        { public long ProcessTime, JobTime; public uint LimitFlags; public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters
        { public ulong ReadCount, WriteCount, OtherCount, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
        { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int info, IntPtr data, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] private static extern bool TerminateJobObject(IntPtr job, uint code);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
