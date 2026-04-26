using System.Globalization;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;

namespace PortfolioSaver.Render.Converters;

public sealed class SevenSegmentDigitConverter : IValueConverter
{
    private static readonly bool SupportsSegmentDigits = DetectSegmentDigitSupport();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string input = value?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        if (!SupportsSegmentDigits)
            return input;

        StringBuilder builder = new(input.Length * 2);
        foreach (char c in input)
        {
            if (c is >= '0' and <= '9')
            {
                int digit = c - '0';
                builder.Append(char.ConvertFromUtf32(0x1FBF0 + digit));
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static bool DetectSegmentDigitSupport()
    {
        try
        {
            const int probeCodePoint = 0x1FBF0;
            foreach (FontFamily family in Fonts.SystemFontFamilies)
            {
                if (!family.GetTypefaces().Any(typeface => typeface.TryGetGlyphTypeface(out GlyphTypeface glyph) &&
                                                          glyph.CharacterToGlyphMap.ContainsKey(probeCodePoint)))
                {
                    continue;
                }

                return true;
            }
        }
        catch
        {
        }

        return false;
    }
}
