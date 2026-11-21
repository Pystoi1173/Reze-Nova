using System;
using System.Collections.Generic;
using System.IO;

namespace RezeNova.Core
{
    internal sealed class RuntimeProfileBuilder
    {
        internal const string CaptureStartedMessage = "windivert initialized. capture is started.";
        internal const string LuaReadyMessage = "REZE_NOVA_LUA_READY";
        private readonly string runtimeDirectory;

        public RuntimeProfileBuilder(string runtimeDirectory)
        {
            this.runtimeDirectory = Path.GetFullPath(runtimeDirectory);
        }

        public RuntimeProfile Build(NovaMode mode)
        {
            if (!Enum.IsDefined(typeof(NovaMode), mode)) throw new ArgumentOutOfRangeException("mode");
            bool wide = mode == NovaMode.Wide;
            string customGamePorts = LoadCustomGamePorts();
            int hostRepeats = mode == NovaMode.Fast ? 6 : mode == NovaMode.Wide ? 13 : 11;
            int genericRepeats = mode == NovaMode.Fast ? 3 : mode == NovaMode.Wide ? 8 : 6;
            string hostPosition = mode == NovaMode.Fast ? "2" : "1,midsld";
            string genericPosition = mode == NovaMode.Fast ? "2" : "midsld";
            var args = new List<string>
            {
                "--wf-tcp-out=" + AddCustomPorts("80,443,2053,2083,2087,2096,8443", customGamePorts),
                "--wf-udp-out=" + AddCustomPorts("443,19294-19344,50000-50100", customGamePorts),
                "--wf-raw-part=@windivert\\windivert_part.discord_media.txt",
                "--wf-raw-part=@windivert\\windivert_part.stun.txt",
                "--wf-raw-part=@windivert\\windivert_part.wireguard.txt",
                "--wf-raw-part=@windivert\\windivert_part.quic_initial_ietf.txt",
                "--blob=active_discord_udp:@fake\\active-discord-udp.bin",
                "--lua-init=@lua/zapret-lib.lua",
                "--lua-init=@lua/zapret-antidpi.lua",
                // Flush WinDivert's stdout message, which may be buffered when captured through a pipe.
                "--lua-init=io.stdout:flush();io.stderr:write(string.char(82,69,90,69,95,78,79,86,65,95,76,85,65,95,82,69,65,68,89,10));io.stderr:flush()"
            };

            args.Add("--filter-tcp=80");
            args.Add("--filter-l7=http");
            args.Add("--out-range=-d10");
            args.Add("--payload=http_req");
            args.Add("--lua-desync=fake:blob=fake_default_http:badsum:repeats=" + genericRepeats);
            args.Add("--lua-desync=fakedsplit:badsum");
            AddExcludeList(args);
            args.Add("--new=web-tls-targeted");

            args.Add("--filter-tcp=443");
            args.Add("--filter-l7=tls");
            args.Add("--hostlist-domains=gateway.discord.gg");
            args.Add("--out-range=-d10");
            args.Add("--payload=tls_client_hello");
            args.Add("--lua-desync=multisplit:pos=1:seqovl=681:seqovl_pattern=fake_default_tls");
            args.Add("--new=discord-gateway-tls");

            args.Add("--filter-tcp=2053,2083,2087,2096,8443");
            args.Add("--filter-l7=tls");
            args.Add("--hostlist-domains=discord.media");
            args.Add("--out-range=-d10");
            args.Add("--payload=tls_client_hello");
            args.Add("--lua-desync=multisplit:pos=1:seqovl=681:seqovl_pattern=fake_default_tls");
            args.Add("--new=discord-media-tls");

            args.Add("--filter-tcp=443");
            args.Add("--filter-l7=tls");
            args.Add("--hostlist-domains=discord.com,discordapp.com,discordapp.net,discordcdn.com");
            args.Add("--hostlist-exclude-domains=gateway.discord.gg");
            args.Add("--out-range=-d10");
            args.Add("--payload=tls_client_hello");
            args.Add("--lua-desync=multisplit:pos=1:seqovl=681:seqovl_pattern=fake_default_tls");
            args.Add("--new=discord-web-tls");

            args.Add("--filter-tcp=443");
            args.Add("--filter-l7=tls");
            AddServiceLists(args);
            args.Add("--hostlist-exclude-domains=gateway.discord.gg,discord.com,discordapp.com,discordapp.net,discordcdn.com");
            args.Add("--out-range=-d10");
            args.Add("--payload=tls_client_hello");
            args.Add("--lua-desync=fake:blob=fake_default_tls:badsum:repeats=" + hostRepeats + ":tls_mod=rnd,dupsid,sni=www.google.com");
            args.Add("--lua-desync=multidisorder:pos=" + hostPosition);
            args.Add("--new=web-tls-generic");

            args.Add("--filter-tcp=443");
            args.Add("--filter-l7=tls");
            AddExcludeList(args);
            args.Add("--hostlist-exclude-domains=gateway.discord.gg");
            args.Add("--out-range=-d10");
            args.Add("--payload=tls_client_hello");
            args.Add("--lua-desync=fake:blob=fake_default_tls:badsum:tcp_seq=-10000:repeats=" + genericRepeats);
            args.Add("--lua-desync=multidisorder:pos=" + genericPosition);
            args.Add("--new=quic-targeted");

            args.Add("--filter-udp=443");
            args.Add("--filter-l7=quic");
            AddServiceLists(args);
            args.Add("--payload=quic_initial");
            args.Add("--lua-desync=fake:blob=fake_default_quic:repeats=" + hostRepeats);
            args.Add("--new=quic-generic");

            args.Add("--filter-udp=443");
            args.Add("--filter-l7=quic");
            AddExcludeList(args);
            args.Add("--payload=quic_initial");
            args.Add("--lua-desync=fake:blob=fake_default_quic:repeats=" + genericRepeats);
            args.Add("--new=voice");

            args.Add("--filter-udp=19294-19344,50000-50100");
            args.Add("--filter-l7=discord,stun");
            args.Add("--lua-desync=fake:blob=active_discord_udp:repeats=6");
            args.Add("--new=discord-voice-udp");

            args.Add("--filter-l7=wireguard");
            args.Add("--payload=wireguard_initiation,wireguard_cookie");
            args.Add("--lua-desync=fake:blob=0x00000000000000000000000000000000:repeats=2");
            AddCustomGameProfiles(args, customGamePorts);
            return new RuntimeProfile(mode, Path.Combine(runtimeDirectory, "bin", "winws2.exe"), runtimeDirectory, args);
        }

