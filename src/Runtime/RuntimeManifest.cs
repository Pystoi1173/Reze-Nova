using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace RezeNova.Runtime
{
    internal sealed class RuntimeManifest
    {
        private static readonly IDictionary<string, string> ReleaseHashes = new Dictionary<string, string>
        {
            { "bin\\winws2.exe", "9f25492575ff7c3f9e0d6211daac85704c309a0ca695423f0276d9ebde3156aa" },
            { "bin\\cygwin1.dll", "103104a52e5293ce418944725df19e2bf81ad9269b9a120d71d39028e821499b" },
            { "bin\\WinDivert.dll", "16abd6a029e65557c6a309bea7b13bf81fff4e193567582e1cddbf6719f323e0" },
            { "bin\\WinDivert64.sys", "8da085332782708d8767bcace5327a6ec7283c17cfb85e40b03cd2323a90ddc2" },
            { "lua\\zapret-lib.lua", "6bf528fef1f0fc3c65e189216fc8a06bb873f6e25d1c29d2bfa4695a09b73440" },
            { "lua\\zapret-antidpi.lua", "6d1fa9168e5de1682fef91fd174f68b58c50f6739ef00f42ca7db2b940b6246d" },
            { "fake\\quic_initial_www_google_com.bin", "f4589c57749f956bb30538197a521d7005f8b0a8723b4707e72405e51ddac50a" },
            { "fake\\discord-ip-discovery-with-port.bin", "073e84935196ec6a9701e6ad0df21de2b5fbfb7e3fd3ce6c5d9bba8e8654eb14" },
            { "fake\\stun.bin", "9cd5469309780ca56c0bd97266524a48c7ee529d02c3179cfecb20b260a59641" },
            { "fake\\active-discord-udp.bin", "2fe18b3bd20807d36704d0b072092ee49ae84edca907a4420ab9a0f0f28fddcf" },
            { "windivert\\windivert_part.discord_media.txt", "2f2deaf49ed68eb684258497134181753188f2b475161deb244488a53872b7c5" },
            { "windivert\\windivert_part.quic_initial_ietf.txt", "f243ddc22758b9e04838211a3815e43fb3abe615e6be29e6a812ae4c72b392e0" },
            { "windivert\\windivert_part.stun.txt", "2a14f470b00cd9704003e0cade500086398f9222c858d9ae944bdeb89f520595" },
            { "windivert\\windivert_part.wireguard.txt", "da68586267462b29e3e370203ce0369ae2baa25a07aa2deab5fb44d69fef27be" },
            { "LICENSE-zapret2.txt", "d089978dd77d53cb6aa5dff51cfdbff617e52dd44af1ac44a6df02c6644f17d5" }
        };

        private static readonly string[] Lists =
        {
            "lists\\services.txt", "lists\\include-user.txt", "lists\\exclude-user.txt"
        };

        private readonly string root;

        public RuntimeManifest(string root)
        {
            this.root = Path.GetFullPath(root);
        }

        public void Validate()
        {
            var missing = new List<string>();
            foreach (string relative in ReleaseHashes.Keys)
                if (!File.Exists(Path.Combine(root, relative))) missing.Add(relative);
            foreach (string relative in Lists)
                if (!File.Exists(Path.Combine(root, relative))) missing.Add(relative);
            if (missing.Count > 0)
                throw new FileNotFoundException("zapret2 v1.0.7 runtime is incomplete: " + String.Join(", ", missing));

            using (var sha = SHA256.Create())
            {
                foreach (var file in ReleaseHashes)
                {
                    using (var stream = File.OpenRead(Path.Combine(root, file.Key)))
                    {
                        string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                        if (!String.Equals(actual, file.Value, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Unexpected SHA-256 for " + file.Key);
                    }
                }
            }
        }
    }
}
