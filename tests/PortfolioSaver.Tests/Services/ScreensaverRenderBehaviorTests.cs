using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Screensaver.Controls;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ScreensaverRenderBehaviorTests
{
    [Fact]
    public void UpdateTapeItem_TriggersSingleFlashWhenLiveValueChanges()
    {
        MethodInfo method = typeof(ScreensaverSceneControl).GetMethod(
            "UpdateTapeItem",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ScreensaverSceneControl.UpdateTapeItem not found.");

        TapeItemViewModel target = new()
        {
            SymbolText = "AAPL",
            LastText = "190.00",
            ChangeText = "+1.00%",
            QuoteUpdateToken = 100
        };
        TapeItemViewModel source = new()
        {
            SymbolText = "AAPL",
            LastText = "191.00",
            ChangeText = "+1.50%",
            QuoteUpdateToken = 200
        };

        method.Invoke(null, [target, source]);
        Assert.Equal(1, target.UpdateSequence);

        method.Invoke(null, [target, source]);
        Assert.Equal(1, target.UpdateSequence);
    }

    [Fact]
    public void FloatingGraphControl_FlashAnimationUsesTwoColorPulses()
    {
        string codeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "FloatingGraphControl.xaml.cs"));

        int pulseCount = Regex.Matches(codeBehind, @"LinearColorKeyFrame\(flashColor").Count;

        Assert.Equal(2, pulseCount);
        Assert.Contains("FlashSequence", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void NetworkWaitingOverlay_DefinesOverlayTemplateAndBounceMotion()
    {
        string xaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml"));
        string codeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("x:Name=\"NetworkWaitingHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TitleText", xaml, StringComparison.Ordinal);
        Assert.Contains("DetailText", xaml, StringComparison.Ordinal);
        Assert.Contains("_networkWaitingViewModel.BounceWithinViewport = true;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_motionController.Step(_networkWaitingViewModel, GetWaitingBounds(), elapsedSeconds);", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyQuoteToGraph_StaleQuoteHidesCardAndClearsValues()
    {
        RunOnSta(() =>
        {
            ScreensaverSceneControl control = new();
            FloatingGraphViewModel graph = new()
            {
                Symbol = "AAPL",
                LastText = "190.00",
                ChangeText = "+1.00%",
                IsVisible = true
            };

            Dictionary<string, QuoteSnapshot> quotes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = new QuoteSnapshot
                {
                    Symbol = "AAPL",
                    Last = 190m,
                    ChangePercent = 1m,
                    FetchTimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-23),
                    IsStale = false
                }
            };

            FieldInfo latestQuotesField = typeof(ScreensaverSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(ScreensaverSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            Assert.False(graph.IsVisible);
            Assert.Equal(string.Empty, graph.LastText);
            Assert.Equal(string.Empty, graph.ChangeText);
        });
    }

    [Fact]
    public void MacroMeter_ProducesSpeedometerPaths_AndStatusBarBindsNeedle()
    {
        MacroMeterViewModel meter = new();
        meter.SetFill(0.55d);

        Assert.Contains("A", meter.ArcPath, StringComparison.Ordinal);
        Assert.StartsWith("M ", meter.ArcPath, StringComparison.Ordinal);
        Assert.Contains("A", meter.TrackPath, StringComparison.Ordinal);
        Assert.StartsWith("M ", meter.NeedlePath, StringComparison.Ordinal);
        Assert.Contains("L", meter.NeedlePath, StringComparison.Ordinal);

        string statusBarXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "StatusBarControl.xaml"));

        Assert.Contains("ItemsSource=\"{Binding MacroMeters}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Data=\"{Binding ArcPath}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Data=\"{Binding TrackPath}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Data=\"{Binding NeedlePath}\"", statusBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatingGraphControl_RendersLatestSegmentOverlayBoundToDedicatedBrush()
    {
        string xaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "FloatingGraphControl.xaml"));

        Assert.Contains("Points=\"{Binding LatestSegmentPoints}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Stroke=\"{Binding LatestSegmentBrush}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ClockControls_UseStableMonospaceClockFontResource()
    {
        string statusBarXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "StatusBarControl.xaml"));
        string floatingClockXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "FloatingClockControl.xaml"));

        Assert.Contains("x:Key=\"StableClockFont\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"StableClockFont\"", floatingClockXaml, StringComparison.Ordinal);
        Assert.Contains("FontFamily=\"{StaticResource StableClockFont}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("FontFamily=\"{StaticResource StableClockFont}\"", floatingClockXaml, StringComparison.Ordinal);
        Assert.Contains("Cascadia Mono,Consolas,Lucida Console", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Cascadia Mono,Consolas,Lucida Console", floatingClockXaml, StringComparison.Ordinal);
        Assert.Contains("UseLayoutRounding=\"True\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("UseLayoutRounding=\"True\"", floatingClockXaml, StringComparison.Ordinal);
        Assert.Contains("TextOptions.TextFormattingMode=\"Display\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("TextOptions.TextFormattingMode=\"Display\"", floatingClockXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"Auto\" />", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("ClipToBounds=\"True\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"56\"", floatingClockXaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"102\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"30\"", floatingClockXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BackgroundTransition_UsesStableCrossfade_AndKeepsSlowZoomLoop()
    {
        string codeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("_backgroundZoomTimer.Tick += (_, _) => StepBackgroundSlowZoom();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void StepBackgroundSlowZoom()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("AnimateBackgroundProperty(incoming, Image.OpacityProperty, 0d, 0.45d, duration, ease);", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("switch (_random.Next(3))", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("AnimateBackgroundTranslation(incoming", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateTapeItem_StaleToLiveRecovery_TriggersFlash()
    {
        MethodInfo method = typeof(ScreensaverSceneControl).GetMethod(
            "UpdateTapeItem",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ScreensaverSceneControl.UpdateTapeItem not found.");

        TapeItemViewModel target = new()
        {
            SymbolText = "AAPL",
            LastText = string.Empty,
            ChangeText = string.Empty
        };
        TapeItemViewModel source = new()
        {
            SymbolText = "AAPL",
            LastText = "190.50",
            ChangeText = "+0.40%",
            QuoteUpdateToken = 200
        };

        method.Invoke(null, [target, source]);
        Assert.Equal(1, target.UpdateSequence);
    }

    [Fact]
    public void ApplyQuoteToGraph_StaleToLiveRecovery_TriggersCardFlash()
    {
        RunOnSta(() =>
        {
            ScreensaverSceneControl control = new();
            FloatingGraphViewModel graph = new()
            {
                Symbol = "AAPL",
                LastText = string.Empty,
                ChangeText = string.Empty,
                IsVisible = true
            };

            Dictionary<string, QuoteSnapshot> quotes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = new QuoteSnapshot
                {
                    Symbol = "AAPL",
                    Last = 190m,
                    ChangePercent = 1m,
                    FetchTimestampUtc = DateTimeOffset.UtcNow,
                    IsStale = false
                }
            };

            FieldInfo latestQuotesField = typeof(ScreensaverSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(ScreensaverSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            Assert.Equal(1, graph.FlashSequence);
        });
    }

    [Fact]
    public void ApplyQuoteToGraph_AlignsLatestSegmentBrushWithLatestQuoteDirection()
    {
        RunOnSta(() =>
        {
            ScreensaverSceneControl control = new();
            FloatingGraphViewModel graph = new()
            {
                Symbol = "AAPL",
                LastText = "190.00",
                ChangeText = "+0.50%",
                LatestSegmentBrush = Brushes.LimeGreen,
                LatestSegmentPoints = [new Point(0, 10), new Point(10, 0)],
                IsVisible = true
            };

            Dictionary<string, QuoteSnapshot> quotes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = new QuoteSnapshot
                {
                    Symbol = "AAPL",
                    Last = 188m,
                    ChangePercent = -1.25m,
                    FetchTimestampUtc = DateTimeOffset.UtcNow,
                    IsStale = false
                }
            };

            FieldInfo latestQuotesField = typeof(ScreensaverSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(ScreensaverSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            Assert.Same(Brushes.OrangeRed, graph.ChangeForeground);
            Assert.Same(Brushes.OrangeRed, graph.LatestSegmentBrush);
            Assert.Equal(2, graph.LatestSegmentPoints.Count);
        });
    }

    [Fact]
    public void ApplyQuoteToGraph_PositiveRefreshQueuesTopEdgeImpulse()
    {
        RunOnSta(() =>
        {
            ScreensaverSceneControl control = new()
            {
                Width = 800,
                Height = 600
            };
            control.Measure(new Size(800, 600));
            control.Arrange(new Rect(0, 0, 800, 600));
            control.UpdateLayout();

            FloatingGraphViewModel graph = new()
            {
                Symbol = "AAPL",
                LastText = "190.00",
                ChangeText = "+0.50%",
                Width = 140,
                Height = 84,
                X = 240,
                Y = 260,
                VelocityY = -7,
                NominalVelocityY = -7,
                IsVisible = true
            };

            Dictionary<string, QuoteSnapshot> quotes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = new QuoteSnapshot
                {
                    Symbol = "AAPL",
                    Last = 191m,
                    ChangePercent = 1.25m,
                    FetchTimestampUtc = DateTimeOffset.UtcNow,
                    IsStale = false
                }
            };

            FieldInfo latestQuotesField = typeof(ScreensaverSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(ScreensaverSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            MethodInfo boundsMethod = typeof(ScreensaverSceneControl).GetMethod(
                "GetGraphMotionBounds",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetGraphMotionBounds method not found.");
            Rect bounds = Assert.IsType<Rect>(boundsMethod.Invoke(control, []));

            Assert.Equal(bounds.Top, graph.RefreshTravelTargetY);
            Assert.True(graph.IsRefreshTravelFlashActive);
            Assert.Equal(260, graph.Y);
            Assert.Equal(-7d, graph.VelocityY);
        });
    }

    [Fact]
    public void ApplyQuoteToGraph_NegativeRefreshQueuesBottomEdgeImpulse()
    {
        RunOnSta(() =>
        {
            ScreensaverSceneControl control = new()
            {
                Width = 800,
                Height = 600
            };
            control.Measure(new Size(800, 600));
            control.Arrange(new Rect(0, 0, 800, 600));
            control.UpdateLayout();

            FloatingGraphViewModel graph = new()
            {
                Symbol = "AAPL",
                LastText = "190.00",
                ChangeText = "+0.50%",
                Width = 140,
                Height = 84,
                X = 240,
                Y = 120,
                VelocityY = 9,
                NominalVelocityY = 9,
                IsVisible = true
            };

            Dictionary<string, QuoteSnapshot> quotes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = new QuoteSnapshot
                {
                    Symbol = "AAPL",
                    Last = 188m,
                    ChangePercent = -1.25m,
                    FetchTimestampUtc = DateTimeOffset.UtcNow,
                    IsStale = false
                }
            };

            FieldInfo latestQuotesField = typeof(ScreensaverSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(ScreensaverSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            MethodInfo boundsMethod = typeof(ScreensaverSceneControl).GetMethod(
                "GetGraphMotionBounds",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetGraphMotionBounds method not found.");
            Rect bounds = Assert.IsType<Rect>(boundsMethod.Invoke(control, []));

            Assert.Equal(bounds.Bottom - graph.Height, graph.RefreshTravelTargetY);
            Assert.True(graph.IsRefreshTravelFlashActive);
            Assert.Equal(120, graph.Y);
            Assert.Equal(9d, graph.VelocityY);
        });
    }

    [Fact]
    public void ResetGraphRefreshImpulseIfNeeded_ClearsTravelFlashAfterBoundaryHit()
    {
        RunOnSta(() =>
        {
            FloatingGraphViewModel graph = new()
            {
                Height = 84,
                Y = 0,
                VelocityY = -14,
                NominalVelocityY = -7,
                RefreshTravelTargetY = 0,
                IsRefreshTravelFlashActive = true
            };

            MethodInfo method = typeof(ScreensaverSceneControl).GetMethod(
                "ResetGraphRefreshImpulseIfNeeded",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("ResetGraphRefreshImpulseIfNeeded method not found.");

            method.Invoke(null, [graph, new Rect(0, 0, 800, 500)]);

            Assert.Null(graph.RefreshTravelTargetY);
            Assert.False(graph.IsRefreshTravelFlashActive);
            Assert.Equal(7d, graph.VelocityY);
        });
    }

    [Fact]
    public void UpdateTapeItem_SameValueWithNewerFetchToken_StillTriggersFlash()
    {
        MethodInfo method = typeof(ScreensaverSceneControl).GetMethod(
            "UpdateTapeItem",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ScreensaverSceneControl.UpdateTapeItem not found.");

        TapeItemViewModel target = new()
        {
            SymbolText = "AAPL",
            LastText = "190.00",
            ChangeText = "+1.00%",
            QuoteUpdateToken = 100
        };
        TapeItemViewModel source = new()
        {
            SymbolText = "AAPL",
            LastText = "190.00",
            ChangeText = "+1.00%",
            QuoteUpdateToken = 200
        };

        method.Invoke(null, [target, source]);

        Assert.Equal(1, target.UpdateSequence);
        Assert.Equal(200, target.QuoteUpdateToken);
    }

    [Fact]
    public void MergeQuotes_PreservesCachedSymbolsWhileApplyingWarmupBatch()
    {
        MethodInfo mergeMethod = typeof(ScreensaverSceneControl).GetMethod(
            "MergeQuotes",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ScreensaverSceneControl.MergeQuotes not found.");

        IReadOnlyDictionary<string, QuoteSnapshot> existing = new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new QuoteSnapshot { Symbol = "AAPL", Last = 190m, FetchTimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-1) },
            ["MSFT"] = new QuoteSnapshot { Symbol = "MSFT", Last = 380m, FetchTimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-1) }
        };
        IReadOnlyDictionary<string, QuoteSnapshot> incoming = new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new QuoteSnapshot { Symbol = "AAPL", Last = 191m, FetchTimestampUtc = DateTimeOffset.UtcNow },
            ["NVDA"] = new QuoteSnapshot { Symbol = "NVDA", Last = 950m, FetchTimestampUtc = DateTimeOffset.UtcNow }
        };

        IReadOnlyDictionary<string, QuoteSnapshot> merged = Assert.IsAssignableFrom<IReadOnlyDictionary<string, QuoteSnapshot>>(
            mergeMethod.Invoke(null, [existing, incoming]));

        Assert.Equal(3, merged.Count);
        Assert.Equal(191m, merged["AAPL"].Last);
        Assert.Equal(380m, merged["MSFT"].Last);
        Assert.Equal(950m, merged["NVDA"].Last);
    }

    [Fact]
    public void ScreensaverLayout_UsesGlobalMarketsTapeAndWaitingBounds()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));
        string sceneXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml"));
        string globalTapeXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "GlobalMarketsTapeControl.xaml"));

        Assert.Contains("private Rect GetWaitingBounds()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_motionController.Step(_networkWaitingViewModel, GetWaitingBounds(), elapsedSeconds);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlobalMarketsTapeHost\"", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("<controls:GlobalMarketsTapeControl />", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NewsFlasherHost\"", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("GlobalMarketsTapeHost.Visibility = Visibility.Visible;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("GlobalMarketsTapeHost.Content = _clockViewModel;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ViewportHost\"", globalTapeXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", globalTapeXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupWarmup_DoesNotDriveFloatingWaitingCardProgressText()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.DoesNotContain("_networkWaitingViewModel.TitleText = \"Loading live market data\";", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void GetRefreshSeconds_WhenActiveSymbolsAreStillMissing_UsesHalfSecondRecoveryInterval()
    {
        RunOnSta(() =>
        {
            ScreensaverSceneControl control = new();

            FieldInfo settingsField = typeof(ScreensaverSceneControl).GetField(
                "_settings",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_settings field not found.");
            settingsField.SetValue(control, new AppSettings
            {
                RefreshSecondsPortfolio = 1200,
                RefreshSecondsOffHours = 1200,
                Groups =
                [
                    new TickerGroup
                    {
                        Name = "Tape 1",
                        Enabled = true,
                        Tickers =
                        [
                            new TickerItem { Symbol = "AAPL", DisplayName = "AAPL", Enabled = true }
                        ]
                    }
                ]
            });

            FieldInfo latestQuotesField = typeof(ScreensaverSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(
                control,
                new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AAPL"] = new QuoteSnapshot
                    {
                        Symbol = "AAPL",
                        Last = 190m,
                        PreviousClose = 189m,
                        FetchTimestampUtc = DateTimeOffset.UtcNow
                    }
                });

            MethodInfo method = typeof(ScreensaverSceneControl).GetMethod(
                "GetRefreshSeconds",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetRefreshSeconds method not found.");

            double refreshSeconds = Assert.IsType<double>(method.Invoke(control, []));
            Assert.Equal(0.5d, refreshSeconds);
        });
    }

    [Fact]
    public void GetRefreshSeconds_WhenAllTrackedSymbolsAreFresh_PollsTwicePerSecond()
    {
        RunOnSta(() =>
        {
            ScreensaverSceneControl control = new();

            FieldInfo settingsField = typeof(ScreensaverSceneControl).GetField(
                "_settings",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_settings field not found.");
            settingsField.SetValue(control, new AppSettings
            {
                RefreshSecondsPortfolio = 5,
                RefreshSecondsOffHours = 5,
                Groups =
                [
                    new TickerGroup
                    {
                        Name = "Tape 1",
                        Enabled = true,
                        Tickers =
                        [
                            new TickerItem { Symbol = "AAPL", DisplayName = "AAPL", Enabled = true }
                        ]
                    }
                ]
            });

            Dictionary<string, QuoteSnapshot> quotes = new(StringComparer.OrdinalIgnoreCase);
            foreach (string symbol in FloatingClockBuilder.GetWorldIndexSymbols()
                         .Concat(StartupCoordinator.GetMacroIndicatorSymbols())
                         .Append("AAPL")
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                quotes[symbol] = new QuoteSnapshot
                {
                    Symbol = symbol,
                    Last = 100m,
                    PreviousClose = 99m,
                    FetchTimestampUtc = DateTimeOffset.UtcNow
                };
            }

            FieldInfo latestQuotesField = typeof(ScreensaverSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo method = typeof(ScreensaverSceneControl).GetMethod(
                "GetRefreshSeconds",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetRefreshSeconds method not found.");

            double refreshSeconds = Assert.IsType<double>(method.Invoke(control, []));
            Assert.Equal(0.5d, refreshSeconds);
        });
    }

    [Fact]
    public void ProviderStatusText_IsCompactedForTopBandReadability()
    {
        MethodInfo method = typeof(ScreensaverSceneControl).GetMethod(
            "CompactProviderText",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CompactProviderText method not found.");

        string compact = Assert.IsType<string>(method.Invoke(null,
        [
            "Provider: YFinance.NET + Cache (Partial), AAPL Updated 6s ago"
        ]));

        Assert.Equal("Provider: Live+Cache (Partial)", compact);
    }

    [Fact]
    public void ProviderStatusText_DropsCacheSuffixOnceAllQuotesAreLive()
    {
        RunOnSta(() =>
        {
            ScreensaverSceneControl control = new();
            StatusBarViewModel status = new() { ProviderText = "Provider: YFinance.NET + Cache, ^SPX Updated 0s ago" };

            FieldInfo statusField = typeof(ScreensaverSceneControl).GetField(
                "_statusViewModel",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_statusViewModel field not found.");
            statusField.SetValue(control, status);

            FieldInfo latestQuotesField = typeof(ScreensaverSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(
                control,
                new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    ["^SPX"] = new QuoteSnapshot
                    {
                        Symbol = "^SPX",
                        Last = 100m,
                        PreviousClose = 99m,
                        FetchTimestampUtc = DateTimeOffset.UtcNow,
                        IsStale = false
                    }
                });

            MethodInfo compactMethod = typeof(ScreensaverSceneControl).GetMethod(
                "CompactStatusText",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("CompactStatusText method not found.");

            compactMethod.Invoke(control, []);

            Assert.Equal("Provider: Live", status.ProviderText);
        });
    }

    [Fact]
    public void StatusFreshness_IsRecomputedFromLatestQuoteTimestamps()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("UpdateStatusFreshnessText(_statusViewModel.UpdatedText);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void UpdateStatusFreshnessText(string? fallbackText = null)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("StartupCoordinator.ShouldShowInitialValueLoadingStatus(_latestQuotes, _settings, GetReferenceUtcNow())", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_statusViewModel.UpdatedText = \"Loading initial values\";", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("StartupCoordinator.TryGetStatusFreshnessAnchorFetchUtc(_latestQuotes, out DateTimeOffset anchorQuoteFetchUtc)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_statusViewModel.UpdatedText = $\"Updated: {TimeFormatHelper.ToAgeString(anchorQuoteFetchUtc)}\";", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void NewsFlasher_UsesTeleprinterPlaybackInsteadOfMarqueeLoop()
    {
        string newsXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "NewsFlasherControl.xaml"));
        string newsCode = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "NewsFlasherControl.xaml.cs"));

        Assert.Contains("FontFamily=\"Courier New\"", newsXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ActiveHeadlineBlock\"", newsXaml, StringComparison.Ordinal);
        Assert.Contains("LineStackingStrategy=\"BlockLineHeight\"", newsXaml, StringComparison.Ordinal);
        Assert.Contains("FormatHeadline", newsCode, StringComparison.Ordinal);
        Assert.Contains("Regex.Replace(normalized, @\"[\\u0000-\\u001F\\u007F]+\", \" \")", newsCode, StringComparison.Ordinal);
        Assert.Contains("Regex.Replace(normalized, @\"\\s+\", \" \")", newsCode, StringComparison.Ordinal);
        Assert.Contains("upper + \" STOP\"", newsCode, StringComparison.Ordinal);
        Assert.Contains("PlaybackPhase.Typing", newsCode, StringComparison.Ordinal);
        Assert.Contains("PlaybackPhase.Scrolling", newsCode, StringComparison.Ordinal);
        Assert.Contains("PlaybackPhase.Clearing", newsCode, StringComparison.Ordinal);
        Assert.Contains("TeleprinterCursor", newsCode, StringComparison.Ordinal);
        Assert.Contains("SetDisplayedHeadlineText(_activeText, includeCursor: true);", newsCode, StringComparison.Ordinal);
        Assert.Contains("ClearCharactersPerTick = 2", newsCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TapeAndStatusBarLayout_UseSafeInsetsAndFixedHeightForMotionStability()
    {
        string tapeXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "TickerTapeControl.xaml"));
        string tapeCode = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "TickerTapeControl.xaml.cs"));
        string statusXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "StatusBarControl.xaml"));
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("x:Name=\"ViewportHost\"", tapeXaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"4,0,4,0\"", tapeXaml, StringComparison.Ordinal);
        Assert.Contains("nameof(TapeItemViewModel.IsWaitingOnData)", tapeCode, StringComparison.Ordinal);
        Assert.Contains("Source = item", tapeCode, StringComparison.Ordinal);
        Assert.Contains("nameof(TapeItemViewModel.WaitingGlyphText)", tapeCode, StringComparison.Ordinal);
        Assert.Contains("nameof(TapeItemViewModel.WaitingGlyphForeground)", tapeCode, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"72\"", statusXaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", statusXaml, StringComparison.Ordinal);
        Assert.Contains("double statusHeight = Math.Max(72d, StatusBarHost.ActualHeight);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("double tapeTopMargin = Math.Clamp(statusHeight + 12d, 78d, 126d);", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreensaverScene_ShowsBrandedAppTitleVersionWatermark_AndClockUsesStableMonospaceClockFonts()
    {
        string sceneXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml"));
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));
        string fullScreenWindowCode = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Screensaver",
            "Windows",
            "FullScreenHostWindow.xaml.cs"));
        string statusBarXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "StatusBarControl.xaml"));
        string floatingClockXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "FloatingClockControl.xaml"));

        Assert.Contains("x:Name=\"VersionWatermark\"", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("SANYALnet Labs DO NOT PANIC Portfolio Visualizer", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("&#169; Supratim Sanyal. MIT License.", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"19\"", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("DropShadowEffect", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("VersionWatermark.Text = PortfolioVersion.SemanticVersion;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetAutomationId(VersionWatermark, \"ScreensaverVersionWatermark\");", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(VersionWatermark, $\"Version {PortfolioVersion.SemanticVersion}\");", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetHelpText(VersionWatermark, PortfolioVersion.SemanticVersion);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("Title = $\"Portfolio Screensaver {PortfolioVersion.SemanticVersion}\";", fullScreenWindowCode, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetAutomationId(this, \"ScreensaverHostWindow\");", fullScreenWindowCode, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetHelpText(this, PortfolioVersion.SemanticVersion);", fullScreenWindowCode, StringComparison.Ordinal);
        Assert.Contains("StableClockFont", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("StableClockFont", floatingClockXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SevenSegmentDigitConverter", statusBarXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SevenSegmentDigitConverter", floatingClockXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreensaverScene_HostsGlobalMarketsTapeAboveNewsTicker()
    {
        string sceneXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml"));

        int globalMarketsIndex = sceneXaml.IndexOf("x:Name=\"GlobalMarketsTapeHost\"", StringComparison.Ordinal);
        int newsIndex = sceneXaml.IndexOf("x:Name=\"NewsFlasherHost\"", StringComparison.Ordinal);
        Assert.True(globalMarketsIndex >= 0);
        Assert.True(newsIndex > globalMarketsIndex);
    }

    [Fact]
    public void ScreensaverScene_LetsGraphCardsUseFullCanvasBehindForeground()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("private Rect GetGraphMotionBounds()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("=> GetFullCanvasBounds();", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalMarketsTapeControl_UsesMarqueeMarketCards()
    {
        string globalTapeXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "GlobalMarketsTapeControl.xaml"));
        string globalTapeCode = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "GlobalMarketsTapeControl.xaml.cs"));

        Assert.Contains("Text=\"{Binding Title}\"", globalTapeXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PinnedCardHost\"", globalTapeXaml, StringComparison.Ordinal);
        Assert.Contains("TapeAnimationController", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("PinnedExchangeKey = \"NewYorkNasdaq\"", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("GetScrollingExchangeCities", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("RefreshPinnedCard", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("IndexValueText", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("MiniGraphPoints", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("WeatherGlyph", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("BuildFlagBadge(city)", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("nameof(ClockCityViewModel.FlagCode)", globalTapeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(ClockCityViewModel.FlagGlyph)", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("private const double CardWidth = 164d;", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("private const double PinnedCardWidth = 150d;", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("private const double CardHeight = 54d;", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("private const double CopySpacing = 14d;", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("private const double SequenceLeadInSpacing = 10d;", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("private const double SequenceTailSpacing = 10d;", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("Background = Brushes.Transparent", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("BorderThickness = new Thickness(0)", globalTapeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding(nameof(ClockCityViewModel.CardBackground))", globalTapeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding(nameof(ClockCityViewModel.CardBorderBrush))", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("Height=\"68\"", globalTapeXaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"8,0,10,0\"", globalTapeXaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"16,0,28,0\"", globalTapeXaml, StringComparison.Ordinal);
        Assert.Contains("<Grid.OpacityMask>", globalTapeXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LeftEdgeShroud\"", globalTapeXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RightEdgeShroud\"", globalTapeXaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"118\"", globalTapeXaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"36\"", globalTapeXaml, StringComparison.Ordinal);
        Assert.Contains("Offset=\"0.22\"", globalTapeXaml, StringComparison.Ordinal);
        Assert.Contains("Offset=\"0.90\"", globalTapeXaml, StringComparison.Ordinal);
        Assert.Contains("Offset=\"1.00\"", globalTapeXaml, StringComparison.Ordinal);
        Assert.Contains("Width = SequenceLeadInSpacing", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("Width = SequenceTailSpacing", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("panel.Children.Add(BuildDelimiter());", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("private static TextBlock BuildDelimiter()", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("private const double FlagBadgeWidth = 20d;", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("private const double FlagBadgeHeight = 14d;", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("FlagImageCache", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("GetFlagImageSource(city.FlagCode)", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("pack://application:,,,/PortfolioSaver.Render;component/Assets/Flags/", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("Stretch = Stretch.Uniform", globalTapeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildUnitedStatesFlag()", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("FontSize = 10", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("Text = \"5D\"", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("Text = \"now\"", globalTapeCode, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalMarkets_HoldWaitingGlyphUntilInitialValuesAreReady()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("private const string GlobalMarketsWaitingGlyph = \"🕒\";", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("bool loadingInitialValues = StartupCoordinator.ShouldShowInitialValueLoadingStatus(_latestQuotes, _settings, GetReferenceUtcNow());", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("city.IndexValueText = GlobalMarketsWaitingGlyph;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("city.IndexChangeText = \"--\";", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("city.IndexChangeForeground = Brushes.Goldenrod;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("city.MiniGraphPoints = [];", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("List<string> loadingSymbols = [];", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("loadingSymbols.Add(city.ExchangeSymbol);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("new KeyValuePair<string, object?>(\"loading_exchange_count\", loadingSymbols.Count)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("new KeyValuePair<string, object?>(\"loading_exchange_symbols\", loadingSymbols.Take(10).ToList())", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatingGraphCards_UseIndependentMotionAcrossFullCanvas_AndRightLabelGutter()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));
        string floatingGraphXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "FloatingGraphControl.xaml"));
        string startupCoordinator = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Services",
            "StartupCoordinator.cs"));

        Assert.Contains("private const int MaxVisibleGraphCards = 12;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("SeparateVisibleGraphCards(bounds);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("GraphCardSeparationGap", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("foreach (FloatingGraphViewModel graph in EnumerateVisibleGraphCards())", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyGraphRefreshImpulse(graph, bounds);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ResetGraphRefreshImpulseIfNeeded(graph, bounds);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyGraphMotionVariance(", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyGraphRefreshTravel(", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("graph.PlotWidth = Math.Max(106d, graphWidth - 62d);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("Margin=\"2,3,12,0\"", floatingGraphXaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"24\"", floatingGraphXaml, StringComparison.Ordinal);
        Assert.Contains("const int maxSceneGraphCards = 12;", startupCoordinator, StringComparison.Ordinal);
        Assert.Contains("return pairs.Take(maxSceneGraphCards).ToList();", startupCoordinator, StringComparison.Ordinal);
        Assert.Contains("const int graphLookbackDays = 1;", startupCoordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusMacroMeters_UpdateInPlace_AndPreserveStaleMacroValues()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("EnsureMacroMetersInitialized();", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("_statusViewModel.MacroMeters.Clear();", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"^IXIC\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"^IRX\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"^TNX\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"BZ=F\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"BTC-USD\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"GC=F\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"GOLD\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"CRUDE\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("\"YLD SPRD\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("Brushes.Goldenrod", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("bool loadingInitialValues = StartupCoordinator.ShouldShowInitialValueLoadingStatus(_latestQuotes, _settings, GetReferenceUtcNow());", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyWaitingMacroMeter(meter);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("meter.ValueText = GlobalMarketsWaitingGlyph;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("new KeyValuePair<string, object?>(\"stale_symbols\", staleSymbols)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("quote.Last is null && quote.PreviousClose is null", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("graph.IsRefreshTravelFlashActive = true;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("graph.IsRefreshTravelFlashActive = false;", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusBarControl_PlacesMacroMetersInTopCenteredLane()
    {
        string statusBarXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "StatusBarControl.xaml"));
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("ItemsSource=\"{Binding MacroMeters}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding MarketStatusText}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ClockDateText}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ClockText}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding ProviderText}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"Auto\" />", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding UpdatedText}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"102\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"88\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"24\" />", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"52\" />", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"30\" />", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"22\" />", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"208\"", statusBarXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"700\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("LineHeight=\"15\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Matches(new Regex("Text=\\\"\\{Binding MarketStatusText\\}\\\"[\\s\\S]*FontFamily=\\\"\\{StaticResource StableClockFont\\}\\\"", RegexOptions.CultureInvariant), statusBarXaml);
        Assert.Matches(new Regex("Text=\\\"\\{Binding UpdatedText\\}\\\"[\\s\\S]*FontFamily=\\\"\\{StaticResource StableClockFont\\}\\\"", RegexOptions.CultureInvariant), statusBarXaml);
        Assert.Contains("treasuryYieldMeterMax = 6m", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void TickerTape_WaitingGlyphLivesBesideSymbolWhileWaitingValuesStayBlank()
    {
        string tapeCode = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "TickerTapeControl.xaml.cs"));

        Assert.Contains("CreateSymbolHost(item)", tapeCode, StringComparison.Ordinal);
        Assert.Contains("private static FrameworkElement CreateSymbolHost(TapeItemViewModel item)", tapeCode, StringComparison.Ordinal);
        Assert.Contains("CreateWaitingGlyphHost(item)", tapeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("showWaitingGlyph: true", tapeCode, StringComparison.Ordinal);
        Assert.Contains("Width = 62d", tapeCode, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(1, 0, 0, 0)", tapeCode, StringComparison.Ordinal);
        Assert.Contains("FontSize = 11", tapeCode, StringComparison.Ordinal);
        Assert.Contains("RenderingEventArgs", File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Render", "Services", "TapeAnimationController.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void ScreensaverScene_EmitsDisplayedTapeSamplesForSoakComparison()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("TraceDisplayedTapeSample();", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void TraceDisplayedTapeSample()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DisplayedTapeSample", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("~{NormalizeTapeSnapshotValue(item.LastText)}~", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreensaverScene_SeparatesTitleLaneFromTopStatusBand()
    {
        string sceneXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml"));

        Assert.Contains("SANYALnet Labs DO NOT PANIC Portfolio Visualizer", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0,10,0,0\"", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StatusBarHost\"", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0,42,0,0\"", sceneXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateQuoteMeter_InvertsVolatilityRiskColors()
    {
        RunOnSta(() =>
        {
            ScreensaverSceneControl control = new();
            MacroMeterViewModel meter = new();

            FieldInfo latestQuotesField = typeof(ScreensaverSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");

            latestQuotesField.SetValue(
                control,
                new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    ["^VIX"] = new QuoteSnapshot
                    {
                        Symbol = "^VIX",
                        Last = 19m,
                        ChangePercent = 2m,
                        FetchTimestampUtc = DateTimeOffset.UtcNow
                    }
                });

            MethodInfo method = typeof(ScreensaverSceneControl).GetMethod(
                "UpdateQuoteMeter",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("UpdateQuoteMeter method not found.");

            method.Invoke(control, [meter, "VIX", "^VIX", 60m, false, true]);

            Assert.Same(Brushes.OrangeRed, meter.AccentBrush);
            Assert.Equal("+2.0%", meter.ChangeText);
        });
    }

    [Fact]
    public void UpdateQuoteMeter_InvertsDollarStrengthColors_ForDxy()
    {
        RunOnSta(() =>
        {
            ScreensaverSceneControl control = new();
            MacroMeterViewModel meter = new();

            FieldInfo latestQuotesField = typeof(ScreensaverSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");

            latestQuotesField.SetValue(
                control,
                new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DX-Y.NYB"] = new QuoteSnapshot
                    {
                        Symbol = "DX-Y.NYB",
                        Last = 98m,
                        ChangePercent = 0.8m,
                        FetchTimestampUtc = DateTimeOffset.UtcNow
                    }
                });

            MethodInfo method = typeof(ScreensaverSceneControl).GetMethod(
                "UpdateQuoteMeter",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("UpdateQuoteMeter method not found.");

            method.Invoke(control, [meter, "DXY", "DX-Y.NYB", 120m, false, true]);

            Assert.Same(Brushes.OrangeRed, meter.AccentBrush);
            Assert.Equal("+0.8%", meter.ChangeText);
        });
    }

    [Fact]
    public void UpdateQuoteMeter_UsesGreenForFallingDollarStrength()
    {
        RunOnSta(() =>
        {
            ScreensaverSceneControl control = new();
            MacroMeterViewModel meter = new();

            FieldInfo latestQuotesField = typeof(ScreensaverSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");

            latestQuotesField.SetValue(
                control,
                new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DX-Y.NYB"] = new QuoteSnapshot
                    {
                        Symbol = "DX-Y.NYB",
                        Last = 98m,
                        ChangePercent = -0.3m,
                        FetchTimestampUtc = DateTimeOffset.UtcNow
                    }
                });

            MethodInfo method = typeof(ScreensaverSceneControl).GetMethod(
                "UpdateQuoteMeter",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("UpdateQuoteMeter method not found.");

            method.Invoke(control, [meter, "DXY", "DX-Y.NYB", 120m, false, true]);

            Assert.Same(Brushes.LimeGreen, meter.AccentBrush);
            Assert.Equal("-0.3%", meter.ChangeText);
        });
    }

    [Fact]
    public void ClockUpdates_ThrottleAncillaryMarketRedraws_WhileKeepingTimeTicksLive()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("private void UpdateClocks(bool forceAncillaryRefresh = false)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_lastClockAncillaryRefreshUtc", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_lastStatusAncillaryRefreshUtc", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("UpdateClockEntries(referenceUtc, refreshClockAncillary);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("UpdateClocks(forceAncillaryRefresh: true);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void UpdateClockEntries(DateTimeOffset referenceUtc, bool refreshAncillary)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyClockMarketData();", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void ApplyClockMarketData(bool force)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("if (force || shouldAppend || city.MiniGraphPoints.Count == 0)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("city.TimeText = FormatClockTimeWithZone(cityTime, zone);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("FormatClockTimeWithZone(", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_statusViewModel.ClockText = FormatClockTimeWithZone(", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("TimeZoneInfo.Utc", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphMotionBounds_UseFullCanvasBecauseGraphsSwimBehindForeground()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("private Rect GetGraphMotionBounds()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("=> GetFullCanvasBounds();", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphWarmup_PreservesInFlightWarmupAcrossRefreshTicks()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("private Task? _graphWarmupTask;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("if (preserveLayout && _graphWarmupTask is not null && !_graphWarmupTask.IsCompleted)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("TraceScene(\"RestartGraphWarmup skipped because a graph warmup is already running.\");", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_graphWarmupTask = WarmGraphsAsync(rotationSeed, preserveLayout, cancellation.Token);", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreensaverScene_ProvidesDeterministicDemoFlashPulses_ForVisualValidation()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));

        Assert.Contains("_demoFlashTimer", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("StartDemoFlashSequence()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RunDemoFlashPulse()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("TriggerValueFlash(Brushes.DeepSkyBlue)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("TriggerCardFlash(Brushes.DeepSkyBlue)", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreensaverScene_IncludesAnimatedMarketCritterOverlay()
    {
        string sceneXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml"));
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "ScreensaverSceneControl.xaml.cs"));
        string critterViewModel = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "ViewModels",
            "MarketSpriteViewModel.cs"));

        Assert.Contains("MarketSpriteItemsControl", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("DataType=\"{x:Type vm:MarketSpriteViewModel}\"", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("private static readonly bool EnableMarketCritters = false;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("if (EnableMarketCritters)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("MarketSpriteItemsControl.Visibility = Visibility.Collapsed;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("EnsureMarketSpritesInitialized()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("StepMarketSpriteMotion(elapsedSeconds);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SpriteText = \"🐂\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SpriteText = \"🐻\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SpriteText = \"💵\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SpriteText = \"💶\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class MarketSpriteViewModel", critterViewModel, StringComparison.Ordinal);
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "PortfolioScreensaver.sln");
            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}







