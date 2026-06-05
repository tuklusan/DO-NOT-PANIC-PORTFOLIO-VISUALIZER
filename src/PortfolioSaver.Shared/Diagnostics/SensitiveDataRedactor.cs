using System.Text.RegularExpressions;

namespace PortfolioSaver.Shared.Diagnostics;

public static class SensitiveDataRedactor
{
    public const string RedactedValue = "<redacted>";

    private static readonly string[] SensitiveKeyFragments = ["key", "secret", "token", "password", "authorization", "credential"];
    private static readonly Regex SensitiveAssignmentPattern = new(
        @"(?i)\b(api[_-]?key|secret|token|password|authorization|credential)\s*[:=]\s*[^\s\|;]+",
        RegexOptions.Compiled);
    private static readonly Regex BearerPattern = new(
        @"(?i)\bbearer\s+[^\s\|;]+",
        RegexOptions.Compiled);

    public static bool IsSensitiveKey(string key)
        => SensitiveKeyFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public static string RedactSensitivePatterns(string value)
    {
        string redacted = SensitiveAssignmentPattern.Replace(value, match =>
        {
            int separator = match.Value.IndexOfAny([':', '=']);
            return separator < 0 ? RedactedValue : match.Value[..(separator + 1)] + RedactedValue;
        });

        return BearerPattern.Replace(redacted, "Bearer " + RedactedValue);
    }
}
