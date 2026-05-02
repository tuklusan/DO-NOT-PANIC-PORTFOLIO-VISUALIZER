using System.Net;
using System.Net.Sockets;

namespace PortfolioSaver.Screensaver.Services;

public sealed class NtpTimeService
{
    private static readonly string[] Hosts = ["pool.ntp.org", "0.pool.ntp.org", "1.pool.ntp.org"];

    public async Task<NtpSyncResult> TryGetUtcNowAsync(CancellationToken cancellationToken = default)
    {
        foreach (string host in Hosts)
        {
            try
            {
                DateTimeOffset utcNow = await QueryHostAsync(host, cancellationToken);
                return new NtpSyncResult
                {
                    Success = true,
                    Source = host,
                    UtcNow = utcNow
                };
            }
            catch
            {
            }
        }

        return new NtpSyncResult
        {
            Success = false,
            Source = "Local clock",
            UtcNow = DateTimeOffset.UtcNow
        };
    }

    private static async Task<DateTimeOffset> QueryHostAsync(string host, CancellationToken cancellationToken)
    {
        using UdpClient udpClient = new();
        udpClient.Client.ReceiveTimeout = 3000;
        udpClient.Client.SendTimeout = 3000;

        IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        IPEndPoint? endpoint = addresses
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => new IPEndPoint(address, 123))
            .FirstOrDefault();

        if (endpoint is null)
            throw new InvalidOperationException($"Could not resolve an IPv4 endpoint for {host}.");

        byte[] request = new byte[48];
        request[0] = 0x1B;
        await udpClient.SendAsync(request, endpoint, cancellationToken);

        UdpReceiveResult response = await udpClient.ReceiveAsync(cancellationToken);
        if (response.Buffer.Length < 48)
            throw new InvalidOperationException($"NTP response from {host} was too short.");

        const byte offsetTransmitTime = 40;
        ulong seconds = ((ulong)response.Buffer[offsetTransmitTime] << 24)
            | ((ulong)response.Buffer[offsetTransmitTime + 1] << 16)
            | ((ulong)response.Buffer[offsetTransmitTime + 2] << 8)
            | response.Buffer[offsetTransmitTime + 3];
        ulong fraction = ((ulong)response.Buffer[offsetTransmitTime + 4] << 24)
            | ((ulong)response.Buffer[offsetTransmitTime + 5] << 16)
            | ((ulong)response.Buffer[offsetTransmitTime + 6] << 8)
            | response.Buffer[offsetTransmitTime + 7];

        DateTime epoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        double milliseconds = (seconds * 1000d) + ((fraction * 1000d) / 0x100000000L);
        return new DateTimeOffset(epoch.AddMilliseconds(milliseconds));
    }
}

public sealed class NtpSyncResult
{
    public bool Success { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset UtcNow { get; set; }
}
