// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VIEWER
// This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
// personal, educational, or hobbyist use only. Commercial exploitation,
// corporate internal operations, or AI model training are strictly forbidden.
//
// ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
// which is licensed under the Apache License, Version 2.0. A copy of the Apache
// License is provided within the distribution environment.
//
// FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
// It does not provide financial, investment, legal, or tax advice. All data
// calculation and scraping outputs are provided 'AS IS' with zero guarantee
// of real-time accuracy or upstream availability.
//
// This file is subject to the terms and conditions defined in the LICENSE
// file located in the root directory of this source code repository.
// Removal or modification of this legal notice constitutes copyright infringement.
// ============================================================================
using System.Net;

namespace YFinance.NET.Server.Hosting;

public sealed record ServerOptions(
    int Port,
    IPAddress BindAddress,
    bool OwnedMode,
    int? OwnerProcessId,
    int MaxConcurrentClients,
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
                case "--no-upstream-sync":
                    enableUpstreamSyncCheck = false;
                    break;
            }
        }

        if (ownedMode && !IPAddress.IsLoopback(bindAddress))
            throw new ArgumentException("Owned mode requires a loopback bind address.");

        return new ServerOptions(port, bindAddress, ownedMode, ownerPid, maxClients, enableUpstreamSyncCheck);
    }
}
