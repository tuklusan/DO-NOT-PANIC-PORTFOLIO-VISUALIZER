using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Win32;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Shared.Diagnostics;

public static class TraceLog
{
    private const int MaxTraceBytes = 4 * 1024 * 1024;
    private const int MaxLineLength = 1900;
    private const int MaxFieldValueLength = 280;
    private static readonly object FileSync = new();
    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly string ProgramName = Process.GetCurrentProcess().ProcessName;
    private static NetworkMetadata _networkMetadata = new(Environment.MachineName, "127.0.0.1");
    private static int _workerStarted;
    private static int _metadataResolutionStarted;

    private static string TraceDirectory
        => Path.Combine(PathHelper.GetAppDataDirectory(), "Trace");

    private static string CircularTracePath
        => Path.Combine(TraceDirectory, "trace.circular.log");

    private static string CircularIndexPath
        => Path.Combine(TraceDirectory, "trace.circular.idx");

    public static void Info(string source, string message, [CallerMemberName] string functionName = "")
        => Enqueue("INFO", source, message, null, functionName);

    public static void InfoState(
        string source,
        string eventName,
        IEnumerable<KeyValuePair<string, object?>> fields,
        [CallerMemberName] string functionName = "")
        => Enqueue("INFO", source, BuildStructuredMessage(eventName, fields), null, functionName);

    public static void Warn(string source, string message, [CallerMemberName] string functionName = "")
        => Enqueue("WARN", source, message, null, functionName);

    public static void WarnState(
        string source,
        string eventName,
        IEnumerable<KeyValuePair<string, object?>> fields,
        [CallerMemberName] string functionName = "")
        => Enqueue("WARN", source, BuildStructuredMessage(eventName, fields), null, functionName);

    public static void Error(string source, string message, Exception? exception = null, [CallerMemberName] string functionName = "")
        => Enqueue("ERROR", source, message, exception, functionName);

    public static void ErrorState(
        string source,
        string eventName,
        IEnumerable<KeyValuePair<string, object?>> fields,
        Exception? exception = null,
        [CallerMemberName] string functionName = "")
        => Enqueue("ERROR", source, BuildStructuredMessage(eventName, fields), exception, functionName);

    public static bool ShouldForceSoftwareRendering()
    {
        string? explicitOverride = Environment.GetEnvironmentVariable("PORTFOLIO_SAVER_FORCE_SOFTWARE_RENDER");
        if (string.Equals(explicitOverride, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(explicitOverride, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            string manufacturer = biosKey?.GetValue("SystemManufacturer")?.ToString() ?? string.Empty;
            string product = biosKey?.GetValue("SystemProductName")?.ToString() ?? string.Empty;
            string fingerprint = $"{manufacturer} {product}".ToLowerInvariant();

            return fingerprint.Contains("virtualbox", StringComparison.Ordinal) ||
                   fingerprint.Contains("innotek", StringComparison.Ordinal) ||
                   fingerprint.Contains("vmware", StringComparison.Ordinal) ||
                   fingerprint.Contains("qemu", StringComparison.Ordinal) ||
                   fingerprint.Contains("kvm", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void Enqueue(string level, string source, string message, Exception? exception, string functionName)
    {
        EnsureWorker();
        EnsureNetworkMetadataResolution();
        string exceptionText = exception is null ? string.Empty : $" | ex={exception.GetType().Name}: {exception.Message}";
        string functionText = string.IsNullOrWhiteSpace(functionName) ? "unknown" : functionName;
        NetworkMetadata metadata = Volatile.Read(ref _networkMetadata);
        string line = $"{DateTimeOffset.UtcNow:O} | {level} | program={ProgramName} | source={source} | function={functionText} | host={metadata.HostName} | ip={metadata.LocalIp} | pid={Environment.ProcessId} | tid={Environment.CurrentManagedThreadId} | {SanitizeValue(message, MaxLineLength)}{SanitizeValue(exceptionText, 240)}";
        Queue.Enqueue(line);
    }

    private static string BuildStructuredMessage(string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
    {
        StringBuilder builder = new();
        builder.Append("event=");
        builder.Append(SanitizeValue(eventName, 80));

        foreach (KeyValuePair<string, object?> field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
                continue;

            builder.Append(" | ");
            string sanitizedKey = SanitizeKey(field.Key);
            builder.Append(sanitizedKey);
            builder.Append('=');
            builder.Append(SensitiveDataRedactor.IsSensitiveKey(sanitizedKey) ? SensitiveDataRedactor.RedactedValue : SanitizeValue(FormatFieldValue(field.Value), MaxFieldValueLength));
        }

        return builder.ToString();
    }

    private static string SanitizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "field";

        return value
            .Trim()
            .Replace(' ', '_')
            .Replace('|', '/')
            .Replace('\r', '_')
            .Replace('\n', '_');
    }

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

    private static void EnsureNetworkMetadataResolution()
    {
        if (Interlocked.CompareExchange(ref _metadataResolutionStarted, 1, 0) != 0)
            return;

        _ = Task.Run(ResolveNetworkMetadata);
    }

    private static void ResolveNetworkMetadata()
    {
        string hostName = GetHostNameSafe();
        string localIp = GetPrimaryIpSafe(hostName);
        Volatile.Write(ref _networkMetadata, new NetworkMetadata(hostName, localIp));
    }

    private sealed record NetworkMetadata(string HostName, string LocalIp);

    private static async Task ProcessQueueAsync()
    {
        while (true)
        {
            if (!Queue.TryDequeue(out string? line))
            {
                await Task.Delay(25).ConfigureAwait(false);
                continue;
            }

            try
            {
                WriteCircular(line);
            }
            catch
            {
            }
        }
    }

    private static void WriteCircular(string line)
    {
        byte[] payload = Encoding.UTF8.GetBytes(line + Environment.NewLine);
        if (payload.Length > MaxTraceBytes)
            payload = payload[^MaxTraceBytes..];

        lock (FileSync)
        {
            Directory.CreateDirectory(TraceDirectory);

            using FileStream stream = new(
                CircularTracePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite);

            if (stream.Length != MaxTraceBytes)
                stream.SetLength(MaxTraceBytes);

            int writePosition = ReadPosition();
            if (writePosition < 0 || writePosition >= MaxTraceBytes)
                writePosition = 0;

            int firstChunkLength = Math.Min(payload.Length, MaxTraceBytes - writePosition);
            stream.Position = writePosition;
            stream.Write(payload, 0, firstChunkLength);

            int remaining = payload.Length - firstChunkLength;
            if (remaining > 0)
            {
                stream.Position = 0;
                stream.Write(payload, firstChunkLength, remaining);
            }

            int nextPosition = (writePosition + payload.Length) % MaxTraceBytes;
            WritePosition(nextPosition);
            stream.Flush(true);
        }
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
            File.WriteAllText(CircularIndexPath, position.ToString());
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

    private static string GetPrimaryIpSafe(string hostName)
    {
        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(hostName);
            IPAddress? ip = addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address));
            return ip?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
