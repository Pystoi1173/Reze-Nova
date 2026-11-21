using System;
using Microsoft.Win32;

namespace RezeNova.Core
{
    internal static class WindowsStartup
    {
        private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string ValueName = "RezeNova";

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                return key != null && key.GetValue(ValueName) != null;
        }

        public static void SetEnabled(bool enabled, string executablePath)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(ValueName, "\"" + executablePath + "\" ui", RegistryValueKind.String);
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}