        private void AddCustomGameProfiles(ICollection<string> args, string ports)
        {
            if (String.IsNullOrWhiteSpace(ports)) return;
            args.Add("--filter-tcp=" + ports);
            args.Add("--ipset=lists/ipset-user.txt");
            args.Add("--lua-desync=fake:blob=fake_default_tls:badsum:repeats=6");
            args.Add("--new=custom-game-tcp");
            args.Add("--filter-udp=" + ports);
            args.Add("--ipset=lists/ipset-user.txt");
            args.Add("--lua-desync=fake:blob=fake_default_quic:repeats=6");
            args.Add("--new=custom-game-udp");
        }

        private string LoadCustomGamePorts()
        {
            string path = Path.Combine(runtimeDirectory, "lists", "game-ports.txt");
            if (!File.Exists(path)) return String.Empty;
            var ports = new List<string>();
            foreach (string raw in File.ReadAllLines(path))
            {
                string value = raw.Trim();
                int port;
                if (Int32.TryParse(value, out port) && port > 0 && port < 65536 && !ports.Contains(value)) ports.Add(value);
            }
            return String.Join(",", ports);
        }

        private static string AddCustomPorts(string basePorts, string customPorts)
        {
            return String.IsNullOrWhiteSpace(customPorts) ? basePorts : basePorts + "," + customPorts;
        }

        private static void AddServiceLists(ICollection<string> args)
        {
            args.Add("--hostlist=lists/services.txt");
            args.Add("--hostlist=lists/include-user.txt");
            AddExcludeList(args);
        }

        private static void AddExcludeList(ICollection<string> args)
        {
            args.Add("--hostlist-exclude=lists/exclude-user.txt");
        }
    }
}
