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
using YFinance.NET.Protocol.Errors;

namespace YFinance.NET.Protocol.Messages;

public sealed record ProtocolResponse<TPayload> : ProtocolEnvelope
{
    public ProtocolResponse()
    {
        MessageType = Constants.ProtocolMessageTypes.Response;
    }

    public string RequestId { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string Status { get; init; } = Constants.ProtocolResponseStatuses.Ok;
    public TPayload? Payload { get; init; }
    public ProtocolError? Error { get; init; }
}
