using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RezeNova.Core
{
    internal sealed class NovaLink
    {
        public string Name;
        public string Address;

        public NovaLink(string name, string address)
        {
            Name = name;
            Address = address;
        }
    }

    internal sealed class NovaSettings
    {
        public NovaMode Mode = NovaMode.Auto;
        public bool StartWithWindows;
        public bool StartRouteOnLaunch;
        public bool StartMinimized;
        public readonly List<NovaLink> CustomLinks = new List<NovaLink>();

        public static NovaSettings Load()
        {
            var settings = new NovaSettings();
            string path = FilePath();
            if (!File.Exists(path)) return settings;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                int separator = line.IndexOf('=');
                if (separator < 1) continue;
                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (key.Equals("mode", StringComparison.OrdinalIgnoreCase))
                {
                    NovaMode parsed;
                    if (Enum.TryParse<NovaMode>(value, true, out parsed)) settings.Mode = parsed;
                }
                else if (key.Equals("start_with_windows", StringComparison.OrdinalIgnoreCase)) settings.StartWithWindows = IsTrue(value);
                else if (key.Equals("start_route", StringComparison.OrdinalIgnoreCase)) settings.StartRouteOnLaunch = IsTrue(value);
                else if (key.Equals("start_minimized", StringComparison.OrdinalIgnoreCase)) settings.StartMinimized = IsTrue(value);
                else if (key.Equals("custom_link", StringComparison.OrdinalIgnoreCase))
                {
                    NovaLink link = DecodeLink(value);
                    if (link != null) settings.CustomLinks.Add(link);
                }
            }
            return settings;
        }

        public void Save()
        {
            string path = FilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = new List<string>
            {
                "mode=" + Mode,
                "start_with_windows=" + StartWithWindows,
                "start_route=" + StartRouteOnLaunch,
                "start_minimized=" + StartMinimized
            };
            foreach (NovaLink link in CustomLinks)
                lines.Add("custom_link=" + Encode(link.Name) + "." + Encode(link.Address));
            File.WriteAllText(path, String.Join(Environment.NewLine, lines) + Environment.NewLine);
        }

        private static string FilePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RezeNova", "settings.ini");
        }

        private static bool IsTrue(string value)
        {
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? String.Empty));
        }

        private static NovaLink DecodeLink(string value)
        {
            try
            {
                int separator = value.IndexOf('.');
                if (separator < 1) return null;
                string name = Encoding.UTF8.GetString(Convert.FromBase64String(value.Substring(0, separator)));
                string address = Encoding.UTF8.GetString(Convert.FromBase64String(value.Substring(separator + 1)));
                if (String.IsNullOrWhiteSpace(name) || String.IsNullOrWhiteSpace(address)) return null;
                return new NovaLink(name, address);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
