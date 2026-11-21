using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using RezeNova.Core;
using RezeNova.Proxy;

namespace RezeNova.Runtime
{
    internal sealed class WinDivertRuntimeService : IDisposable
    {
        private readonly object sync = new object();
        private readonly object logSync = new object();
        private readonly string logDirectory;
        private readonly Func<NovaMode, ProcessStartInfo> prepare;
        private readonly int startupTimeoutMs;
        private readonly string startMutexName;
        private Process process;
        private OwnedWindowsProcess processJob;
        private RuntimeState state;
        private NovaMode? currentMode;
        private string lastError;
        private bool disposed;

        public WinDivertRuntimeService(string runtimeDirectory, string logDirectory)
            : this(logDirectory, mode => PrepareRuntime(runtimeDirectory, mode), 10000, @"Global\RezeNova-WinDivert-Startup")
        {
        }

        internal WinDivertRuntimeService(string logDirectory, Func<NovaMode, ProcessStartInfo> prepare,
            int startupTimeoutMs, string startMutexName = null)
        {
            this.logDirectory = Path.GetFullPath(logDirectory);
            this.prepare = prepare;
            this.startupTimeoutMs = startupTimeoutMs;
            this.startMutexName = startMutexName;
        }

        public RuntimeState State { get { lock (sync) return state; } }
        public NovaMode? CurrentMode { get { lock (sync) return currentMode; } }
        public string LastError { get { lock (sync) return lastError; } }

        public void Start(NovaMode mode)
        {
            Start(mode, CancellationToken.None);
        }

        public void Start(NovaMode mode, CancellationToken cancellationToken)
        {
            lock (sync)
            {
                ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                if (state == RuntimeState.Running && currentMode == mode && !process.HasExited) return;
                if (process != null) StopLocked();
                StartLocked(mode, cancellationToken);
            }
        }

        public void Restart(NovaMode mode)
        {
            lock (sync)
            {
                ThrowIfDisposed();
                StopLocked();
                StartLocked(mode, CancellationToken.None);
            }
        }

        public void Stop()
        {
            lock (sync)
            {
                if (!disposed) StopLocked();
            }
        }

