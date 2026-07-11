// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
// ============================================================================
using System.Net;
using System.Net.Sockets;
using PortfolioSaver.Shared.Diagnostics;

namespace PortfolioSaver.Presentation.Services;

public sealed class NtpTimeService
{
    private static readonly string[] Hosts = ["pool.ntp.org", "0.pool.ntp.org", "1.pool.ntp.org"];
    private static readonly TimeSpan PerHostTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(1.5);

    public async Task<NtpSyncResult> TryGetUtcNowAsync(CancellationToken cancellationToken = default)
    {
        int failureCount = 0;
        string? lastFailure = null;
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failureCount++;
                lastFailure = ex.GetType().Name;
            }
        }

        TraceLog.WarnState(
            "NtpTimeService",
            "AllHostsFailed",
            [
                new("host_count", Hosts.Length),
                new("failure_count", failureCount),
                new("last_failure", lastFailure ?? "none")
            ]);
        return new NtpSyncResult
        {
            Success = false,
            Source = "Local clock",
            UtcNow = DateTimeOffset.UtcNow
        };
    }

    private static async Task<DateTimeOffset> QueryHostAsync(string host, CancellationToken cancellationToken)
    {
        using CancellationTokenSource hostTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        hostTimeout.CancelAfter(PerHostTimeout);
        try
        {
            using UdpClient udpClient = new();
            udpClient.Client.ReceiveTimeout = 3000;
            udpClient.Client.SendTimeout = 3000;

            IPAddress[] addresses = await ResolveHostAsync(host, hostTimeout.Token);
            IPEndPoint? endpoint = addresses
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => new IPEndPoint(address, 123))
                .FirstOrDefault();

            if (endpoint is null)
                throw new InvalidOperationException($"Could not resolve an IPv4 endpoint for {host}.");

            byte[] request = new byte[48];
            request[0] = 0x1B;
            await udpClient.SendAsync(request, endpoint, hostTimeout.Token);

            UdpReceiveResult response = await udpClient.ReceiveAsync(hostTimeout.Token);
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TraceLog.InfoState(
                "NtpTimeService",
                "HostTimeout",
                [new("host", host), new("timeout_ms", PerHostTimeout.TotalMilliseconds)]);
            throw new TimeoutException($"NTP lookup timed out for {host}.");
        }
    }

    private static async Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken)
    {
        using CancellationTokenSource dnsTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        dnsTimeout.CancelAfter(DnsTimeout);
        try
        {
            return await Dns.GetHostAddressesAsync(host, dnsTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TraceLog.InfoState(
                "NtpTimeService",
                "DnsTimeout",
                [new("host", host), new("timeout_ms", DnsTimeout.TotalMilliseconds)]);
            throw new TimeoutException($"DNS resolution timed out for {host}.");
        }
    }
}

public sealed class NtpSyncResult
{
    public bool Success { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset UtcNow { get; set; }
}
