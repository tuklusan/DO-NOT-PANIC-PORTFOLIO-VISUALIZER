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
