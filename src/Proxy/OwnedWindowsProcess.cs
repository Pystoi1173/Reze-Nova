using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace RezeNova.Proxy
{
    internal sealed class OwnedWindowsProcess : IDisposable
    {
        private readonly KernelHandle job;
        private readonly KernelHandle root;

        private OwnedWindowsProcess(KernelHandle job, KernelHandle root)
        {
            this.job = job;
            this.root = root;
        }

        public static OwnedWindowsProcess Start(string executable, string arguments, string workingDirectory,
            IDictionary<string, string> environmentOverrides = null)
        {
            const uint CreateSuspended = 0x00000004;
            const uint CreateUnicodeEnvironment = 0x00000400;
            const uint CreateNoWindow = 0x08000000;
            KernelHandle job = CreateOwnedJob();
            KernelHandle root = null;
            bool started = false;
            IntPtr environment = IntPtr.Zero;
            try
            {
                if (environmentOverrides != null)
                    environment = BuildEnvironment(environmentOverrides);
                var startup = new StartupInfo();
                startup.Size = Marshal.SizeOf(startup);
                var commandLine = new StringBuilder("\"" + executable + "\"");
                if (!String.IsNullOrEmpty(arguments)) commandLine.Append(' ').Append(arguments);
                ProcessInformation process;
                if (!CreateProcess(executable, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                        CreateSuspended | CreateUnicodeEnvironment | CreateNoWindow,
                        environment, workingDirectory, ref startup, out process))
                    throw new Win32Exception();

                root = new KernelHandle(process.Process);
                using (var thread = new KernelHandle(process.Thread))
                {
                    // Assign before resuming so that even an immediate descendant stays in the job.
                    if (!AssignProcessToJobObject(job, root) || ResumeThread(thread) == UInt32.MaxValue)
                        throw new Win32Exception();
                }
                var owned = new OwnedWindowsProcess(job, root);
                started = true;
                return owned;
            }
            finally
            {
                if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
                if (!started)
                {
                    if (root != null)
                    {
                        TerminateProcess(root, 1);
                        root.Dispose();
                    }
                    job.Dispose();
                }
            }
        }

        public static OwnedWindowsProcess Attach(Process process)
        {
            KernelHandle job = CreateOwnedJob();
            KernelHandle root = null;
            try
            {
                root = new KernelHandle(process.Handle, false);
                if (!AssignProcessToJobObject(job, root)) throw new Win32Exception();
                return new OwnedWindowsProcess(job, root);
            }
            catch
            {
                if (root != null) root.Dispose();
                job.Dispose();
                throw;
            }
        }

        private static KernelHandle CreateOwnedJob()
        {
            const uint KillOnJobClose = 0x00002000;
            const int JobExtendedLimitInformation = 9;
            KernelHandle job = CreateJobObject(IntPtr.Zero, null);
            try
            {
                if (job.IsInvalid) throw new Win32Exception();
                var limits = new ExtendedLimitInformation();
                limits.BasicLimitInformation.LimitFlags = KillOnJobClose;
                if (!SetInformationJobObject(job, JobExtendedLimitInformation, ref limits, Marshal.SizeOf(limits)))
                    throw new Win32Exception();
                return job;
            }
            catch
            {
                job.Dispose();
                throw;
            }
        }

        public bool HasExited
        {
            get
            {
                uint result = WaitForSingleObject(root, 0);
                if (result == 0) return true;
                if (result == 258) return false;
                throw new Win32Exception();
            }
        }

        public bool OwnsProcess(uint pid)
        {
            const uint QueryLimitedInformation = 0x1000;
            using (KernelHandle process = OpenProcess(QueryLimitedInformation, false, pid))
            {
                bool inJob;
                return !process.IsInvalid && IsProcessInJob(process, job, out inJob) && inJob;
            }
        }

        public bool Stop(int timeoutMilliseconds)
        {
            if (timeoutMilliseconds < 0) throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            const int JobBasicAccountingInformation = 1;
            var clock = Stopwatch.StartNew();
            if (!TerminateJobObject(job, 0)) throw new Win32Exception();

            while (true)
            {
                BasicAccountingInformation stats;
                if (!QueryInformationJobObject(job, JobBasicAccountingInformation, out stats,
                        Marshal.SizeOf(typeof(BasicAccountingInformation)), IntPtr.Zero))
                    throw new Win32Exception();
                if (stats.ActiveProcesses == 0 && HasExited) return true;
                long remaining = timeoutMilliseconds - clock.ElapsedMilliseconds;
                if (remaining <= 0) return false;
                Thread.Sleep((int)Math.Min(remaining, 25));
            }
        }

        public void Dispose()
        {
            job.Dispose();
            root.Dispose();
        }

        private static IntPtr BuildEnvironment(IDictionary<string, string> changes)
        {
            var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                variables.Add((string)entry.Key, (string)entry.Value);
            foreach (KeyValuePair<string, string> change in changes)
            {
                if (change.Value == null) variables.Remove(change.Key);
                else variables[change.Key] = change.Value;
            }
            var block = new StringBuilder();
            foreach (KeyValuePair<string, string> variable in variables)
                block.Append(variable.Key).Append('=').Append(variable.Value).Append('\0');
            block.Append('\0');
            return Marshal.StringToHGlobalUni(block.ToString());
        }

        private sealed class KernelHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public KernelHandle() : base(true) { }
            public KernelHandle(IntPtr handle, bool ownsHandle = true) : base(ownsHandle) { SetHandle(handle); }
            protected override bool ReleaseHandle() { return CloseHandle(handle); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int Size;
            public string Reserved, Desktop, Title;
            public int X, Y, Width, Height, Columns, Rows, FillAttribute, Flags;
            public short ShowWindow, ReservedSize;
            public IntPtr ReservedBytes, StandardInput, StandardOutput, StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr Process, Thread;
            public uint ProcessId, ThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimitInformation
        {
            public BasicLimitInformation BasicLimitInformation;
            public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicAccountingInformation
        {
            public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
            public uint PageFaults, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern KernelHandle CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(KernelHandle job, int informationClass,
            ref ExtendedLimitInformation information, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryInformationJobObject(KernelHandle job, int informationClass,
            out BasicAccountingInformation information, int size, IntPtr returnLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(string executable, StringBuilder commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
            string workingDirectory, ref StartupInfo startup, out ProcessInformation process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(KernelHandle job, KernelHandle process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsProcessInJob(KernelHandle process, KernelHandle job, out bool result);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(KernelHandle thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(KernelHandle handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern KernelHandle OpenProcess(uint access, bool inheritHandle, uint pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(KernelHandle process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateJobObject(KernelHandle job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