        private void StartLocked(NovaMode mode, CancellationToken cancellationToken)
        {
            state = RuntimeState.Starting;
            lastError = null;
            string logPath = null;
            Process next = null;
            Mutex startMutex = null;
            bool ownsStartMutex = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (startMutexName != null)
                {
                    startMutex = new Mutex(false, startMutexName);
                    try { ownsStartMutex = startMutex.WaitOne(0); }
                    catch (AbandonedMutexException) { ownsStartMutex = true; }
                    if (!ownsStartMutex)
                        throw new InvalidOperationException("Another Nova instance is starting the system route.");
                }
                ProcessStartInfo info = prepare(mode);
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(logDirectory);
                logPath = Path.Combine(logDirectory, "runtime-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".log");
                File.WriteAllText(logPath, "Starting " + mode + Environment.NewLine, Encoding.UTF8);
                var readiness = new StartupReadiness();
                next = new Process { StartInfo = info, EnableRaisingEvents = true };
                next.OutputDataReceived += (sender, args) =>
                {
                    AppendLog(logPath, args.Data);
                    if (args.Data == RuntimeProfileBuilder.CaptureStartedMessage) readiness.CaptureStarted = true;
                };
                next.ErrorDataReceived += (sender, args) =>
                {
                    AppendLog(logPath, args.Data);
                    if (args.Data == RuntimeProfileBuilder.LuaReadyMessage) readiness.LuaReady = true;
                };
                next.Exited += (sender, args) =>
                    ThreadPool.QueueUserWorkItem(_ => RuntimeExited((Process)sender, logPath));

                cancellationToken.ThrowIfCancellationRequested();
                if (!next.Start()) throw new InvalidOperationException("Windows did not start the runtime.");
                process = next;
                currentMode = mode;
                try { processJob = OwnedWindowsProcess.Attach(next); }
                catch (System.ComponentModel.Win32Exception)
                {
                    if (next.HasExited) throw StartupExit(next);
                    throw;
                }
                next.BeginOutputReadLine();
                next.BeginErrorReadLine();
                var timer = Stopwatch.StartNew();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (next.HasExited) throw StartupExit(next);
                    bool captureStarted = readiness.CaptureStarted;
                    bool luaReady = readiness.LuaReady;
                    if (captureStarted && luaReady) break;

                    long remaining = startupTimeoutMs - timer.ElapsedMilliseconds;
                    if (remaining <= 0)
                    {
                        string missing = captureStarted ? "Lua" : luaReady ? "WinDivert" : "WinDivert and Lua";
                        throw new TimeoutException("Runtime did not confirm startup: missing " + missing + " readiness.");
                    }
                    if (next.WaitForExit((int)Math.Min(50, remaining))) throw StartupExit(next);
                }
                state = RuntimeState.Running;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (process != null) StopProcess();
                    else if (next != null) next.Dispose();
                    state = RuntimeState.Stopped;
                    AppendLog(logPath, "Runtime start cancelled.");
                }
                catch (Exception error)
                {
                    state = RuntimeState.Failed;
                    lastError = "Could not stop the cancelled runtime: " + error.Message;
                    AppendLog(logPath, lastError);
                    throw new InvalidOperationException(lastError, error);
                }
                throw;
            }
            catch (Exception error)
            {
                lastError = error.Message + (logPath == null ? String.Empty : " Log: " + logPath);
                try
                {
                    if (process != null) StopProcess();
                    else if (next != null) next.Dispose();
                }
                catch (Exception cleanupError)
                {
                    lastError += " Cleanup failed: " + cleanupError.Message;
                }
                state = RuntimeState.Failed;
                AppendLog(logPath, lastError);
                throw new InvalidOperationException(lastError, error);
            }
            finally
            {
                if (startMutex != null)
                {
                    if (ownsStartMutex) startMutex.ReleaseMutex();
                    startMutex.Dispose();
                }
            }
        }

        private void StopLocked()
        {
            state = RuntimeState.Stopping;
            try
            {
                if (process != null) StopProcess();
                state = RuntimeState.Stopped;
                lastError = null;
            }
            catch (Exception error)
            {
                state = RuntimeState.Failed;
                lastError = error.Message;
                throw;
            }
        }

        private static InvalidOperationException StartupExit(Process child)
        {
            return new InvalidOperationException("Runtime exited during startup with code " + child.ExitCode + ".");
        }

        private void StopProcess()
        {
            Process current = process;
            if (processJob != null)
            {
                if (!processJob.Stop(5000)) throw new TimeoutException("The runtime process job did not stop within five seconds.");
                processJob.Dispose();
                processJob = null;
            }
            else
            {
                if (!current.HasExited)
                {
                    try { current.Kill(); }
                    catch (InvalidOperationException) { if (!current.HasExited) throw; }
                }
                if (!current.WaitForExit(5000)) throw new TimeoutException("Runtime did not stop within five seconds.");
            }
            process = null;
            currentMode = null;
            current.Dispose();
        }

        private void RuntimeExited(Process exited, string logPath)
        {
            lock (sync)
            {
                if (!Object.ReferenceEquals(process, exited)) return;
                lastError = "Runtime exited unexpectedly with code " + exited.ExitCode + ". Log: " + logPath;
                try { StopProcess(); }
                catch (Exception error) { lastError += " Cleanup failed: " + error.Message; }
                state = RuntimeState.Failed;
                AppendLog(logPath, lastError);
            }
        }

        private void AppendLog(string path, string line)
        {
            if (path == null || String.IsNullOrEmpty(line)) return;
            lock (logSync)
            {
                try { File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + line + Environment.NewLine, Encoding.UTF8); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static ProcessStartInfo PrepareRuntime(string directory, NovaMode mode)
        {
            new RuntimeManifest(directory).Validate();
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("The WinDivert runtime requires an elevated console.");
            }
            CheckForActiveEngine("winws");
            CheckForActiveEngine("winws2");
            RuntimeProfile profile = new RuntimeProfileBuilder(directory).Build(mode);
            return new ProcessStartInfo
            {
                FileName = profile.EnginePath,
                WorkingDirectory = profile.WorkingDirectory,
                Arguments = profile.CommandLineArguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }

        private static void CheckForActiveEngine(string name)
        {
            Process[] engines = Process.GetProcessesByName(name);
            try
            {
                foreach (Process engine in engines)
                    if (!engine.HasExited)
                        throw new InvalidOperationException("A network runtime is already active (" + name + ", PID " + engine.Id + ").");
            }
            finally
            {
                foreach (Process engine in engines) engine.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException("WinDivertRuntimeService");
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                StopLocked();
                disposed = true;
            }
        }

        private sealed class StartupReadiness
        {
            public volatile bool CaptureStarted;
            public volatile bool LuaReady;
        }
    }
}
