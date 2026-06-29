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
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PortfolioSaver.Shared.Diagnostics;
using YFinance.NET.Storage;

namespace YFinance.NET.Diagnostics;

public sealed class YFinanceCircularTraceSink : IYFinanceTraceSink
{
    private const int MaxLineLength = 1900;
    private const int MaxFieldValueLength = 280;
    private const int MaxTraceBatchLines = 512;
    private const int TraceIndexCheckpointLines = 64;
    private static readonly object FileSync = new();
    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly string ProgramName = Process.GetCurrentProcess().ProcessName;
    private static readonly string HostName = GetHostNameSafe();
    private static readonly string LocalIp = GetPrimaryIpSafe();
    private static readonly Lazy<YFinanceCircularTraceSink> LazyInstance = new(
        static () => new YFinanceCircularTraceSink(),
        LazyThreadSafetyMode.ExecutionAndPublication);
    private static int _circularWritePosition = -1;
    private static int _workerStarted;
    private static int _maxTraceBytes;

    public static YFinanceCircularTraceSink Instance => LazyInstance.Value;

    private YFinanceCircularTraceSink()
    {
    }

    internal static void ResetCircularStateForTests()
    {
        lock (FileSync)
        {
            while (Queue.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref _maxTraceBytes, 0);
            _circularWritePosition = -1;
        }
    }

    public void InfoState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
        => Enqueue("INFO", source, BuildStructuredMessage(eventName, fields), null);

    public void WarnState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
        => Enqueue("WARN", source, BuildStructuredMessage(eventName, fields), null);

