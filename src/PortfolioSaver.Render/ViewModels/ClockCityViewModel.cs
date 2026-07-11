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
using System.Windows;
using System.Windows.Media;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Render.ViewModels;

public sealed class ClockCityViewModel : BindableBase
{
    private static readonly PointCollection EmptyMiniGraphPoints = CreateFrozenPointCollection(Array.Empty<Point>());
    private static readonly Brush DefaultCardBackground = CreateFrozenBrush(Color.FromArgb(0x66, 0x1D, 0x27, 0x33));
    private static readonly Brush DefaultCardBorderBrush = CreateFrozenBrush(Color.FromArgb(0x33, 0x4D, 0x6B, 0x85));
    private string _key = string.Empty;
    private string _label = string.Empty;
    private string _primaryTimeZoneId = string.Empty;
    private string _secondaryTimeZoneId = string.Empty;
    private string _timeText = "--:--";
    private string _zoneText = string.Empty;
    private string _weatherGlyph = string.Empty;
    private string _weatherText = string.Empty;
    private string _flagGlyph = string.Empty;
    private string _flagCode = string.Empty;
    private bool _supportsWeather;
    private double _latitude;
    private double _longitude;
    private bool _isLocalSummary;
    private bool _showExchangeDetails = true;
    private string _exchangeName = string.Empty;
    private string _exchangeSymbol = string.Empty;
    private string _calendarExchangeCode = string.Empty;
    private string _marketStatusText = string.Empty;
    private Brush _marketStatusForeground = Brushes.Gainsboro;
    private string _indexValueText = "--";
    private string _indexChangeText = "--";
    private Brush _indexChangeForeground = Brushes.Gainsboro;
    private Brush _miniGraphStroke = Brushes.SlateGray;
    private PointCollection _miniGraphPoints = EmptyMiniGraphPoints;
    private Brush _cardBackground = DefaultCardBackground;
    private Brush _cardBorderBrush = DefaultCardBorderBrush;

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public string PrimaryTimeZoneId
    {
        get => _primaryTimeZoneId;
        set => SetProperty(ref _primaryTimeZoneId, value);
    }

    public string SecondaryTimeZoneId
    {
        get => _secondaryTimeZoneId;
        set => SetProperty(ref _secondaryTimeZoneId, value);
    }

    public string TimeText
    {
        get => _timeText;
        set => SetProperty(ref _timeText, value);
    }

    public string ZoneText
    {
        get => _zoneText;
        set => SetProperty(ref _zoneText, value);
    }

    public string WeatherGlyph
    {
        get => _weatherGlyph;
        set => SetProperty(ref _weatherGlyph, value);
    }

    public string WeatherText
    {
        get => _weatherText;
        set => SetProperty(ref _weatherText, value);
    }

    public string FlagGlyph
    {
        get => _flagGlyph;
        set => SetProperty(ref _flagGlyph, value);
    }

    public string FlagCode
    {
        get => _flagCode;
        set => SetProperty(ref _flagCode, value);
    }

    public bool SupportsWeather
    {
        get => _supportsWeather;
        set => SetProperty(ref _supportsWeather, value);
    }

    public double Latitude
    {
        get => _latitude;
        set => SetProperty(ref _latitude, value);
    }

    public double Longitude
    {
        get => _longitude;
        set => SetProperty(ref _longitude, value);
    }

    public bool IsLocalSummary
    {
        get => _isLocalSummary;
        set => SetProperty(ref _isLocalSummary, value);
    }

    public bool ShowExchangeDetails
    {
        get => _showExchangeDetails;
        set => SetProperty(ref _showExchangeDetails, value);
    }

    public string ExchangeName
    {
        get => _exchangeName;
        set => SetProperty(ref _exchangeName, value);
    }

    public string ExchangeSymbol
    {
        get => _exchangeSymbol;
        set => SetProperty(ref _exchangeSymbol, value);
    }

    public string CalendarExchangeCode
    {
        get => _calendarExchangeCode;
        set => SetProperty(ref _calendarExchangeCode, value);
    }

    public string MarketStatusText
    {
        get => _marketStatusText;
        set => SetProperty(ref _marketStatusText, value);
    }

    public Brush MarketStatusForeground
    {
        get => _marketStatusForeground;
        set => SetProperty(ref _marketStatusForeground, value);
    }

    public string IndexValueText
    {
        get => _indexValueText;
        set => SetProperty(ref _indexValueText, value);
    }

    public string IndexChangeText
    {
        get => _indexChangeText;
        set => SetProperty(ref _indexChangeText, value);
    }

    public Brush IndexChangeForeground
    {
        get => _indexChangeForeground;
        set => SetProperty(ref _indexChangeForeground, value);
    }

    public Brush MiniGraphStroke
    {
        get => _miniGraphStroke;
        set => SetProperty(ref _miniGraphStroke, value);
    }

    public PointCollection MiniGraphPoints
    {
        get => _miniGraphPoints;
        set => SetProperty(ref _miniGraphPoints, value);
    }

    /// <summary>
    /// Replaces the WPF point collection only when the rendered points changed.
    /// Call from the UI dispatcher, before WPF starts the next render/layout pass.
    /// </summary>
    public bool SetMiniGraphPointsIfChanged(IEnumerable<Point> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        IReadOnlyList<Point> updatedPoints = points as IReadOnlyList<Point> ?? points.ToArray();

        if (_miniGraphPoints.Count == updatedPoints.Count)
        {
            bool unchanged = true;
            for (int index = 0; index < updatedPoints.Count; index++)
            {
                if (_miniGraphPoints[index] == updatedPoints[index])
                    continue;

                unchanged = false;
                break;
            }

            if (unchanged)
                return false;
        }

        MiniGraphPoints = new PointCollection(updatedPoints);
        return true;
    }

    public Brush CardBackground
    {
        get => _cardBackground;
        set => SetProperty(ref _cardBackground, value);
    }

    public Brush CardBorderBrush
    {
        get => _cardBorderBrush;
        set => SetProperty(ref _cardBorderBrush, value);
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    private static PointCollection CreateFrozenPointCollection(IEnumerable<Point> points)
    {
        PointCollection collection = new(points);
        collection.Freeze();
        return collection;
    }
}
