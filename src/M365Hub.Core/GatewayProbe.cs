// Raw-socket probe for 127.0.0.1:8230. Deliberately NOT HttpClient:
// HttpClient honours the system proxy, so on a machine with proxy
// auto-detect the first request can stall past the 2s timeout and a
// healthy-but-slow first request looked exactly like a hung gateway
// (1.0.4 app-launch regression: probe said Hung when nothing was
// listening at all). A TCP connect either succeeds or is refused
// immediately, and only a connection that stays open without sending
// a single byte counts as hung.
using System.Net.Sockets;

namespace M365Hub.Core;

public enum GatewayProbeState { None, Live, Hung }

public static class GatewayProbe
{
    /// <summary>Probe host:port. None = connection refused (nothing
    /// listening, or the connect itself never completed); Live =
    /// something answered HTTP; Hung = TCP accepted but no response
    /// byte within the timeout (or the peer closed without answering).</summary>
    public static GatewayProbeState Probe(string host, int port, TimeSpan timeout)
    {
        using var client = new TcpClient();
        try
        {
            var connect = client.ConnectAsync(host, port);
            if (!connect.Wait(timeout)) return GatewayProbeState.None;
            if (!client.Connected) return GatewayProbeState.None;
        }
        catch
        {
            // Connection refused etc: nothing listening.
            return GatewayProbeState.None;
        }

        try
        {
            using var stream = client.GetStream();
            stream.WriteTimeout = (int)timeout.TotalMilliseconds;
            stream.ReadTimeout = (int)timeout.TotalMilliseconds;
            byte[] req = System.Text.Encoding.ASCII.GetBytes(
                "GET /healthz HTTP/1.0\r\nHost: " + host + "\r\n\r\n");
            stream.Write(req, 0, req.Length);
            var buf = new byte[1];
            int n = stream.Read(buf, 0, 1);
            return n > 0 ? GatewayProbeState.Live : GatewayProbeState.Hung;
        }
        catch
        {
            // Read timed out (or the peer reset): accepted but never answered.
            return GatewayProbeState.Hung;
        }
    }
}
