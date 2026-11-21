using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RezeNova.Proxy
{
    internal static class TelegramProxyListener
    {
        public static bool TryInspect(OwnedWindowsProcess process, int port, out int clients)
        {
            clients = 0;
            bool ownedListener = false;
            bool conflict = false;
            InspectTable(process, port, false, ref ownedListener, ref conflict, ref clients);
            InspectTable(process, port, true, ref ownedListener, ref conflict, ref clients);
            if (conflict) throw new InvalidOperationException("The Nova Telegram proxy listener failed its loopback or ownership check.");
            return ownedListener;
        }

        private static void InspectTable(OwnedWindowsProcess process, int port, bool ipv6,
            ref bool ownedListener, ref bool conflict, ref int clients)
        {
            const int AfInet = 2, AfInet6 = 23;
            const int OwnerPidAll = 5;
            const int ErrorInsufficientBuffer = 122, ErrorNotSupported = 50;
            const int Listen = 2, Established = 5;
            int addressFamily = ipv6 ? AfInet6 : AfInet;
            int rowSize = ipv6 ? 56 : 24; // MIB_TCP6ROW_OWNER_PID / MIB_TCPROW_OWNER_PID.
            int size = 0;
            uint error = GetExtendedTcpTable(IntPtr.Zero, ref size, false, addressFamily, OwnerPidAll, 0);
            if (ipv6 && error == ErrorNotSupported) return;
            if (error != ErrorInsufficientBuffer) throw new Win32Exception((int)error);

            for (int attempt = 0; attempt < 4; attempt++)
            {
                int capacity = size;
                IntPtr table = Marshal.AllocHGlobal(capacity);
                try
                {
                    error = GetExtendedTcpTable(table, ref size, false, addressFamily, OwnerPidAll, 0);
                    if (error == ErrorInsufficientBuffer) continue;
                    if (error != 0) throw new Win32Exception((int)error);
                    int count = Marshal.ReadInt32(table);
                    if (count < 0 || count > (capacity - sizeof(int)) / rowSize)
                        throw new InvalidOperationException("Windows returned an invalid TCP table.");

                    for (int i = 0; i < count; i++)
                    {
                        IntPtr row = IntPtr.Add(table, sizeof(int) + i * rowSize);
                        int state = Marshal.ReadInt32(row, ipv6 ? 48 : 0);
                        if (state != Listen && state != Established) continue;
                        int portOffset = ipv6 ? 20 : 8;
                        int localPort = (Marshal.ReadByte(row, portOffset) << 8) | Marshal.ReadByte(row, portOffset + 1);
                        if (state == Established && (ipv6 || localPort != port)) continue;
                        uint ownerPid = unchecked((uint)Marshal.ReadInt32(row, ipv6 ? 52 : 20));
                        bool expectedAddress = !ipv6 && Marshal.ReadInt32(row, 4) == 0x0100007f;

                        if (state == Listen)
                        {
                            // The bundled proxy should have a single listener, on the exact IPv4 address configured by Nova.
                            if (process.OwnsProcess(ownerPid))
                            {
                                if (expectedAddress && localPort == port) ownedListener = true;
                                else conflict = true;
                            }
                            else if (localPort == port && !ipv6 && (expectedAddress || Marshal.ReadInt32(row, 4) == 0))
                            {
                                conflict = true;
                            }
                        }
                        else if (expectedAddress && Marshal.ReadByte(row, 12) == 127 && process.OwnsProcess(ownerPid))
                        {
                            clients++;
                        }
                    }
                    return;
                }
                finally { Marshal.FreeHGlobal(table); }
            }
            throw new Win32Exception((int)error);
        }

        [DllImport("iphlpapi.dll")]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort,
            int addressFamily, int tableClass, uint reserved);
    }
}
