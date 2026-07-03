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

namespace YFinance.NET.Server.Hosting;

public sealed record ServerOptions(
    int Port,
    IPAddress BindAddress,
    bool OwnedMode,
    int? OwnerProcessId,
    int MaxConcurrentClients,
    int MaxConcurrentRequestsPerClient,
    bool EnableUpstreamSyncCheck)
{
    public static ServerOptions Parse(string[] args)
    {
        int port = Protocol.Constants.ProtocolConstants.DefaultPort;
        IPAddress bindAddress = IPAddress.Loopback;
        bool bindAddressSpecified = false;
        bool ownedMode = false;
        int? ownerPid = null;
        int maxClients = Protocol.Constants.ProtocolConstants.MaxConcurrentClients;
        int maxConcurrentRequestsPerClient = 8;
        bool enableUpstreamSyncCheck = true;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedPort):
                    port = parsedPort;
                    i++;
                    break;
                case "--bind-address":
                    if (i + 1 >= args.Length || !IPAddress.TryParse(args[i + 1], out IPAddress? parsedBindAddress))
                        throw new ArgumentException("--bind-address requires a valid IP address.");

                    bindAddress = parsedBindAddress;
                    bindAddressSpecified = true;
                    i++;
                    break;
                case "--allow-remote":
                    if (!bindAddressSpecified)
                        bindAddress = IPAddress.Any;
                    break;
                case "--owned":
                    ownedMode = true;
                    break;
                case "--owner-pid" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedPid):
                    ownerPid = parsedPid;
                    i++;
                    break;
                case "--max-clients" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedClients):
                    maxClients = parsedClients;
                    i++;
                    break;
                case "--max-requests-per-client" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedRequests):
                    maxConcurrentRequestsPerClient = Math.Max(1, parsedRequests);
                    i++;
                    break;
                case "--no-upstream-sync":
                    enableUpstreamSyncCheck = false;
                    break;
            }
        }

        if (ownedMode && !IPAddress.IsLoopback(bindAddress))
            throw new ArgumentException("Owned mode requires a loopback bind address.");

        return new ServerOptions(port, bindAddress, ownedMode, ownerPid, maxClients, maxConcurrentRequestsPerClient, enableUpstreamSyncCheck);
    }
}
