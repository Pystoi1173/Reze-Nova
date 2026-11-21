using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using RezeNova.Core;
#if NOVA_UI
using System.Windows;
using RezeNova.UI;
#endif
using RezeNova.Proxy;
using RezeNova.Runtime;

namespace RezeNova
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string runtime = Path.Combine(root, "runtime");
            string logs = Path.Combine(root, "logs");
            try
            {
                if (args.Length == 0 || (args.Length == 1 && IsCommand(args[0], "self-test")))
                    return SelfTest(runtime);
#if NOVA_UI
                if (args.Length == 1 && IsCommand(args[0], "ui"))
                    return RunUi(root);
#endif
                if (args.Length == 3 && IsCommand(args[0], "self-test") && IsCommand(args[1], "--runtime-dir"))
                    return SelfTest(Path.GetFullPath(args[2]));

                if (args.Length <= 2 && IsCommand(args[0], "start"))
                {
                    NovaMode mode = ParseMode(args.Length == 2 ? args[1] : "auto");
                    using (var service = new WinDivertRuntimeService(runtime, logs))
                    {
                        RunSystemRoute(service, mode);
                    }
                    return 0;
                }

                if (IsCommand(args[0], "telegram") &&
                    (args.Length == 1 || (args.Length == 2 && IsCommand(args[1], "--open"))))
                    return RunTelegram(root, args.Length == 2);

                Console.Error.WriteLine("Usage: Reze-Nova.exe ui | self-test [--runtime-dir PATH] | start [auto|fast|stable|wide] | telegram [--open]");
                return 2;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }

#if NOVA_UI
        private static int RunUi(string root)
        {
            int result = 1;
            Exception failure = null;
            var thread = new Thread(new ThreadStart(delegate
            {
                try
                {
                    var app = new Application();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    var window = new NovaWindow(root);
                    app.MainWindow = window;
                    app.Run(window);
                    result = 0;
                }
                catch (Exception error) { failure = error; }
            }));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
            return result;
        }
#endif

        private static int RunTelegram(string root, bool openInDesktop)
        {
            using (var service = new TelegramProxyService(root))
            using (var cancellation = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler handler = (sender, args) =>
                {
                    args.Cancel = true;
                    try { cancellation.Cancel(); }
                    catch (ObjectDisposedException) { }
                };
                Console.CancelKeyPress += handler;
                try
                {
                    service.Start(cancellation.Token);
                    Console.WriteLine("Telegram proxy listening on 127.0.0.1:{0}. Press Ctrl+C to stop.", service.Status.Port);
                    if (openInDesktop)
                    {
                        Uri uri = service.ConnectionUri;
                        if (uri == null) throw new InvalidOperationException(service.Status.LastError ?? "The Telegram proxy stopped.");
                        using (Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })) { }
                    }
                    if (Console.IsInputRedirected)
                    {
                        var input = new Thread(() =>
                        {
                            string command = Console.ReadLine();
                            if (IsCommand(command, "stop"))
                            {
                                try { cancellation.Cancel(); }
                                catch (ObjectDisposedException) { }
                            }
                        }) { IsBackground = true };
                        input.Start();
                    }
                    while (service.Status.State == TelegramProxyState.Running && !cancellation.Token.WaitHandle.WaitOne(500)) { }
                    if (service.Status.State == TelegramProxyState.Failed)
                        throw new InvalidOperationException(service.Status.LastError);
                    service.Stop();
                    if (service.Status.State == TelegramProxyState.Failed)
                        throw new InvalidOperationException(service.Status.LastError);
                    return 0;
                }
                catch (OperationCanceledException)
                {
                    if (service.Status.State == TelegramProxyState.Failed)
                        throw new InvalidOperationException(service.Status.LastError);
                    return 0;
                }
                finally { Console.CancelKeyPress -= handler; }
            }
        }

        private static void RunSystemRoute(WinDivertRuntimeService service, NovaMode mode)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler handler = (sender, args) =>
                {
                    args.Cancel = true;
                    try { cancellation.Cancel(); }
                    catch (ObjectDisposedException) { }
                };
                Console.CancelKeyPress += handler;
                try
                {
                    service.Start(mode, cancellation.Token);
                    Console.WriteLine("Capture initialized ({0}). Press Ctrl+C to stop.", mode);
                    while (service.State == RuntimeState.Running && !cancellation.Token.WaitHandle.WaitOne(500)) { }
                    if (service.State == RuntimeState.Failed)
                        throw new InvalidOperationException(service.LastError);
                    service.Stop();
                }
                catch (OperationCanceledException)
                {
                    if (!cancellation.IsCancellationRequested) throw;
                }
                finally { Console.CancelKeyPress -= handler; }
            }
        }

        private static int SelfTest(string runtime)
        {
            new RuntimeManifest(runtime).Validate();
            foreach (NovaMode mode in Enum.GetValues(typeof(NovaMode)))
            {
                var profile = new RuntimeProfileBuilder(runtime).Build(mode);
                Console.WriteLine("{0} profile built ({1} arguments)", mode, profile.Arguments.Count);
            }
            Console.WriteLine("Runtime manifest validated.");
            return 0;
        }

        private static bool IsCommand(string input, string command)
        {
            return String.Equals(input, command, StringComparison.OrdinalIgnoreCase);
        }

        private static NovaMode ParseMode(string value)
        {
            if (IsCommand(value, "auto")) return NovaMode.Auto;
            if (IsCommand(value, "fast")) return NovaMode.Fast;
            if (IsCommand(value, "stable")) return NovaMode.Stable;
            if (IsCommand(value, "wide")) return NovaMode.Wide;
            throw new ArgumentException("Unknown mode. Use auto, fast, stable, or wide.");
        }
    }
}
