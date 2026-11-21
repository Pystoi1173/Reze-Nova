using System;
using System.Threading;

namespace RezeNova.Proxy
{
    internal enum TelegramProxyState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Failed
    }

    internal sealed class TelegramProxyStatus
    {
        public TelegramProxyState State { get; private set; }
        public int Port { get; private set; }
        public int ClientCount { get; private set; }
        public bool DesktopVerified { get; private set; }
        public string LastError { get; private set; }

        public TelegramProxyStatus(TelegramProxyState state, int port, int clientCount, bool desktopVerified, string lastError)
        {
            State = state;
            Port = port;
            ClientCount = clientCount;
            DesktopVerified = desktopVerified;
            LastError = lastError;
        }
    }

    internal interface ITelegramProxyService : IDisposable
    {
        TelegramProxyStatus Status { get; }
        // Contains the proxy secret. Request it only for an explicit connection action.
        Uri ConnectionUri { get; }
        void Start(CancellationToken cancellationToken);
        void Stop();
    }
}