    public void ErrorState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields, Exception? exception = null)
        => Enqueue("ERROR", source, BuildStructuredMessage(eventName, fields), exception);

    private static string TraceDirectory
        => Path.Combine(GetAppDataDirectory(), "Trace");

    private static string CircularTracePath
        => Path.Combine(TraceDirectory, "yfinance.circular.log");

    private static string CircularIndexPath
        => Path.Combine(TraceDirectory, "yfinance.circular.idx");

    private static string GetAppDataDirectory()
        => AppDataRootResolver.ResolveInstalledLocalDataRoot();

    private static void Enqueue(string level, string source, string message, Exception? exception)
    {
        EnsureWorker();
        string exceptionText = exception is null ? string.Empty : $" | ex={exception.GetType().Name}: {exception.Message}";
        string line = $"{DateTimeOffset.UtcNow:O} | {level} | program={ProgramName} | source={source} | host={HostName} | ip={LocalIp} | pid={Environment.ProcessId} | tid={Environment.CurrentManagedThreadId} | {SanitizeValue(message, MaxLineLength)}{SanitizeValue(exceptionText, 240)}";
        Queue.Enqueue(line);
    }

    private static string BuildStructuredMessage(string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
    {
        StringBuilder builder = new();
        builder.Append("event=");
        builder.Append(SanitizeValue(eventName, 80));

        foreach ((string key, object? value) in fields.Select(static pair => (pair.Key, pair.Value)))
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            builder.Append(" | ");
            string sanitizedKey = SanitizeKey(key);
            builder.Append(sanitizedKey);
            builder.Append('=');
            builder.Append(SensitiveDataRedactor.IsSensitiveKey(sanitizedKey) ? SensitiveDataRedactor.RedactedValue : SanitizeValue(FormatFieldValue(value), MaxFieldValueLength));
        }

        return builder.ToString();
    }

    private static string SanitizeKey(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "field"
            : value.Trim().Replace(' ', '_').Replace('|', '/').Replace('\r', '_').Replace('\n', '_');

    private static string SanitizeValue(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string sanitized = value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace('|', '/')
            .Trim();
        sanitized = SensitiveDataRedactor.RedactSensitivePatterns(sanitized);

        if (sanitized.Length <= maxLength)
            return sanitized;

        return sanitized[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static string FormatFieldValue(object? value)
    {
        if (value is null)
            return "<null>";

        return value switch
        {
            string text => text,
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString(),
            bool flag => flag ? "true" : "false",
            Enum => Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString() ?? string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? value.ToString() ?? string.Empty,
            IEnumerable sequence when value is not string => FormatEnumerable(sequence),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string FormatEnumerable(IEnumerable sequence)
    {
        List<string> items = [];
        int totalCount = 0;
        foreach (object? item in sequence)
        {
            totalCount++;
            if (items.Count < 8)
                items.Add(SanitizeValue(FormatFieldValue(item), 48));
        }

        if (totalCount == 0)
            return "[]";

        string suffix = totalCount > items.Count ? $", ... ({totalCount} total)" : string.Empty;
        return $"[{string.Join(", ", items)}{suffix}]";
    }

    private static void EnsureWorker()
    {
        if (Interlocked.CompareExchange(ref _workerStarted, 1, 0) != 0)
            return;

        _ = Task.Run(ProcessQueueAsync);
    }

    private static async Task ProcessQueueAsync()
    {
        while (true)
        {
            try
            {
                if (!Queue.TryDequeue(out string? line))
                {
                    await Task.Delay(25).ConfigureAwait(false);
                    continue;
                }

                List<string> lines = [line];
                while (lines.Count < MaxTraceBatchLines && Queue.TryDequeue(out string? nextLine))
                    lines.Add(nextLine);

                WriteCircularBatch(lines);
            }
            catch
            {
                await Task.Delay(250).ConfigureAwait(false);
            }
        }
    }

    private static void WriteCircular(string line)
        => WriteCircularBatch([line]);

    private static void WriteCircularBatch(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
            return;

        int maxTraceBytes = Math.Max(
            CircularTraceSettings.MinimumMaxTraceMegabytes * 1024 * 1024,
            GetMaxTraceBytes());

        lock (FileSync)
        {
            Directory.CreateDirectory(TraceDirectory);

            using FileStream stream = new(
                CircularTracePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite);

            if (stream.Length != maxTraceBytes)
                stream.SetLength(maxTraceBytes);

            int writePosition = _circularWritePosition;
            if (writePosition < 0)
                writePosition = ReadPosition();
            if (writePosition < 0 || writePosition >= maxTraceBytes)
                writePosition = 0;

            int nextPosition = writePosition;
            int linesWritten = 0;
            foreach (string line in lines)
            {
                byte[] payload = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                if (payload.Length > maxTraceBytes)
                    payload = payload[^maxTraceBytes..];

                int firstChunkLength = Math.Min(payload.Length, maxTraceBytes - nextPosition);
                stream.Position = nextPosition;
                stream.Write(payload, 0, firstChunkLength);

                int remaining = payload.Length - firstChunkLength;
                if (remaining > 0)
                {
                    stream.Position = 0;
                    stream.Write(payload, firstChunkLength, remaining);
                }

                nextPosition = (nextPosition + payload.Length) % maxTraceBytes;
                linesWritten++;
                // Bound crash recovery loss to 63 trace lines while preserving
                // enough write batching to keep live diagnostic traces current.
                if (linesWritten % TraceIndexCheckpointLines == 0)
                    WritePosition(nextPosition);
            }

            _circularWritePosition = nextPosition;
            WritePosition(nextPosition);
            // Dispose/Flush commits the batch to the OS. Avoid Flush(true) here:
            // per-line disk fsync caused trace lag during 30-minute VM soaks,
            // and the index is intentionally checkpointed during the batch and at the end.
            stream.Flush();
        }
    }

    private static int GetMaxTraceBytes()
    {
        return CircularTraceSettings.ResolveCachedMaxTraceBytes(ref _maxTraceBytes);
    }

    private static int ReadPosition()
    {
        try
        {
            if (!File.Exists(CircularIndexPath))
                return 0;

            string raw = File.ReadAllText(CircularIndexPath).Trim();
            return int.TryParse(raw, out int position) ? position : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void WritePosition(int position)
    {
        try
        {
            File.WriteAllText(CircularIndexPath, position.ToString(CultureInfo.InvariantCulture));
        }
        catch
        {
        }
    }

    private static string GetHostNameSafe()
    {
        try
        {
            return Dns.GetHostName();
        }
        catch
        {
            return Environment.MachineName;
        }
    }

    private static string GetPrimaryIpSafe()
    {
        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(Dns.GetHostName());
            IPAddress? ip = addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address));
            return ip?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
