using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Net;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace checkservice
{

    public class RdpSession
    {
        public int SessionId { get; set; }
        public string State { get; set; }
        public string UserName { get; set; }
        public string ClientHostname { get; set; }
        public string ClientIp { get; set; }
        public string ClientMac { get; set; }
        public string ClientHwid { get; set; }
        public string ClientVersion { get; set; }
        public DateTime ConnectionTime { get; set; } = DateTime.Now;
    }

    public enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit
    }

    public enum WTS_INFO_CLASS
    {
        WTSUserName = 5,
        WTSWinStationName = 6,
        WTSDomainName = 7,
        WTSConnectState = 8,
        WTSClientName = 10,
        WTSClientAddress = 14,
        WTSClientHardwareId = 15,
        WTSClientVersion = 64
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WTS_SESSION_INFO
    {
        public Int32 SessionId;
        [MarshalAs(UnmanagedType.LPStr)]
        public String pWinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WTS_CLIENT_ADDRESS
    {
        public Int32 AddressFamily;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] Address;
    }

    public static class WtsApi32
    {
        private const int WTS_CURRENT_SERVER_HANDLE = 0;

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern Int32 WTSEnumerateSessions(
            IntPtr hServer,
            Int32 Reserved,
            Int32 Version,
            ref IntPtr ppSessionInfo,
            ref Int32 pCount);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern void WTSFreeMemory(IntPtr pMemory);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSQuerySessionInformation(
            IntPtr hServer,
            Int32 SessionId,
            WTS_INFO_CLASS WTSInfoClass,
            out IntPtr ppBuffer,
            out Int32 pBytesReturned);

        public static string QuerySessionString(IntPtr hServer, int sessionId, WTS_INFO_CLASS infoClass)
        {
            IntPtr buffer = IntPtr.Zero;
            int bytesReturned = 0;
            string result = string.Empty;

            if (WTSQuerySessionInformation(hServer, sessionId, infoClass, out buffer, out bytesReturned) && bytesReturned > 0)
            {
                result = Marshal.PtrToStringAnsi(buffer);
                WTSFreeMemory(buffer);
            }
            return result ?? string.Empty;
        }

        public static string QuerySessionIp(IntPtr hServer, int sessionId)
        {
            IntPtr buffer = IntPtr.Zero;
            int bytesReturned = 0;
            string ipAddress = string.Empty;

            if (WTSQuerySessionInformation(hServer, sessionId, WTS_INFO_CLASS.WTSClientAddress, out buffer, out bytesReturned) && bytesReturned > 0)
            {
                var clientAddress = (WTS_CLIENT_ADDRESS)Marshal.PtrToStructure(buffer, typeof(WTS_CLIENT_ADDRESS));

                if (clientAddress.AddressFamily == 2)
                {
                    byte[] ipBytes = new byte[4];
                    Array.Copy(clientAddress.Address, 2, ipBytes, 0, 4);
                    ipAddress = new IPAddress(ipBytes).ToString();
                }

                WTSFreeMemory(buffer);
            }
            return ipAddress;
        }

        public static string QuerySessionMacAddress(IntPtr hServer, int sessionId) => string.Empty;
        public static string QuerySessionHardwareId(IntPtr hServer, int sessionId) => string.Empty;
    }


    public class RdpSessionTracker
    {
        private readonly ILogger<RdpSessionTracker> _logger;
        private readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;
        private Dictionary<string, RdpSession> _lastActiveSessions = new Dictionary<string, RdpSession>();

        public RdpSessionTracker(ILogger<RdpSessionTracker> logger)
        {
            _logger = logger;
        }

        private string GetSessionKey(RdpSession session) => $"{session.SessionId}-{session.UserName}";

        public (List<RdpSession> newSessions, List<RdpSession> disconnectedSessions) CheckForSessionChanges()
        {
            var currentSessions = GetActiveRdpSessions();
            var newSessions = new List<RdpSession>();
            var disconnectedSessions = new List<RdpSession>();
            var currentActiveSessionsMap = new Dictionary<string, RdpSession>();

            foreach (var session in currentSessions)
            {
                var key = GetSessionKey(session);
                currentActiveSessionsMap[key] = session;

                if (!_lastActiveSessions.ContainsKey(key))
                {
                    newSessions.Add(session);
                }
            }


            foreach (var key in _lastActiveSessions.Keys)
            {

                if (!currentActiveSessionsMap.ContainsKey(key))
                {
                    disconnectedSessions.Add(_lastActiveSessions[key]);
                }
            }


            _lastActiveSessions = currentActiveSessionsMap;


            if (newSessions.Any())
            {
                _logger.LogInformation("Novas sessões RDP detectadas: {Count}", newSessions.Count);
            }
            if (disconnectedSessions.Any())
            {
                _logger.LogInformation("Sessões RDP desconectadas (logoff): {Count}", disconnectedSessions.Count);
            }

            return (newSessions, disconnectedSessions);
        }

        public List<RdpSession> GetActiveRdpSessions()
        {
            IntPtr sessionInfoPtr = IntPtr.Zero;
            Int32 sessionCount = 0;
            List<RdpSession> activeSessions = new List<RdpSession>();

            try
            {
                // 1. Enumerar Sessões
                if (WtsApi32.WTSEnumerateSessions(WTS_CURRENT_SERVER_HANDLE, 0, 1, ref sessionInfoPtr, ref sessionCount) == 0)
                {
                    _logger.LogError("Erro ao enumerar sessões RDP. Verifique as permissões (executar como Administrador).");
                    return activeSessions;
                }

                Int64 current = sessionInfoPtr.ToInt64();

                for (int i = 0; i < sessionCount; i++)
                {
                    var session = (WTS_SESSION_INFO)Marshal.PtrToStructure(new IntPtr(current), typeof(WTS_SESSION_INFO));
                    current += Marshal.SizeOf(typeof(WTS_SESSION_INFO));


                    if (session.State == WTS_CONNECTSTATE_CLASS.WTSActive || session.State == WTS_CONNECTSTATE_CLASS.WTSConnected)
                    {
                        string userName = WtsApi32.QuerySessionString(WTS_CURRENT_SERVER_HANDLE, session.SessionId, WTS_INFO_CLASS.WTSUserName);
                        string clientName = WtsApi32.QuerySessionString(WTS_CURRENT_SERVER_HANDLE, session.SessionId, WTS_INFO_CLASS.WTSClientName);


                        if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(clientName))
                        {
                            activeSessions.Add(new RdpSession
                            {
                                SessionId = session.SessionId,
                                State = session.State.ToString().Replace("WTS", ""),
                                UserName = userName,
                                ClientHostname = clientName,
                                ClientIp = WtsApi32.QuerySessionIp(WTS_CURRENT_SERVER_HANDLE, session.SessionId),
                                ClientMac = WtsApi32.QuerySessionMacAddress(WTS_CURRENT_SERVER_HANDLE, session.SessionId),
                                ClientHwid = WtsApi32.QuerySessionHardwareId(WTS_CURRENT_SERVER_HANDLE, session.SessionId),
                                ClientVersion = WtsApi32.QuerySessionString(WTS_CURRENT_SERVER_HANDLE, session.SessionId, WTS_INFO_CLASS.WTSClientVersion)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro inesperado durante o rastreamento de sessões RDP.");
            }
            finally
            {
                if (sessionInfoPtr != IntPtr.Zero)
                {
                    WtsApi32.WTSFreeMemory(sessionInfoPtr);
                }
            }

            return activeSessions;
        }
    }
}
