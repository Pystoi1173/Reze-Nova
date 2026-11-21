using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RezeNova.Core;
using RezeNova.Runtime;

internal static class RuntimeLifecycleTests
{
    public static int Main(string[] args)
    {
        if (args.Length != 0)
        {
            if (args[0] == "lock-start")
            {
                using (var mutex = Mutex.OpenExisting(args[1]))
                {
                    if (!mutex.WaitOne(5000)) return 2;
                    try
                    {
                        Console.WriteLine("locked");
                        Thread.Sleep(15000);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            }
            if (args[0] == "owner")
            {
                using (var service = new WinDivertRuntimeService(args[1], mode => Child("pid"), 4000))
                {
                    service.Start(NovaMode.Auto);
                    Console.WriteLine("running");
                    Thread.Sleep(15000);
                }
                return 0;
            }
            if (args[0] == "pid" || args[0] == "cancel")
                Console.WriteLine("child-pid=" + Process.GetCurrentProcess().Id);
            if (args[0] == "fail") return 37;
            if (args[0] == "lua-only") Console.WriteLine("windivert initialized");
            else if (args[0] != "timeout" && args[0] != "cancel")
                Console.WriteLine(RuntimeProfileBuilder.CaptureStartedMessage);
            if (args[0] != "timeout" && args[0] != "cancel" && args[0] != "capture-only")
                Console.Error.WriteLine(RuntimeProfileBuilder.LuaReadyMessage);
            Thread.Sleep(args[0] == "exit" ? 1500 : 15000);
            return 0;
        }

        string logs = Path.Combine(Path.GetTempPath(), "RezeNova-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Run(logs);
            Console.WriteLine("PASS: runtime lifecycle and profile checks");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            if (Directory.Exists(logs)) Directory.Delete(logs, true);
        }
    }

    private static void Run(string logs)
    {
        int launches = 0;
        using (var service = new WinDivertRuntimeService(logs, mode =>
        {
            launches++;
            return Child("hold");
        }, 4000))
        {
            Parallel.For(0, 12, _ => service.Start(NovaMode.Auto));
            Check(launches == 1 && service.State == RuntimeState.Running, "concurrent start launched more than once");
            service.Restart(NovaMode.Stable);
            Check(launches == 2 && service.CurrentMode == NovaMode.Stable, "restart did not switch mode");
            service.Stop();
            service.Stop();
            Check(service.State == RuntimeState.Stopped && service.CurrentMode == null, "stop not confirmed");
        }

        using (var service = new WinDivertRuntimeService(logs, mode => Child("exit"), 4000))
        {
            service.Start(NovaMode.Auto);
            Check(SpinWait.SpinUntil(() => service.State == RuntimeState.Failed, 5000), "unexpected exit not observed");
            Check(service.CurrentMode == null && service.LastError.Contains("exited unexpectedly"), "unexpected exit status incorrect");
        }

        CheckOwnerTermination(logs);
        CheckConcurrentNovaStartup(logs);
        CheckCancelledStart(logs);
        CheckCanRecover(logs, "fail", "code 37");
        CheckCanRecover(logs, "timeout", "missing WinDivert and Lua readiness");
        CheckCanRecover(logs, "capture-only", "missing Lua readiness");
        CheckCanRecover(logs, "lua-only", "missing WinDivert readiness");
        using (var service = new WinDivertRuntimeService(Path.Combine(logs, "missing-runtime"), logs))
        {
            ExpectFailure(service, "manifest", "runtime is incomplete");
            Check(service.State == RuntimeState.Failed, "manifest failure left lifecycle in Starting");
        }

        var builder = new RuntimeProfileBuilder(logs);
        foreach (NovaMode mode in Enum.GetValues(typeof(NovaMode)))
        {
            RuntimeProfile profile = builder.Build(mode);
            Check(Path.GetFileName(profile.EnginePath) == "winws2.exe", "incorrect engine");
            Check(profile.CommandLineArguments.Contains("--lua-init=@lua/zapret-lib.lua"), "missing Lua library");
            Check(profile.CommandLineArguments.Contains("--wf-udp-out=443,19294-19344,50000-50100"), "missing QUIC and Discord UDP interception");
            Check(profile.CommandLineArguments.Contains("--wf-raw-part=@windivert\\windivert_part.discord_media.txt"), "missing Discord media filter");
            Check(profile.CommandLineArguments.Contains("--wf-udp-out=443,19294-19344,50000-50100"), "missing official Discord UDP port range");
            Check(profile.CommandLineArguments.Contains("--blob=active_discord_udp:@fake\\active-discord-udp.bin"), "missing official Discord UDP fake blob");
            Check(profile.CommandLineArguments.Contains("--filter-udp=19294-19344,50000-50100"), "missing Discord UDP profile");
            Check(profile.CommandLineArguments.Contains("--filter-l7=discord,stun"), "Discord voice and STUN are not using the shared UDP profile");
            Check(profile.CommandLineArguments.Contains("--lua-desync=fake:blob=active_discord_udp:repeats=6"), "missing Discord voice UDP desync");
            Check(profile.CommandLineArguments.Contains("--hostlist-domains=gateway.discord.gg"), "missing dedicated Discord gateway profile");
            Check(profile.CommandLineArguments.Contains("--hostlist-domains=discord.media"), "missing dedicated Discord media profile");
            Check(profile.CommandLineArguments.Contains("--hostlist-domains=discord.com,discordapp.com,discordapp.net,discordcdn.com"), "missing dedicated Discord web profile");
            Check(profile.CommandLineArguments.Contains("--hostlist-exclude-domains=gateway.discord.gg,discord.com,discordapp.com,discordapp.net,discordcdn.com"), "Discord web hosts are not excluded from generic TLS desync");
            Check(profile.CommandLineArguments.Contains("--filter-tcp=2053,2083,2087,2096,8443"), "missing Discord media TCP ports");
            Check(profile.CommandLineArguments.Contains("--lua-desync=multisplit:pos=1:seqovl=681:seqovl_pattern=fake_default_tls"), "missing Discord gateway or media multisplit");
            Check(profile.CommandLineArguments.Contains("--hostlist-exclude-domains=gateway.discord.gg"), "Discord gateway is not excluded from generic TLS desync");
            Check(profile.CommandLineArguments.Contains("--wf-raw-part=@windivert\\windivert_part.stun.txt"), "missing STUN filter");
            Check(profile.CommandLineArguments.Contains("--wf-raw-part=@windivert\\windivert_part.wireguard.txt"), "missing WireGuard filter");
            Check(profile.CommandLineArguments.Contains("--wf-raw-part=@windivert\\windivert_part.quic_initial_ietf.txt"), "missing QUIC filter");
            Check(profile.CommandLineArguments.Contains(":badsum:"), "TCP fake profile does not corrupt checksums");
            Check(profile.CommandLineArguments.Contains("--hostlist=lists/services.txt"), "service and user host lists are missing from the profile");
        }
    }

    private static void CheckOwnerTermination(string logs)
    {
        string ownerLogs = Path.Combine(logs, "owner test");
        using (Process owner = Process.Start(Child("owner \"" + ownerLogs + "\"")))
        {
            Process ownedChild = null;
            try
            {
                var ready = owner.StandardOutput.ReadLineAsync();
                Check(ready.Wait(8000) && ready.Result == "running", "test owner did not start");
                ownedChild = WaitForChild(ownerLogs);
                owner.Kill();
                Check(owner.WaitForExit(5000), "test owner did not terminate");
                Check(ownedChild.WaitForExit(5000), "the runtime survived owner termination");
            }
            finally
            {
                if (!owner.HasExited) owner.Kill();
                owner.WaitForExit(5000);
                if (ownedChild != null) ownedChild.Dispose();
            }
        }
    }

    private static void CheckConcurrentNovaStartup(string logs)
    {
        string mutexName = @"Local\RezeNova-StartupTest-" + Guid.NewGuid().ToString("N");
        using (var mutex = new Mutex(false, mutexName))
        using (Process owner = Process.Start(Child("lock-start " + mutexName)))
        {
            int launches = 0;
            using (var service = new WinDivertRuntimeService(logs, mode =>
            {
                launches++;
                return Child("hold");
            }, 4000, mutexName))
            {
                try
                {
                    var ready = owner.StandardOutput.ReadLineAsync();
                    Check(ready.Wait(5000) && ready.Result == "locked", "test owner did not acquire the startup lock");
                    ExpectFailure(service, "concurrent Nova startup", "Another Nova instance is starting");
                    Check(launches == 0, "startup continued while another Nova held the lock");
                }
                finally
                {
                    if (!owner.HasExited) owner.Kill();
                    Check(owner.WaitForExit(5000), "startup lock owner did not terminate");
                }
                service.Start(NovaMode.Fast);
                Check(launches == 1 && service.State == RuntimeState.Running, "could not start after the lock owner exited");
            }
        }
    }

    private static void CheckCancelledStart(string logs)
    {
        string cancelLogs = Path.Combine(logs, "cancel test");
        using (var service = new WinDivertRuntimeService(cancelLogs, mode => Child(mode == NovaMode.Auto ? "cancel" : "hold"), 10000))
        using (var cancellation = new CancellationTokenSource())
        {
            var start = Task.Run(() =>
            {
                try { service.Start(NovaMode.Auto, cancellation.Token); }
                catch (OperationCanceledException) { return true; }
                return false;
            });
            try
            {
                using (Process child = WaitForChild(cancelLogs))
                {
                    cancellation.Cancel();
                    Check(start.Wait(5000) && start.Result, "start did not report cancellation");
                    Check(child.WaitForExit(5000), "cancelled child survived");
                }
            }
            finally { cancellation.Cancel(); }
            Check(service.State == RuntimeState.Stopped && service.CurrentMode == null, "cancelled start left stale state");
            service.Start(NovaMode.Fast);
            Check(service.State == RuntimeState.Running, "could not start after cancellation");
        }
    }

    private static Process WaitForChild(string logs)
    {
        const string marker = "child-pid=";
        int pid = 0;
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 4000)
        {
            if (Directory.Exists(logs))
            {
                foreach (string log in Directory.GetFiles(logs, "runtime-*.log"))
                {
                    using (var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            int index = line.IndexOf(marker, StringComparison.Ordinal);
                            if (index >= 0 && Int32.TryParse(line.Substring(index + marker.Length), out pid))
                            {
                                Process child = Process.GetProcessById(pid);
                                if (!child.HasExited) return child;
                                child.Dispose();
                            }
                        }
                    }
                }
            }
            Thread.Sleep(25);
        }
        throw new InvalidOperationException("Could not identify the running test child.");
    }

    private static void CheckCanRecover(string logs, string first, string expected)
    {
        using (var service = new WinDivertRuntimeService(logs, mode => Child(mode == NovaMode.Auto ? first : "hold"), 2000))
        {
            ExpectFailure(service, first, expected);
            Check(service.State == RuntimeState.Failed && service.CurrentMode == null, "failed start retained a child or mode");
            service.Start(NovaMode.Fast);
            Check(service.State == RuntimeState.Running, "could not recover from failed start");
        }
    }

    private static void ExpectFailure(WinDivertRuntimeService service, string scenario, string expected)
    {
        try { service.Start(NovaMode.Auto); }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains(expected), "wrong failure for " + scenario + ": " + error.Message);
            return;
        }
        throw new InvalidOperationException("Unexpected success for " + scenario);
    }

    private static ProcessStartInfo Child(string argument)
    {
        return new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, argument)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
    }

    private static int Count(string value, string fragment)
    {
        int count = 0;
        int position = 0;
        while ((position = value.IndexOf(fragment, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += fragment.Length;
        }
        return count;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
