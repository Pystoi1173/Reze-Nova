using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RezeNova.Proxy
{
    internal sealed class TelegramProxyService : ITelegramProxyService
    {
        private const int ListenPort = 1443;
        private const int StartTimeoutMilliseconds = 15000;
        private const int StopTimeoutMilliseconds = 3000;
        private readonly object lifecycle = new object();
        private readonly object snapshot = new object();
        private readonly TelegramProxyFiles files;
        private TelegramProxyStatus status = new TelegramProxyStatus(TelegramProxyState.Stopped, ListenPort, 0, false, null);
        private CancellationTokenSource startCancellation;
        private bool disposed;

        private OwnedWindowsProcess process;
        private string runDirectory;
        private string secret;

        public TelegramProxyService(string distributionRoot)
        {
            files = new TelegramProxyFiles(distributionRoot);
        }

        public TelegramProxyStatus Status
        {
            get
            {
                lock (snapshot)
                {
                    if (status.State != TelegramProxyState.Running) return status;
                }
                lock (lifecycle)
                {
                    RefreshRunningStatus();
                    lock (snapshot) return status;
                }
            }
        }

        public Uri ConnectionUri
        {
            get
            {
                lock (snapshot)
                {
                    if (disposed || status.State != TelegramProxyState.Running) return null;
                }
                lock (lifecycle)
                {
                    RefreshRunningStatus();
                    lock (snapshot)
                    {
                        if (disposed || status.State != TelegramProxyState.Running) return null;
                        return new Uri("tg://proxy?server=127.0.0.1&port=" + ListenPort.ToString(CultureInfo.InvariantCulture)
                            + "&secret=dd" + secret);
                    }
                }
            }
        }

        public void Start(CancellationToken cancellationToken)
        {
            lock (lifecycle)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RefreshRunningStatus();
                lock (snapshot)
                {
                    if (disposed) throw new ObjectDisposedException("TelegramProxyService");
                    if (status.State == TelegramProxyState.Running) return;
                    startCancellation = new CancellationTokenSource();
                    SetStatus(TelegramProxyState.Starting);
                }

                string error = "Could not start the Nova Telegram proxy.";
                try
                {
                    using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, startCancellation.Token))
                    {
                        CancellationToken token = cancellation.Token;
                        var clock = Stopwatch.StartNew();
                        token.ThrowIfCancellationRequested();
                        error = "The Telegram proxy port 127.0.0.1:1443 is already in use or unavailable.";
                        CheckPortAvailable();
                        error = "TG WS Proxy is already running in this Windows session. Close that instance before starting Nova's proxy.";
                        CheckUpstreamInstance();

                        error = "Could not prepare Nova's private Telegram proxy settings.";
                        runDirectory = files.CreateRunDirectory();
                        secret = files.LoadOrCreateSecret();
                        TelegramProxyFiles.WriteConfiguration(runDirectory, ListenPort, secret);
                        error = "Could not read or verify the bundled TG WS Proxy executable. Run package-telegram.ps1.";
                        string executable;
                        using (files.StageVerifiedEngine(runDirectory, out executable))
                        {
                            token.ThrowIfCancellationRequested();
                            if (clock.ElapsedMilliseconds >= StartTimeoutMilliseconds)
                            {
                                error = "Preparing TG WS Proxy exceeded the 15-second startup deadline.";
                                throw new TimeoutException();
                            }
                            error = "Windows could not launch the bundled TG WS Proxy in Nova's process job.";
                            process = OwnedWindowsProcess.Start(executable, "--portable", runDirectory, ChildEnvironment());
                        }

                        error = "Windows could not verify the Telegram proxy's loopback listener and ownership.";
                        while (clock.ElapsedMilliseconds < StartTimeoutMilliseconds)
                        {
                            token.ThrowIfCancellationRequested();
                            if (process.HasExited)
                            {
                                error = "The bundled TG WS Proxy exited before opening its listener.";
                                throw new InvalidOperationException();
                            }
                            int clients;
                            if (TelegramProxyListener.TryInspect(process, ListenPort, out clients))
                            {
                                token.ThrowIfCancellationRequested();
                                SetStatus(TelegramProxyState.Running, clients, clients > 0);
                                return;
                            }
                            token.WaitHandle.WaitOne((int)Math.Min(100, Math.Max(0, StartTimeoutMilliseconds - clock.ElapsedMilliseconds)));
                        }
                        token.ThrowIfCancellationRequested();
                        error = "The bundled TG WS Proxy did not open an owned loopback listener within 15 seconds.";
                        throw new TimeoutException();
                    }
                }
                catch (OperationCanceledException)
                {
                    bool stopped = ReleaseRun();
                    SetStatus(stopped ? TelegramProxyState.Stopped : TelegramProxyState.Failed,
                        error: stopped ? null : "Windows could not confirm that the cancelled Telegram proxy stopped.");
                    throw;
                }
                catch (Exception)
                {
                    if (!ReleaseRun()) error += " Windows could not confirm that the proxy stopped.";
                    SetStatus(TelegramProxyState.Failed, error: error);
                    throw new InvalidOperationException(error);
                }
                finally
                {
                    lock (snapshot)
                    {
                        startCancellation.Dispose();
                        startCancellation = null;
                    }
                }
            }
        }

        public void Stop()
        {
            CancelStart(false);
            lock (lifecycle) StopLocked();
        }

        public void Dispose()
        {
            CancelStart(true);
            lock (lifecycle) StopLocked();
        }

        private void CancelStart(bool disposing)
        {
            lock (snapshot)
            {
                if (disposing) disposed = true;
                if (startCancellation != null) startCancellation.Cancel();
            }
        }

        private void StopLocked()
        {
            lock (snapshot)
            {
                if (status.State == TelegramProxyState.Stopped) return;
                SetStatus(TelegramProxyState.Stopping);
            }
            if (ReleaseRun()) SetStatus(TelegramProxyState.Stopped);
            else SetStatus(TelegramProxyState.Failed, error: "Windows could not confirm that the Telegram proxy job stopped within three seconds.");
        }

        private bool ReleaseRun()
        {
            secret = null;
            bool stopped = true;
            if (process != null)
            {
                try { stopped = process.Stop(StopTimeoutMilliseconds); }
                catch (System.ComponentModel.Win32Exception) { stopped = false; }
                finally
                {
                    process.Dispose(); // Kill-on-close is the final fallback for the owned job.
                    process = null;
                }
            }
            if (runDirectory != null)
            {
                try { Directory.Delete(runDirectory, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                finally { runDirectory = null; }
            }
            return stopped;
        }

        private void RefreshRunningStatus()
        {
            lock (snapshot)
            {
                if (status.State != TelegramProxyState.Running) return;
            }
            string error = "The Nova Telegram proxy no longer owns its loopback listener.";
            try
            {
                int clients;
                if (process.HasExited)
                    error = "The bundled TG WS Proxy exited unexpectedly.";
                else if (TelegramProxyListener.TryInspect(process, ListenPort, out clients))
                {
                    SetStatus(TelegramProxyState.Running, clients, clients > 0);
                    return;
                }
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            if (!ReleaseRun()) error += " Windows could not confirm that the proxy stopped.";
            SetStatus(TelegramProxyState.Failed, error: error);
        }

        private void SetStatus(TelegramProxyState state, int clients = 0, bool desktopVerified = false, string error = null)
        {
            lock (snapshot) status = new TelegramProxyStatus(state, ListenPort, clients, desktopVerified, error);
        }

        private static void CheckPortAvailable()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.ExclusiveAddressUse = true;
                socket.Bind(new IPEndPoint(IPAddress.Loopback, ListenPort));
            }
        }

        private static void CheckUpstreamInstance()
        {
            Mutex upstream;
            if (!Mutex.TryOpenExisting(@"Local\TgWsProxy_SingleInstance", out upstream)) return;
            upstream.Dispose();
            throw new InvalidOperationException();
        }

        private IDictionary<string, string> ChildEnvironment()
        {
            string appData = Directory.CreateDirectory(Path.Combine(runDirectory, "app-data")).FullName;
            string temp = Directory.CreateDirectory(Path.Combine(runDirectory, "temp")).FullName;
            return new Dictionary<string, string>
            {
                { "APPDATA", appData },
                { "LOCALAPPDATA", appData },
                { "TEMP", temp },
                { "TMP", temp },
                { "PYINSTALLER_RESET_ENVIRONMENT", "1" },
                // The release falls back to console mode on pystray's ImportError. This keeps its settings UI out of Nova.
                { "PYSTRAY_BACKEND", "nova-headless" }
            };
        }
    }
}
