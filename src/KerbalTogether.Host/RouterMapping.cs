using Open.Nat;

namespace KerbalTogether.Host;

public sealed class RouterMapping : IAsyncDisposable
{
    private NatDevice? device;
    private Mapping? mapping;
    private CancellationTokenSource? renew;
    private Task? renewal;

    public async Task<string?> TryOpen(int port, Action<string> log, CancellationToken token)
    {
        foreach (var protocol in new[] { PortMapper.Upnp, PortMapper.Pmp })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                log("Trying " + protocol + " router mapping…");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                var discovered = await new NatDiscoverer().DiscoverDeviceAsync(protocol, deadline);
                var requested = new Mapping(Protocol.Udp, port, port, 300, "Kerbal Together");
                await discovered.CreatePortMapAsync(requested).WaitAsync(TimeSpan.FromSeconds(6), token);
                device = discovered;
                mapping = requested;
                renew = new CancellationTokenSource();
                renewal = Renew(log, renew.Token);
                var ip = await discovered.GetExternalIPAsync().WaitAsync(TimeSpan.FromSeconds(6), token);
                log(protocol + " mapping created (reachability still needs a friend to connect).");
                if (!IsPublicIPv4(ip)) log("Router reports a private/shared address: double NAT or CGNAT may prevent incoming connections.");
                return ip + ":" + port;
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                log(protocol + " unavailable: " + e.Message);
                if (device != null) await DisposeAsync();
            }
        }
        return null;
    }

    public static bool IsPublicIPv4(System.Net.IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4 && b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224 &&
               !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) && !(b[0] == 192 && b[1] == 168) &&
               !(b[0] == 100 && b[1] >= 64 && b[1] <= 127) && !(b[0] == 169 && b[1] == 254);
    }

    private async Task Renew(Action<string> log, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(60), token);
                try { await device!.CreatePortMapAsync(mapping!).WaitAsync(TimeSpan.FromSeconds(6), token); }
                catch (Exception e) when (!token.IsCancellationRequested) { log("Router mapping renewal failed: " + e.Message + ". Direct connections may stop working; restart hosting to retry or use IPv6."); }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        renew?.Cancel();
        if (renewal != null) await renewal;
        if (device != null && mapping != null)
            try { await device.DeletePortMapAsync(mapping).WaitAsync(TimeSpan.FromSeconds(3)); } catch { /* A leased mapping expires after 5 minutes. */ }
        renew?.Dispose();
        renew = null; renewal = null; device = null; mapping = null;
    }
}
