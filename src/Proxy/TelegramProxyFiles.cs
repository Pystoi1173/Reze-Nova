using System;
using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace RezeNova.Proxy
{
    internal sealed class TelegramProxyFiles
    {
        private const string ExpectedSha256 = "454205cbcf76f333934108d893b58a528373bce8096624e27f0b6dc16eea660f";
        private const string EngineName = "TgWsProxy.exe";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly string bundledEngine;
        private readonly string profileRoot;

        public TelegramProxyFiles(string distributionRoot)
        {
            bundledEngine = Path.Combine(Path.GetFullPath(distributionRoot), "telegram-proxy", EngineName);
            string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (String.IsNullOrEmpty(localData))
                throw new InvalidOperationException("Local application data is unavailable.");
            profileRoot = Path.Combine(localData, "Reze-Nova-TelegramProxy");
        }

        public string CreateRunDirectory()
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                GrantDirectoryAccess(security, identity.User);
            }
            GrantDirectoryAccess(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
            DirectoryInfo profile = Directory.CreateDirectory(profileRoot, security);
            RejectReparsePoint(profile.FullName);
            profile.SetAccessControl(security);
            return Directory.CreateDirectory(Path.Combine(profileRoot, "run-" + Guid.NewGuid().ToString("N")), security).FullName;
        }

        public string LoadOrCreateSecret()
        {
            string path = Path.Combine(profileRoot, "secret.bin");
            if (!File.Exists(path))
            {
                byte[] generated = new byte[16];
                using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                    random.GetBytes(generated);

                string staged = Path.Combine(profileRoot, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(generated, 0, generated.Length);
                        stream.Flush(true);
                    }
                    try { File.Move(staged, path); }
                    catch (IOException) { if (!File.Exists(path)) throw; }
                }
                finally
                {
                    Array.Clear(generated, 0, generated.Length);
                    File.Delete(staged);
                }
            }

            RejectReparsePoint(path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var secret = new byte[16];
                if (stream.Length != secret.Length || stream.Read(secret, 0, secret.Length) != secret.Length)
                    throw new IOException("The Nova Telegram proxy secret has an invalid length.");
                string hex = ToHex(secret);
                Array.Clear(secret, 0, secret.Length);
                return hex;
            }
        }

        public FileStream StageVerifiedEngine(string runDirectory, out string engine)
        {
            engine = Path.Combine(runDirectory, EngineName);
            File.Copy(bundledEngine, engine);
            var stream = new FileStream(engine, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                using (SHA256 sha256 = SHA256.Create())
                    if (ToHex(sha256.ComputeHash(stream)) != ExpectedSha256)
                        throw new IOException("The Flowseal Telegram proxy executable failed SHA-256 verification.");
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        public static void WriteConfiguration(string runDirectory, int port, string secret)
        {
            string portable = Directory.CreateDirectory(Path.Combine(runDirectory, "TgWsProxy_data")).FullName;
            string json = "{\"host\":\"127.0.0.1\",\"port\":" + port.ToString(CultureInfo.InvariantCulture)
                + ",\"secret\":\"" + secret + "\",\"check_updates\":false,\"autostart\":false,\"log_max_mb\":0.1}";
            File.WriteAllText(Path.Combine(portable, "config.json"), json, Utf8);
            File.WriteAllBytes(Path.Combine(portable, ".first_run_done_mtproto"), new byte[0]);
            File.WriteAllBytes(Path.Combine(portable, ".ipv6_warned"), new byte[0]);
        }

        private static void GrantDirectoryAccess(DirectorySecurity security, SecurityIdentifier sid)
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }

        private static void RejectReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A Nova Telegram proxy data path is a reparse point.");
        }

        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
