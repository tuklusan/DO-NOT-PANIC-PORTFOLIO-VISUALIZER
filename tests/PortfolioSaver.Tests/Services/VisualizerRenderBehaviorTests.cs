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
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Globalization;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Render.Controls;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Presentation.Controls;
using PortfolioSaver.Presentation.Services;
using Xunit;
using CurrentTradingPeriods = YFinance.NET.Models.CurrentTradingPeriods;
using TradingPeriodWindow = YFinance.NET.Models.TradingPeriodWindow;

namespace PortfolioSaver.Tests.Services;

public sealed class VisualizerRenderBehaviorTests
{
    [Fact]
    public void FloatingSpriteMotionController_ClampsNonBouncingSprites_WithoutReversingVelocity()
    {
        FloatingSpriteMotionController controller = new();
        MarketSpriteViewModel sprite = new()
        {
            Width = 20,
            Height = 10,
            X = 150,
            Y = 100,
            VelocityX = 40,
            VelocityY = 30,
            BounceWithinViewport = false
        };

        controller.Step(sprite, new Rect(0, 0, 100, 80), 1);

        Assert.Equal(80, sprite.X);
        Assert.Equal(70, sprite.Y);
        Assert.Equal(40, sprite.VelocityX);
        Assert.Equal(30, sprite.VelocityY);
    }

    [Fact]
    public void FloatingSpriteMotionController_ClampsBouncingSprites_AndReversesVelocity()
    {
        FloatingSpriteMotionController controller = new();
        MarketSpriteViewModel sprite = new()
        {
            Width = 20,
            Height = 10,
            X = 150,
            Y = 100,
            VelocityX = 40,
            VelocityY = 30,
            BounceWithinViewport = true
        };

        controller.Step(sprite, new Rect(0, 0, 100, 80), 1);

        Assert.Equal(80, sprite.X);
        Assert.Equal(70, sprite.Y);
        Assert.Equal(-40, sprite.VelocityX);
        Assert.Equal(-30, sprite.VelocityY);
    }

    [Fact]
    public void UpdateTapeItem_TriggersSingleFlashWhenLiveValueChanges()
    {
        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "UpdateTapeItem",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VisualizerSceneControl.UpdateTapeItem not found.");

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
        Assert.Contains("SustainedFlashVisualMaximumDuration", codeBehind, StringComparison.Ordinal);
        Assert.Contains("new RepeatBehavior(SustainedFlashVisualMaximumDuration)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("RepeatBehavior.Forever", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualizerSceneControl_SyncGraphVisualsUpdatesIncrementally()
    {
        string codeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));

        Assert.Contains("_graphControlsByKey", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_graphControlsByKey.TryGetValue(graphKey", codeBehind, StringComparison.Ordinal);
        Assert.Contains("staleControl.DataContext = null;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("FloatingGraphCanvas.Children.Remove(staleControl)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("FloatingGraphCanvas.Children.Insert(Math.Min(index, FloatingGraphCanvas.Children.Count), control)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("int currentIndex = FloatingGraphCanvas.Children.IndexOf(control);", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("FloatingGraphCanvas.Children.Add(control)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("FloatingGraphCanvas.Children.Clear()", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualizerSceneControl_FreezeForCrossThreadSnapshot_FreezesMutableBrush()
    {
        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "FreezeForCrossThreadSnapshot",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VisualizerSceneControl.FreezeForCrossThreadSnapshot not found.");
        SolidColorBrush mutableBrush = new(Colors.OrangeRed);

        Brush result = Assert.IsAssignableFrom<Brush>(method.Invoke(null, [mutableBrush]));

        Assert.NotSame(mutableBrush, result);
        Assert.True(result.IsFrozen);
        Assert.False(mutableBrush.IsFrozen);
    }

    [Fact]
    public void VisualizerSceneControl_FreezeForCrossThreadSnapshot_ReusesAlreadyFrozenBrush()
    {
        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "FreezeForCrossThreadSnapshot",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VisualizerSceneControl.FreezeForCrossThreadSnapshot not found.");
        SolidColorBrush frozenBrush = new(Colors.LimeGreen);
        frozenBrush.Freeze();

        object? result = method.Invoke(null, [frozenBrush]);

        Assert.Same(frozenBrush, result);
    }

    [Fact]
    public void VisualizerSceneControl_FreezeForCrossThreadSnapshot_RejectsNullBrush()
    {
        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "FreezeForCrossThreadSnapshot",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VisualizerSceneControl.FreezeForCrossThreadSnapshot not found.");

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [null]));

        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

    [Fact]
    public void VisualizerSceneControl_CreateFrozenBrush_ReturnsFrozenBrushWithRequestedColor()
    {
        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "CreateFrozenBrush",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VisualizerSceneControl.CreateFrozenBrush not found.");
        Color expectedColor = Color.FromArgb(0xCC, 0x12, 0x34, 0x56);

        SolidColorBrush brush = Assert.IsType<SolidColorBrush>(method.Invoke(null, [expectedColor]));

        Assert.True(brush.IsFrozen);
        Assert.Equal(expectedColor, brush.Color);
    }

    [Fact]
    public void VisualizerSceneControl_ResolveTimeZone_CachesLookupResultsForSchedulerTicks()
    {
        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "ResolveTimeZone",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VisualizerSceneControl.ResolveTimeZone not found.");
        FieldInfo cacheField = typeof(VisualizerSceneControl).GetField(
            "TimeZoneLookupCache",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VisualizerSceneControl.TimeZoneLookupCache not found.");

        object? cache = cacheField.GetValue(null);
        cache?.GetType().GetMethod("Clear")?.Invoke(cache, []);

        object? first = method.Invoke(null, ["Eastern Standard Time", "America/New_York"]);
        object? second = method.Invoke(null, ["Eastern Standard Time", "America/New_York"]);
        object? fallback = method.Invoke(null, ["Not/A/Real/Zone", "Eastern Standard Time"]);

        Assert.Same(first, second);
        Assert.IsType<TimeZoneInfo>(first);
        Assert.IsType<TimeZoneInfo>(fallback);
        int count = (int)(cache?.GetType().GetProperty("Count")?.GetValue(cache) ?? -1);
        Assert.Equal(2, count);
    }

    [Fact]
    public void VisualizerSceneControl_CreateFrozenPointCollection_PreservesAndFreezesMiniGraphPoints()
    {
        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "CreateFrozenPointCollection",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VisualizerSceneControl.CreateFrozenPointCollection not found.");
        Point[] points = [new(1.25, 2.5), new(3.75, 4.5), new(5.25, 6.5)];

        PointCollection collection = Assert.IsType<PointCollection>(method.Invoke(null, [points]));

        Assert.True(collection.IsFrozen);
        Assert.Equal(points.Length, collection.Count);
        for (int index = 0; index < points.Length; index++)
            Assert.Equal(points[index], collection[index]);
    }

    [Fact]
    public void VisualizerSceneControl_CreateFrozenPointCollection_ReusesAlreadyFrozenCollection()
    {
        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "CreateFrozenPointCollection",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VisualizerSceneControl.CreateFrozenPointCollection not found.");
        PointCollection frozenPoints = new([new Point(10, 20), new Point(30, 40)]);
        frozenPoints.Freeze();

        object? collection = method.Invoke(null, [frozenPoints]);

        Assert.Same(frozenPoints, collection);
    }

    [Fact]
    public void NetworkWaitingOverlay_DefinesOverlayTemplateAndBounceMotion()
    {
        string xaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml"));
        string codeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));

        Assert.Contains("x:Name=\"NetworkWaitingHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TitleText", xaml, StringComparison.Ordinal);
        Assert.Contains("DetailText", xaml, StringComparison.Ordinal);
        Assert.Contains("_networkWaitingViewModel.BounceWithinViewport = true;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Rect waitingBounds = GetWaitingBounds();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_motionController.Step(_networkWaitingViewModel, waitingBounds, elapsedSeconds);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ClampSpriteToBounds(_networkWaitingViewModel, waitingBounds);", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void BackgroundImages_StretchToScreenBoundsWithoutUniformCropping()
    {
        string xaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml"));
        XDocument document = XDocument.Parse(xaml);
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        string[] backgroundStretchValues = document.Descendants(wpf + "Image")
            .Where(element =>
                element.Attribute(x + "Name")?.Value is "BackgroundImageA" or "BackgroundImageB")
            .Select(element => element.Attribute("Stretch")?.Value ?? string.Empty)
            .Order()
            .ToArray();

        Assert.True(backgroundStretchValues.SequenceEqual(["Fill", "Fill"]), string.Join(", ", backgroundStretchValues));
        Assert.DoesNotContain("Stretch=\"UniformToFill\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyQuoteToGraph_OlderQuoteStillShowsCurrentValues()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new();
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

            FieldInfo latestQuotesField = typeof(VisualizerSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(VisualizerSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            Assert.True(graph.IsVisible);
            Assert.Equal("190.00", graph.LastText);
            Assert.Equal("+1.00%", graph.ChangeText);
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
    public void BackgroundTransition_PreloadsBitmap_AndKeepsSlowZoomLoopLightweight()
    {
        string codeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));

        Assert.Contains("private readonly DispatcherTimer _sceneTimer = new() { Interval = SceneSchedulerInterval };", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("_backgroundTransitionCompletionTimer", codeBehind, StringComparison.Ordinal);
        Assert.Contains("if (_backgroundZoomRunning && now >= _nextBackgroundZoomTickUtc)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("RunScheduledSceneAction(\"background-zoom\", StepBackgroundSlowZoom);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void StepBackgroundSlowZoom()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundTimerArmed\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundRotationChosen\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundTransitionComplete\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundZoomStarted\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("\"BackgroundZoomStopped\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void SetBackgroundZoomRunning(bool enabled, string reason)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("if (bitmap.CanFreeze)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private static async Task<byte[]?> PreloadBackgroundBytesAsync(string path, CancellationToken cancellationToken = default)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("byte[]? preloadedBytes = await PreloadBackgroundBytesAsync(backgroundPath, cancellationToken).ConfigureAwait(true);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BitmapImage backgroundBitmap = CreateBackgroundBitmap(backgroundPath, preloadedBytes);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("fileBitmap.StreamSource = memoryStream;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private int _backgroundTransitionGeneration;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("int transitionGeneration = ++_backgroundTransitionGeneration;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BackgroundTransitionSkipped", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private bool TryPromoteCommittedBackgroundSource()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private bool TryRecoverOrQueueActiveBackgroundSource()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("bool promotedCommittedSource = TryPromoteCommittedBackgroundSource();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("if (!promotedCommittedSource && TryRecoverOrQueueActiveBackgroundSource())", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BackgroundSourceRecovered", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void FinalizeBackgroundTransition(Image activeImage, Image standbyImage, ImageSource source)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void CanonicalizeBackgroundLayers(ImageSource source)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private ImageSource? _committedBackgroundSource;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_committedBackgroundSource = committedSource;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("activeImage.Source = committedSource;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("standbyImage.Source = standbySource;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CanonicalizeBackgroundLayers(incomingBitmap);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_activeBackgroundImage = activeImage;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_inactiveBackgroundImage = standbyImage;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private static ImageSource CreateStandbyBackgroundSource(ImageSource source)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private BitmapImage? _currentBackgroundBitmap;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private double _currentBackgroundOpacity = 0.45d;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_currentBackgroundBitmap = incomingBitmap;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private static double GetBackgroundPresentationOpacity(BitmapSource bitmap)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private bool _backgroundRecoveryReloadInFlight;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private CancellationTokenSource? _backgroundRecoveryReloadCancellation;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private int _backgroundRecoveryReloadGeneration;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("QueueBackgroundRecoveryReload(_currentBackgroundPath!);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("int recoveryGeneration = ++_backgroundRecoveryReloadGeneration;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private async Task ReloadBackgroundForRecoveryAsync(string path, CancellationTokenSource cancellation, int recoveryGeneration)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("await LoadBackgroundAsync(path, cancellation.Token).ConfigureAwait(true);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("if (recoveryGeneration == _backgroundRecoveryReloadGeneration)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void CancelBackgroundRecoveryReload()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BackgroundRecoveryReloadCanceled", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BackgroundRecoveryReloadCancelFailed", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("recoverySource = _currentBackgroundBitmap = CreateBackgroundBitmap(_currentBackgroundPath!);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_backgroundTransitionInFlight", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SetBackgroundZoomRunning(false, \"background-transitioning\");", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TimeSpan duration = TimeSpan.FromMilliseconds(450);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("AnimateBackgroundProperty(incoming, Image.OpacityProperty, 0d, _currentBackgroundOpacity, duration, ease);", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("AnimateBackgroundProperty(outgoing, Image.OpacityProperty", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("outgoing.Source = incoming.Source;", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("switch (_random.Next(3))", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("AnimateBackgroundTranslation(incoming", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("fileBitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("outgoing.Source = null;", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Defaults_UseFiveMinuteBackgroundRotationBaseline()
    {
        AppSettings settings = Defaults.CreateSettings();

        Assert.Equal(300, settings.BackgroundChangeSeconds);
        Assert.True(settings.ShuffleBackgrounds);
    }

    [Fact]
    public void UpdateTapeItem_StaleToLiveRecovery_TriggersFlash()
    {
        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "UpdateTapeItem",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VisualizerSceneControl.UpdateTapeItem not found.");

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
            VisualizerSceneControl control = new();
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

            FieldInfo latestQuotesField = typeof(VisualizerSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(VisualizerSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            Assert.Equal(1, graph.FlashSequence);
        });
    }

    [Fact]
    public void ApplyQuoteToGraph_StaleQuoteKeepsMoverVisibleUsingLastKnownData()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new();
            FloatingGraphViewModel graph = new()
            {
                Symbol = "AAPL",
                LastText = "190.00",
                ChangeText = "+1.00%",
                LatestSegmentPoints = [new Point(0, 12), new Point(10, 4)],
                IsVisible = true
            };

            Dictionary<string, QuoteSnapshot> quotes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = new QuoteSnapshot
                {
                    Symbol = "AAPL",
                    Last = 190m,
                    ChangePercent = 1m,
                    FetchTimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
                    IsStale = true
                }
            };

            FieldInfo latestQuotesField = typeof(VisualizerSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(VisualizerSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            Assert.True(graph.IsVisible);
            Assert.Equal("190.00", graph.LastText);
            Assert.Equal("+1.00%", graph.ChangeText);
            Assert.Same(Brushes.Goldenrod, graph.ChangeForeground);
            Assert.Same(Brushes.Goldenrod, graph.LatestSegmentBrush);
        });
    }

    [Fact]
    public void ApplyQuoteToGraph_PercentOnlyChange_DoesNotTriggerCardFlash()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new();
            FloatingGraphViewModel graph = new()
            {
                Symbol = "AAPL",
                LastText = "190.00",
                ChangeText = "+1.00%",
                RawLastValue = 190m,
                IsVisible = true
            };

            Dictionary<string, QuoteSnapshot> quotes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = new QuoteSnapshot
                {
                    Symbol = "AAPL",
                    Last = 190m,
                    ChangePercent = 1.25m,
                    FetchTimestampUtc = DateTimeOffset.UtcNow,
                    IsStale = false
                }
            };

            FieldInfo latestQuotesField = typeof(VisualizerSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(VisualizerSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            Assert.Equal(0, graph.FlashSequence);
            Assert.Equal("+1.25%", graph.ChangeText);
            Assert.Equal(190m, graph.RawLastValue);
        });
    }

    [Fact]
    public void ApplyQuoteToGraph_FirstLiveValueTriggersInitialCardFlash()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new();
            FloatingGraphViewModel graph = new()
            {
                Symbol = "AAPL",
                IsVisible = true
            };

            Dictionary<string, QuoteSnapshot> quotes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = new QuoteSnapshot
                {
                    Symbol = "AAPL",
                    Last = 190m,
                    ChangePercent = 1.25m,
                    FetchTimestampUtc = DateTimeOffset.UtcNow,
                    IsStale = false
                }
            };

            FieldInfo latestQuotesField = typeof(VisualizerSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(VisualizerSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            Assert.Equal(1, graph.FlashSequence);
            Assert.Equal(190m, graph.RawLastValue);
        });
    }

    [Fact]
    public void ApplyQuoteToGraph_AlignsLatestSegmentBrushWithLatestQuoteDirection()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new();
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

            FieldInfo latestQuotesField = typeof(VisualizerSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(VisualizerSceneControl).GetMethod(
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
            VisualizerSceneControl control = new()
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

            FieldInfo latestQuotesField = typeof(VisualizerSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(VisualizerSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            MethodInfo boundsMethod = typeof(VisualizerSceneControl).GetMethod(
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
            VisualizerSceneControl control = new()
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

            FieldInfo latestQuotesField = typeof(VisualizerSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(VisualizerSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            MethodInfo boundsMethod = typeof(VisualizerSceneControl).GetMethod(
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
    public void ApplyQuoteToGraph_SuppressedRebuildUpdatesValueWithoutStartingFlashTravel()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new()
            {
                Width = 800,
                Height = 600
            };
            control.Measure(new Size(800, 600));
            control.Arrange(new Rect(0, 0, 800, 600));
            control.UpdateLayout();

            FloatingGraphViewModel graph = new()
            {
                Symbol = "VOO",
                LastText = "687.00",
                ChangeText = "+0.50%",
                RawLastValue = 687m,
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
                ["VOO"] = new QuoteSnapshot
                {
                    Symbol = "VOO",
                    Last = 688.11m,
                    ChangePercent = 0.9833m,
                    FetchTimestampUtc = DateTimeOffset.UtcNow,
                    IsStale = false
                }
            };

            FieldInfo latestQuotesField = typeof(VisualizerSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            FieldInfo suppressField = typeof(VisualizerSceneControl).GetField(
                "_suppressGraphRefreshMotionCues",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_suppressGraphRefreshMotionCues field not found.");
            suppressField.SetValue(control, true);

            MethodInfo applyQuoteMethod = typeof(VisualizerSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            Assert.Equal(688.11m, graph.RawLastValue);
            Assert.Equal("688.11", graph.LastText);
            Assert.Equal("+0.98%", graph.ChangeText);
            Assert.Null(graph.RefreshTravelTargetY);
            Assert.False(graph.IsRefreshTravelFlashActive);
            Assert.Equal(0, graph.FlashSequence);
            Assert.Equal(-7d, graph.VelocityY);

            quotes["VOO"] = new QuoteSnapshot
            {
                Symbol = "VOO",
                Last = 689m,
                ChangePercent = 1.1m,
                FetchTimestampUtc = DateTimeOffset.UtcNow.AddSeconds(1),
                IsStale = false
            };
            suppressField.SetValue(control, false);

            applyQuoteMethod.Invoke(control, [graph]);

            Assert.Equal(689m, graph.RawLastValue);
            Assert.True(graph.IsRefreshTravelFlashActive);
            Assert.Equal(1, graph.FlashSequence);
            Assert.NotNull(graph.RefreshTravelTargetY);
            Assert.InRange(graph.RefreshTravelTargetY.Value, 0, Math.Max(0, control.ActualHeight - graph.Height));
        });
    }

    [Fact]
    public void ApplyQuoteToGraph_RawPriceChangeDuringTravel_DoesNotRetriggerSustainedFlash()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new()
            {
                Width = 800,
                Height = 600
            };
            control.Measure(new Size(800, 600));
            control.Arrange(new Rect(0, 0, 800, 600));
            control.UpdateLayout();

            DateTimeOffset originalStartedUtc = DateTimeOffset.UtcNow.AddSeconds(-2);
            FloatingGraphViewModel graph = new()
            {
                Symbol = "AAPL",
                LastText = "190.00",
                ChangeText = "+0.50%",
                RawLastValue = 190m,
                FlashSequence = 7,
                Width = 140,
                Height = 84,
                X = 240,
                Y = 260,
                VelocityY = -14,
                NominalVelocityY = -7,
                RefreshTravelTargetY = 0,
                RefreshTravelFlashStartedUtc = originalStartedUtc,
                IsRefreshTravelFlashActive = true,
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

            FieldInfo latestQuotesField = typeof(VisualizerSceneControl).GetField(
                "_latestQuotes",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_latestQuotes field not found.");
            latestQuotesField.SetValue(control, quotes);

            MethodInfo applyQuoteMethod = typeof(VisualizerSceneControl).GetMethod(
                "ApplyQuoteToGraph",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyQuoteToGraph method not found.");
            applyQuoteMethod.Invoke(control, [graph]);

            Assert.Equal(191m, graph.RawLastValue);
            Assert.Equal("191.00", graph.LastText);
            Assert.Equal(7, graph.FlashSequence);
            Assert.Equal(0, graph.RefreshTravelTargetY);
            Assert.True(graph.IsRefreshTravelFlashActive);
            Assert.Equal(originalStartedUtc, graph.RefreshTravelFlashStartedUtc);
            Assert.Equal(-14d, graph.VelocityY);
        });
    }

    [Fact]
    public void CopyMotion_PreservesGraphQuoteAndFlashStateAcrossModelReplacement()
    {
        DateTimeOffset started = DateTimeOffset.UtcNow.AddSeconds(-3);
        FloatingGraphViewModel source = new()
        {
            X = 123,
            Y = 234,
            VelocityX = 4,
            VelocityY = -8,
            NominalVelocityX = 2,
            NominalVelocityY = -5,
            RefreshTravelTargetY = 17,
            RawLastValue = 456.78m,
            QuoteUpdateToken = 99,
            FlashBrush = Brushes.LimeGreen,
            IsRefreshTravelFlashActive = true,
            RefreshTravelFlashStartedUtc = started
        };
        FloatingGraphViewModel target = new();

        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "CopyMotion",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CopyMotion method not found.");
        method.Invoke(null, [source, target]);

        Assert.Equal(source.X, target.X);
        Assert.Equal(source.Y, target.Y);
        Assert.Equal(source.VelocityX, target.VelocityX);
        Assert.Equal(source.VelocityY, target.VelocityY);
        Assert.Equal(source.NominalVelocityX, target.NominalVelocityX);
        Assert.Equal(source.NominalVelocityY, target.NominalVelocityY);
        Assert.Equal(source.RefreshTravelTargetY, target.RefreshTravelTargetY);
        Assert.Equal(source.RawLastValue, target.RawLastValue);
        Assert.Equal(source.QuoteUpdateToken, target.QuoteUpdateToken);
        Assert.Same(source.FlashBrush, target.FlashBrush);
        Assert.Equal(source.IsRefreshTravelFlashActive, target.IsRefreshTravelFlashActive);
        Assert.Equal(source.RefreshTravelFlashStartedUtc, target.RefreshTravelFlashStartedUtc);
    }

    [Fact]
    public void CopyMotion_ClearsInvalidGraphFlashStateWhenTargetIsMissing()
    {
        FloatingGraphViewModel source = new()
        {
            RefreshTravelTargetY = null,
            IsRefreshTravelFlashActive = true,
            RefreshTravelFlashStartedUtc = DateTimeOffset.UtcNow,
            RawLastValue = 456.78m,
            QuoteUpdateToken = 99,
            FlashBrush = Brushes.LimeGreen
        };
        FloatingGraphViewModel target = new()
        {
            RefreshTravelTargetY = 0,
            IsRefreshTravelFlashActive = true,
            RefreshTravelFlashStartedUtc = DateTimeOffset.UtcNow
        };

        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "CopyMotion",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CopyMotion method not found.");
        method.Invoke(null, [source, target]);

        Assert.Null(target.RefreshTravelTargetY);
        Assert.False(target.IsRefreshTravelFlashActive);
        Assert.Equal(DateTimeOffset.MinValue, target.RefreshTravelFlashStartedUtc);
        Assert.Equal(source.RawLastValue, target.RawLastValue);
        Assert.Equal(source.QuoteUpdateToken, target.QuoteUpdateToken);
        Assert.Same(source.FlashBrush, target.FlashBrush);
    }

    [Fact]
    public void SeedSpriteLayout_ClearsGraphFlashTravelStateWhenReseeding()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new();
            control.ApplyTemplate();
            control.Measure(new Size(1280, 720));
            control.Arrange(new Rect(0, 0, 1280, 720));
            control.UpdateLayout();

            FloatingGraphViewModel graph = new()
            {
                Symbol = "VOO",
                TapeName = "CORE",
                Width = 140,
                Height = 84,
                IsVisible = true,
                RefreshTravelTargetY = 0,
                IsRefreshTravelFlashActive = true,
                RefreshTravelFlashStartedUtc = DateTimeOffset.UtcNow
            };

            FieldInfo graphsField = typeof(VisualizerSceneControl).GetField(
                "_graphs",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_graphs field not found.");
            System.Collections.IList graphs = (System.Collections.IList)(graphsField.GetValue(control)
                ?? throw new InvalidOperationException("_graphs value not found."));
            graphs.Add(graph);

            MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
                "SeedSpriteLayout",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("SeedSpriteLayout method not found.");
            method.Invoke(control, [false]);

            Assert.Null(graph.RefreshTravelTargetY);
            Assert.False(graph.IsRefreshTravelFlashActive);
            Assert.Equal(DateTimeOffset.MinValue, graph.RefreshTravelFlashStartedUtc);
        });
    }

    [Fact]
    public void ResetInvalidGraphRefreshImpulseIfNeeded_ClearsMissingTargetFlashState()
    {
        FloatingGraphViewModel graph = new()
        {
            VelocityX = 260,
            VelocityY = -260,
            NominalVelocityX = 3,
            NominalVelocityY = -4,
            RefreshTravelTargetY = null,
            IsRefreshTravelFlashActive = true,
            RefreshTravelFlashStartedUtc = DateTimeOffset.UtcNow
        };

        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "ResetInvalidGraphRefreshImpulseIfNeeded",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResetInvalidGraphRefreshImpulseIfNeeded method not found.");
        object? reason = method.Invoke(null, [graph]);

        Assert.Equal("missing-target", reason);
        Assert.Null(graph.RefreshTravelTargetY);
        Assert.False(graph.IsRefreshTravelFlashActive);
        Assert.Equal(DateTimeOffset.MinValue, graph.RefreshTravelFlashStartedUtc);
        Assert.Equal(3, graph.VelocityX);
        Assert.Equal(-4, graph.VelocityY);
    }

    [Fact]
    public void FloatingGraphControl_DataContextChangeReconcilesSustainedFlashState()
    {
        RunOnSta(() =>
        {
            FloatingGraphControl control = new();
            control.ApplyTemplate();
            control.Measure(new Size(180, 120));
            control.Arrange(new Rect(0, 0, 180, 120));
            control.UpdateLayout();

            FloatingGraphViewModel activeGraph = new()
            {
                Width = 140,
                Height = 84,
                IsVisible = true,
                FlashBrush = Brushes.LimeGreen,
                IsRefreshTravelFlashActive = true
            };
            FloatingGraphViewModel inactiveGraph = new()
            {
                Width = 140,
                Height = 84,
                IsVisible = true,
                FlashBrush = Brushes.OrangeRed,
                IsRefreshTravelFlashActive = false
            };

            control.DataContext = activeGraph;
            Border rootBorder = Assert.IsType<Border>(control.FindName("RootBorder"));
            SolidColorBrush activeBrush = Assert.IsType<SolidColorBrush>(rootBorder.Background);
            Assert.True(activeBrush.HasAnimatedProperties);

            control.DataContext = inactiveGraph;

            SolidColorBrush inactiveBrush = Assert.IsType<SolidColorBrush>(rootBorder.Background);
            Assert.False(inactiveBrush.HasAnimatedProperties);
            Assert.Equal(Color.FromArgb(0x7A, 0x0D, 0x13, 0x1B), inactiveBrush.Color);
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

            MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
                "ResetGraphRefreshImpulseIfNeeded",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("ResetGraphRefreshImpulseIfNeeded method not found.");

            string? reason = Assert.IsType<string>(method.Invoke(null, [graph, new Rect(0, 0, 800, 500)]));

            Assert.Null(graph.RefreshTravelTargetY);
            Assert.False(graph.IsRefreshTravelFlashActive);
            Assert.Equal(7d, graph.VelocityY);
            Assert.Equal("top-boundary", reason);
        });
    }

    [Fact]
    public void ApplyGraphRefreshImpulse_UsesFastEdgeSeekingVelocity()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new();
            FloatingGraphViewModel graph = new()
            {
                Height = 84,
                Y = 420,
                VelocityY = 8,
                NominalVelocityY = 8,
                RefreshTravelTargetY = 0,
                IsRefreshTravelFlashActive = true
            };

            MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
                "ApplyGraphRefreshImpulse",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplyGraphRefreshImpulse method not found.");

            method.Invoke(control, [graph, new Rect(0, 0, 800, 500)]);

            Assert.Equal(0d, graph.VelocityX);
            Assert.True(graph.VelocityY <= -260d);
            Assert.Equal(8d, graph.NominalVelocityY);
        });
    }

    [Fact]
    public void ResetGraphRefreshImpulseIfNeeded_ClearsSustainedFlashAfterTimeout()
    {
        RunOnSta(() =>
        {
            FloatingGraphViewModel graph = new()
            {
                Height = 84,
                Y = 240,
                VelocityY = -14,
                NominalVelocityY = -7,
                RefreshTravelTargetY = 0,
                IsRefreshTravelFlashActive = true,
                RefreshTravelFlashStartedUtc = DateTimeOffset.UtcNow.AddSeconds(-30)
            };

            MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
                "ResetGraphRefreshImpulseIfNeeded",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("ResetGraphRefreshImpulseIfNeeded method not found.");

            string? reason = Assert.IsType<string>(method.Invoke(null, [graph, new Rect(0, 0, 800, 500)]));

            Assert.Null(graph.RefreshTravelTargetY);
            Assert.False(graph.IsRefreshTravelFlashActive);
            Assert.Equal(-7d, graph.VelocityY);
            Assert.Equal("timeout", reason);
        });
    }

    [Fact]
    public void UpdateTapeItem_SameValueWithNewerFetchToken_DoesNotTriggerFlash()
    {
        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "UpdateTapeItem",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VisualizerSceneControl.UpdateTapeItem not found.");

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

        Assert.Equal(0, target.UpdateSequence);
        Assert.Equal(200, target.QuoteUpdateToken);
    }

    [Fact]
    public void MergeQuotes_PreservesCachedSymbolsWhileApplyingWarmupBatch()
    {
        MethodInfo mergeMethod = typeof(VisualizerSceneControl).GetMethod(
            "MergeQuotes",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("VisualizerSceneControl.MergeQuotes not found.");

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
    public void VisualizerLayout_UsesGlobalMarketsTapeAndWaitingBounds()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));
        string sceneXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml"));
        string globalTapeXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "GlobalMarketsTapeControl.xaml"));

        Assert.Contains("private Rect GetWaitingBounds()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("Rect waitingBounds = GetWaitingBounds();", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_motionController.Step(_networkWaitingViewModel, waitingBounds, elapsedSeconds);", sceneCodeBehind, StringComparison.Ordinal);
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
            "VisualizerSceneControl.xaml.cs"));

        Assert.DoesNotContain("_networkWaitingViewModel.TitleText = \"Loading live market data\";", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void MotionTick_AvoidsGlobalClampAndLinqAllocationInFrameLoop()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));

        int methodStart = sceneCodeBehind.IndexOf("private void StepMotion()", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "StepMotion method not found.");
        int nextMethodStart = sceneCodeBehind.IndexOf("private IEnumerable<FloatingGraphViewModel> EnumerateVisibleGraphCards()", methodStart, StringComparison.Ordinal);
        Assert.True(nextMethodStart > methodStart, "StepMotion method boundary not found.");
        string body = sceneCodeBehind[methodStart..nextMethodStart];

        Assert.Contains("for (int i = 0; i < _graphs.Count && visibleGraphCount < MaxVisibleGraphCards; i++)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnumerateVisibleGraphCards()", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ClampSpritesToSafeBounds();", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"if\s*\(\s*EnableMarketCritters\s*\)\s*if\s*\(\s*EnableMarketCritters\s*\)", body);
    }

    [Fact]
    public void MotionTick_KeepsVisibleNonBouncingGraphsInsideBoundsWithoutGlobalClampPass()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new();
            Window? host = null;

            try
            {
                host = HostControlForLayout(control, 800, 600);
                MethodInfo boundsMethod = typeof(VisualizerSceneControl).GetMethod(
                    "GetGraphMotionBounds",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("GetGraphMotionBounds method not found.");
                MethodInfo stepMotion = typeof(VisualizerSceneControl).GetMethod(
                    "StepMotion",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("StepMotion method not found.");
                FieldInfo graphsField = typeof(VisualizerSceneControl).GetField(
                    "_graphs",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("_graphs field not found.");
                FieldInfo lastMotionTickField = typeof(VisualizerSceneControl).GetField(
                    "_lastMotionTick",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("_lastMotionTick field not found.");

                Rect bounds = Assert.IsType<Rect>(boundsMethod.Invoke(control, []));
                FloatingGraphViewModel graph = new()
                {
                    Symbol = "AAPL",
                    Width = 140,
                    Height = 84,
                    X = bounds.Right + 120,
                    Y = bounds.Bottom + 120,
                    VelocityX = 500,
                    VelocityY = 500,
                    IsVisible = true,
                    BounceWithinViewport = false
                };

                var graphs = Assert.IsType<System.Collections.ObjectModel.ObservableCollection<FloatingGraphViewModel>>(graphsField.GetValue(control));
                graphs.Add(graph);
                lastMotionTickField.SetValue(control, DateTime.UtcNow - TimeSpan.FromSeconds(1));

                stepMotion.Invoke(control, []);

                Assert.InRange(graph.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - graph.Width));
                Assert.InRange(graph.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - graph.Height));
                Assert.True(graph.VelocityX > 0);
                Assert.True(graph.VelocityY > 0);
            }
            finally
            {
                host?.Close();
            }
        });
    }

    [Fact]
    public void GraphMotionBounds_ReturnSceneWideSafeInset()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new();
            Window? host = null;

            try
            {
                host = HostControlForLayout(control, 800, 600);
                MethodInfo boundsMethod = typeof(VisualizerSceneControl).GetMethod(
                    "GetGraphMotionBounds",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("GetGraphMotionBounds method not found.");

                Rect bounds = Assert.IsType<Rect>(boundsMethod.Invoke(control, []));

                Assert.Equal(12, bounds.Left);
                Assert.Equal(12, bounds.Top);
                Assert.True(bounds.Width >= 760);
                Assert.True(bounds.Height >= 540);
                Assert.True(bounds.Right <= 800);
                Assert.True(bounds.Bottom <= 600);
            }
            finally
            {
                host?.Close();
            }
        });
    }

    [Fact]
    public void MarketBagSpriteMotion_ClampsBagsInsideMarketSpriteBounds()
    {
        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "StepMarketBagSprite",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("StepMarketBagSprite method not found.");
        MarketSpriteViewModel bag = new()
        {
            Width = 40,
            Height = 40,
            X = 300,
            BaseY = 300,
            VelocityX = 80
        };
        Rect bounds = new(10, 20, 200, 120);

        method.Invoke(null, [bag, bounds, 1d, 1d]);

        Assert.Equal(bounds.Right - bag.Width, bag.X);
        Assert.InRange(bag.Y, bounds.Top, bounds.Bottom - bag.Height);
        Assert.True(bag.VelocityX < 0);
    }

    [Fact]
    public void MarketCritterMotion_ClampsCrittersInsideMarketSpriteBounds()
    {
        MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
            "StepCritterChase",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("StepCritterChase method not found.");
        MarketSpriteViewModel critter = new()
        {
            Width = 40,
            Height = 40,
            X = 300,
            Y = 300
        };
        MarketSpriteViewModel target = new()
        {
            Width = 40,
            Height = 40,
            X = 600,
            Y = 600
        };
        Rect bounds = new(10, 20, 200, 120);

        method.Invoke(null, [critter, target, bounds, 10d, 500d]);

        Assert.InRange(critter.X, bounds.Left, bounds.Right - critter.Width);
        Assert.InRange(critter.Y, bounds.Top, bounds.Bottom - critter.Height);
    }

    [Fact]
    public void RuntimeQuoteScheduler_IgnoresLegacyRefreshSlidersAndDispatchesEverySecond()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new();
            MethodInfo configureTimers = typeof(VisualizerSceneControl).GetMethod(
                "ConfigureTimers",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ConfigureTimers method not found.");
            MethodInfo stopLiveTimers = typeof(VisualizerSceneControl).GetMethod(
                "StopLiveTimers",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("StopLiveTimers method not found.");
            MethodInfo sceneSchedulerTick = typeof(VisualizerSceneControl).GetMethod(
                "OnSceneSchedulerTick",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("OnSceneSchedulerTick method not found.");
            FieldInfo settingsField = typeof(VisualizerSceneControl).GetField(
                "_settings",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_settings field not found.");
            FieldInfo sceneTimerField = typeof(VisualizerSceneControl).GetField(
                "_sceneTimer",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_sceneTimer field not found.");
            FieldInfo nextRuntimeQuoteTickField = typeof(VisualizerSceneControl).GetField(
                "_nextRuntimeQuoteTickUtc",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_nextRuntimeQuoteTickUtc field not found.");
            FieldInfo nextClockTickField = typeof(VisualizerSceneControl).GetField(
                "_nextClockTickUtc",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_nextClockTickUtc field not found.");

            settingsField.SetValue(control, new AppSettings
            {
                RefreshSecondsPortfolio = 1200,
                RefreshSecondsOffHours = 1200,
                ClockRefreshSeconds = 1
            });

            try
            {
                DateTimeOffset beforeConfigure = DateTimeOffset.UtcNow;
                configureTimers.Invoke(control, []);
                DateTimeOffset afterConfigure = DateTimeOffset.UtcNow;
                DispatcherTimer sceneTimer = Assert.IsType<DispatcherTimer>(sceneTimerField.GetValue(control));
                DateTimeOffset nextRuntimeQuoteTick = Assert.IsType<DateTimeOffset>(nextRuntimeQuoteTickField.GetValue(control));
                Assert.Equal(TimeSpan.FromMilliseconds(33), sceneTimer.Interval);
                Assert.InRange(nextRuntimeQuoteTick - beforeConfigure, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(2000));
                Assert.InRange(nextRuntimeQuoteTick - afterConfigure, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(2000));

                nextClockTickField.SetValue(control, DateTimeOffset.MaxValue);
                nextRuntimeQuoteTickField.SetValue(control, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(1));
                sceneSchedulerTick.Invoke(control, [null, EventArgs.Empty]);
                DateTimeOffset nextRuntimeQuoteTickAfterScheduler = Assert.IsType<DateTimeOffset>(nextRuntimeQuoteTickField.GetValue(control));
                Assert.True(nextRuntimeQuoteTickAfterScheduler > DateTimeOffset.UtcNow);
            }
            finally
            {
                stopLiveTimers.Invoke(control, []);
            }
        });

        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));

        Assert.DoesNotContain("private double GetRefreshSeconds()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("_refreshTimer.Interval = TimeSpan.FromSeconds(GetRefreshSeconds())", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusFreshness_IsRecomputedFromLatestQuoteTimestamps()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));

        Assert.Contains("UpdateStatusFreshnessText();", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void UpdateStatusFreshnessText()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Loading initial values", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_statusViewModel.UpdatedPrefixText = \"Last Updated:\";", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_statusViewModel.UpdatedTickerFieldText = StartupCoordinator.FormatUpdatedTickerField", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_statusViewModel.UpdatedTickerFieldForeground = StartupCoordinator.ResolveUpdatedTickerFieldBrush", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_statusViewModel.DataFreshnessText = StartupCoordinator.ResolveDataFreshnessText", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RuntimeQuoteOfflineDisplayFailureThreshold = 1", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RuntimeQuoteTransportRecoveryFailureThreshold = 10", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RuntimeDataFreshnessChanged", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ResolveEffectiveDataFreshnessNetworkState", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("GetCachedStatusNetworkAvailability()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RefreshCachedStatusNetworkAvailabilityAsync()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("failure_counted", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("UpdateDataFreshnessStatus();", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"data_freshness_text\"", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("StartupCoordinator.TryGetLatestUpdatedSymbol(_latestQuotes, out string latestUpdatedSymbol, out DateTimeOffset latestUpdatedFetchUtc)", sceneCodeBehind, StringComparison.Ordinal);

        int updateMethodStart = sceneCodeBehind.IndexOf("private void UpdateDataFreshnessStatus()", StringComparison.Ordinal);
        Assert.True(updateMethodStart >= 0);
        int updateMethodEnd = sceneCodeBehind.IndexOf("private bool GetCachedStatusNetworkAvailability()", updateMethodStart, StringComparison.Ordinal);
        Assert.True(updateMethodEnd > updateMethodStart);
        string updateMethodBody = sceneCodeBehind[updateMethodStart..updateMethodEnd];
        Assert.DoesNotContain("_networkAvailabilityService.IsNetworkAvailable()", updateMethodBody, StringComparison.Ordinal);

        string statusBarXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "StatusBarControl.xaml"));
        Assert.Contains("DataFreshnessText", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("DataFreshnessForeground", statusBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeQuoteScheduler_SlowsRecentlyFetchedClosedWorldMarketSymbolsOnly()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new();
            FloatingClockViewModel clock = new();
            clock.Cities.Add(new ClockCityViewModel
            {
                Key = "Riyadh",
                ExchangeSymbol = "^TASI.SR",
                ShowExchangeDetails = true
            });

            typeof(VisualizerSceneControl)
                .GetField("_clockViewModel", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(control, clock);
            MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
                "IsRuntimeQuoteRefreshSuppressedForClosedClockMarket",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("VisualizerSceneControl.IsRuntimeQuoteRefreshSuppressedForClosedClockMarket not found.");
            DateTimeOffset now = DateTimeOffset.UtcNow;

            typeof(VisualizerSceneControl)
                .GetField("_latestQuotes", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(control, new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    ["^TASI.SR"] = new()
                    {
                        Symbol = "^TASI.SR",
                        MarketSession = MarketSession.Closed,
                        FetchTimestampUtc = now - TimeSpan.FromMinutes(2)
                    },
                    ["VOO"] = new()
                    {
                        Symbol = "VOO",
                        MarketSession = MarketSession.Closed,
                        FetchTimestampUtc = now - TimeSpan.FromMinutes(2)
                    }
                });

            Assert.True(Assert.IsType<bool>(method.Invoke(control, ["^TASI.SR", now])));
            Assert.False(Assert.IsType<bool>(method.Invoke(control, ["VOO", now])));

            typeof(VisualizerSceneControl)
                .GetField("_latestQuotes", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(control, new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    ["^TASI.SR"] = new()
                    {
                        Symbol = "^TASI.SR",
                        MarketSession = MarketSession.Closed,
                        FetchTimestampUtc = now - TimeSpan.FromMinutes(11)
                    }
                });

            Assert.False(Assert.IsType<bool>(method.Invoke(control, ["^TASI.SR", now])));
        });
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
        Assert.Contains("TextWrapping=\"Wrap\"", newsXaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"{Binding ElementName=ViewportHost, Path=ActualWidth}\"", newsXaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"38\"", newsXaml, StringComparison.Ordinal);
        Assert.Contains("LineStackingStrategy=\"BlockLineHeight\"", newsXaml, StringComparison.Ordinal);
        Assert.Contains("FormatHeadline", newsCode, StringComparison.Ordinal);
        Assert.Contains("StartHeadlinePreparation", newsCode, StringComparison.Ordinal);
        Assert.Contains("PrepareHeadlineAsync", newsCode, StringComparison.Ordinal);
        Assert.Contains("Task.Run(", newsCode, StringComparison.Ordinal);
        Assert.Contains("CancelHeadlinePreparation", newsCode, StringComparison.Ordinal);
        Assert.Contains("_headlinePreparationTask", newsCode, StringComparison.Ordinal);
        Assert.Contains("generation != _headlinePreparationGeneration", newsCode, StringComparison.Ordinal);
        Assert.Contains("private readonly object _headlineWidthCacheGate = new();", newsCode, StringComparison.Ordinal);
        Assert.Contains("BuildPreparedHeadline(activeText, context, _headlineWidthCache, _headlineWidthCacheGate, cancellation)", newsCode, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource", newsCode, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested();", newsCode, StringComparison.Ordinal);
        Assert.Contains("lock (_headlineWidthCacheGate)", newsCode, StringComparison.Ordinal);
        Assert.Contains("normalized.Replace(\"\\r\\n\", \"\\n\", StringComparison.Ordinal).Replace('\\r', '\\n')", newsCode, StringComparison.Ordinal);
        Assert.Contains("Regex.Replace(line, @\"[\\u0000-\\u0009\\u000B-\\u001F\\u007F]+\", \" \")", newsCode, StringComparison.Ordinal);
        Assert.Contains("Regex.Replace(line, @\"[ \\t]+\", \" \")", newsCode, StringComparison.Ordinal);
        Assert.Contains("PlaybackPhase.Typing", newsCode, StringComparison.Ordinal);
        Assert.Contains("PlaybackPhase.Scrolling", newsCode, StringComparison.Ordinal);
        Assert.Contains("PlaybackPhase.PauseAfterScroll", newsCode, StringComparison.Ordinal);
        Assert.Contains("PlaybackPhase.PauseBetweenHeadlines", newsCode, StringComparison.Ordinal);
        Assert.Contains("PlaybackPhase.AdvanceHeadline", newsCode, StringComparison.Ordinal);
        Assert.Contains("DefaultBetweenHeadlinePauseSeconds = 1.6d", newsCode, StringComparison.Ordinal);
        Assert.Contains("TeleprinterCursor", newsCode, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetTop(ActiveHeadlineBlock, _currentVerticalOffset);", newsCode, StringComparison.Ordinal);
        Assert.Contains("MeasureHeadlineHeight", newsCode, StringComparison.Ordinal);
        Assert.Contains("private bool IsViewportReady()", newsCode, StringComparison.Ordinal);
        Assert.Contains("ViewportHost?.ActualWidth ?? 0d", newsCode, StringComparison.Ordinal);
        Assert.Contains("ViewportHost?.ActualHeight ?? 0d", newsCode, StringComparison.Ordinal);
        Assert.Contains("double.IsFinite(width)", newsCode, StringComparison.Ordinal);
        Assert.Contains("double.IsFinite(height)", newsCode, StringComparison.Ordinal);
        Assert.Contains("RestartPlaybackForViewportChange", newsCode, StringComparison.Ordinal);
        Assert.Contains("LayoutUpdated += OnLayoutUpdated;", newsCode, StringComparison.Ordinal);
        Assert.Contains("LayoutUpdated -= OnLayoutUpdated;", newsCode, StringComparison.Ordinal);
        Assert.Contains("RecoverPlaybackWhenViewportReady", newsCode, StringComparison.Ordinal);
        Assert.Contains("RefreshDebounceInterval = TimeSpan.FromMilliseconds(500)", newsCode, StringComparison.Ordinal);
        Assert.Contains("private readonly DispatcherTimer _refreshDebounceTimer = new() { Interval = RefreshDebounceInterval };", newsCode, StringComparison.Ordinal);
        Assert.Contains("_refreshDebounceTimer.Tick += OnRefreshDebounceElapsed;", newsCode, StringComparison.Ordinal);
        Assert.Contains("private void OnRefreshDebounceElapsed", newsCode, StringComparison.Ordinal);
        Assert.Contains("RequestRefresh(resetToFirstHeadline: false)", newsCode, StringComparison.Ordinal);
        Assert.Contains("RequestRefresh(resetToFirstHeadline: true)", newsCode, StringComparison.Ordinal);
        Assert.Contains("ResetPlaybackCore(preserveHeadlineIndex)", newsCode, StringComparison.Ordinal);
        Assert.Contains("if (_isUnloaded)", newsCode, StringComparison.Ordinal);
        Assert.Contains("_refreshDebounceTimer.Stop();", newsCode, StringComparison.Ordinal);
        Assert.Contains("if (!IsViewportReady())", newsCode, StringComparison.Ordinal);
        Assert.Contains("double GetSafeViewportWidth()", newsCode, StringComparison.Ordinal);
        Assert.Contains("PausePlaybackUntilViewportReady", newsCode, StringComparison.Ordinal);
        Assert.Contains("ResumePlaybackWhenViewportReady", newsCode, StringComparison.Ordinal);
        Assert.Contains("_playbackTimer.Stop();", newsCode, StringComparison.Ordinal);
        Assert.Contains("_headlineWidthCache", newsCode, StringComparison.Ordinal);
        Assert.Contains("MeasurementCacheKey", newsCode, StringComparison.Ordinal);
        Assert.Contains("MaxWidthMeasurementCacheEntries = 256", newsCode, StringComparison.Ordinal);
        Assert.Contains("TelegraphVerticalScrollPixelsPerSecond = 42d", newsCode, StringComparison.Ordinal);
        Assert.Contains("TypewriterCharactersPerTick = 2", newsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(NewsFlasherViewModel.MarqueeText)", newsCode, StringComparison.Ordinal);
    }

    [Fact]
    public void NewsFlasherControl_HeadlineBurstDebounceStopsPlaybackThenRestartsOnce()
    {
        RunOnSta(() =>
        {
            NewsFlasherViewModel viewModel = new() { Speed = 1d };
            viewModel.Headlines.Add(new NewsHeadlineViewModel { Text = "Initial financial news item." });
            viewModel.Headlines.Add(new NewsHeadlineViewModel { Text = "Second financial news item." });

            NewsFlasherControl control = new()
            {
                DataContext = viewModel,
                Width = 520,
                Height = 54
            };
            Window? host = null;

            try
            {
                host = HostControlForLayout(control, 520, 54);
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                DispatcherTimer playbackTimer = (DispatcherTimer)(typeof(NewsFlasherControl)
                    .GetField("_playbackTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(control) ?? throw new InvalidOperationException("NewsFlasherControl._playbackTimer not found."));
                DispatcherTimer refreshDebounceTimer = (DispatcherTimer)(typeof(NewsFlasherControl)
                    .GetField("_refreshDebounceTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(control) ?? throw new InvalidOperationException("NewsFlasherControl._refreshDebounceTimer not found."));
                MethodInfo debounceElapsedMethod = typeof(NewsFlasherControl).GetMethod("OnRefreshDebounceElapsed", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("NewsFlasherControl.OnRefreshDebounceElapsed not found.");
                FieldInfo headlineIndexField = typeof(NewsFlasherControl).GetField("_headlineIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("NewsFlasherControl._headlineIndex not found.");
                FieldInfo phaseField = typeof(NewsFlasherControl).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("NewsFlasherControl._phase not found.");
                object typingPhase = Enum.Parse(phaseField.FieldType, "Typing");

                Assert.True(playbackTimer.IsEnabled);

                phaseField.SetValue(control, typingPhase);
                viewModel.Headlines.Add(new NewsHeadlineViewModel { Text = "Replacement financial news item after burst." });

                Assert.False(playbackTimer.IsEnabled);
                Assert.True(refreshDebounceTimer.IsEnabled);

                debounceElapsedMethod.Invoke(control, [control, EventArgs.Empty]);

                Assert.False(refreshDebounceTimer.IsEnabled);
                Assert.True(playbackTimer.IsEnabled);
                Assert.Equal(0, Assert.IsType<int>(headlineIndexField.GetValue(control)));

                headlineIndexField.SetValue(control, 1);
                phaseField.SetValue(control, typingPhase);
                viewModel.Speed = 1.5d;

                Assert.False(playbackTimer.IsEnabled);
                Assert.True(refreshDebounceTimer.IsEnabled);

                debounceElapsedMethod.Invoke(control, [control, EventArgs.Empty]);

                Assert.False(refreshDebounceTimer.IsEnabled);
                Assert.True(playbackTimer.IsEnabled);
                Assert.Equal(1, Assert.IsType<int>(headlineIndexField.GetValue(control)));
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                host?.Close();
            }
        });
    }

    [Fact]
    public void NewsFlasherControl_PendingDebounceAfterUnloadDoesNotRestartPlayback()
    {
        RunOnSta(() =>
        {
            NewsFlasherViewModel viewModel = new() { Speed = 1d };
            viewModel.Headlines.Add(new NewsHeadlineViewModel { Text = "Initial financial news item." });

            NewsFlasherControl control = new()
            {
                DataContext = viewModel,
                Width = 520,
                Height = 54
            };
            Window? host = null;

            try
            {
                host = HostControlForLayout(control, 520, 54);
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                DispatcherTimer playbackTimer = (DispatcherTimer)(typeof(NewsFlasherControl)
                    .GetField("_playbackTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(control) ?? throw new InvalidOperationException("NewsFlasherControl._playbackTimer not found."));
                DispatcherTimer refreshDebounceTimer = (DispatcherTimer)(typeof(NewsFlasherControl)
                    .GetField("_refreshDebounceTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(control) ?? throw new InvalidOperationException("NewsFlasherControl._refreshDebounceTimer not found."));
                MethodInfo debounceElapsedMethod = typeof(NewsFlasherControl).GetMethod("OnRefreshDebounceElapsed", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("NewsFlasherControl.OnRefreshDebounceElapsed not found.");
                FieldInfo phaseField = typeof(NewsFlasherControl).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("NewsFlasherControl._phase not found.");
                object typingPhase = Enum.Parse(phaseField.FieldType, "Typing");

                phaseField.SetValue(control, typingPhase);
                viewModel.Headlines.Add(new NewsHeadlineViewModel { Text = "Replacement financial news item after burst." });

                Assert.True(refreshDebounceTimer.IsEnabled);

                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                debounceElapsedMethod.Invoke(control, [control, EventArgs.Empty]);

                Assert.False(refreshDebounceTimer.IsEnabled);
                Assert.False(playbackTimer.IsEnabled);
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                host?.Close();
            }
        });
    }

    [Fact]
    public void NewsFlasherControl_IdleRefreshPreservesIndexForSpeedAndResetsForHeadlineChanges()
    {
        RunOnSta(() =>
        {
            NewsFlasherViewModel viewModel = new() { Speed = 1d };
            viewModel.Headlines.Add(new NewsHeadlineViewModel { Text = "Initial financial news item." });
            viewModel.Headlines.Add(new NewsHeadlineViewModel { Text = "Second financial news item." });

            NewsFlasherControl control = new()
            {
                DataContext = viewModel,
                Width = 520,
                Height = 54
            };
            Window? host = null;

            try
            {
                host = HostControlForLayout(control, 520, 54);
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                DispatcherTimer refreshDebounceTimer = (DispatcherTimer)(typeof(NewsFlasherControl)
                    .GetField("_refreshDebounceTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(control) ?? throw new InvalidOperationException("NewsFlasherControl._refreshDebounceTimer not found."));
                FieldInfo headlineIndexField = typeof(NewsFlasherControl).GetField("_headlineIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("NewsFlasherControl._headlineIndex not found.");

                headlineIndexField.SetValue(control, 1);
                viewModel.Speed = 1.25d;

                Assert.False(refreshDebounceTimer.IsEnabled);
                Assert.Equal(1, Assert.IsType<int>(headlineIndexField.GetValue(control)));

                viewModel.Headlines.Add(new NewsHeadlineViewModel { Text = "Replacement financial news item after idle refresh." });

                Assert.False(refreshDebounceTimer.IsEnabled);
                Assert.Equal(0, Assert.IsType<int>(headlineIndexField.GetValue(control)));
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                host?.Close();
            }
        });
    }

    [Fact]
    public void NewsFlasherControl_PreLayoutPlaybackTickDoesNotThrow()
    {
        RunOnSta(() =>
        {
            NewsFlasherControl control = new();
            NewsFlasherViewModel viewModel = new()
            {
                Speed = 1d
            };
            viewModel.Headlines.Add(new NewsHeadlineViewModel
            {
                Text = "A pre-layout financial headline should wait for a real viewport before text measurement begins."
            });

            MethodInfo subscribeMethod = typeof(NewsFlasherControl).GetMethod("SubscribeToFlasher", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.SubscribeToFlasher not found.");
            MethodInfo tickMethod = typeof(NewsFlasherControl).GetMethod("OnPlaybackTick", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.OnPlaybackTick not found.");
            FieldInfo awaitingViewportField = typeof(NewsFlasherControl).GetField("_awaitingViewport", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._awaitingViewport not found.");

            subscribeMethod.Invoke(control, [viewModel]);
            tickMethod.Invoke(control, [null, EventArgs.Empty]);
            Assert.True((bool)(awaitingViewportField.GetValue(control) ?? false));

            Window? host = null;
            try
            {
                control.Width = 420;
                control.Height = 54;
                host = HostControlForLayout(control, 500, 120);

                TextBlock activeHeadlineBlock = (TextBlock)(control.FindName("ActiveHeadlineBlock")
                    ?? throw new InvalidOperationException("ActiveHeadlineBlock not found."));
                Assert.NotNull(activeHeadlineBlock);
                tickMethod.Invoke(control, [null, EventArgs.Empty]);
                PumpDispatcherUntil(() => !(bool)(awaitingViewportField.GetValue(control) ?? true) &&
                                           !string.IsNullOrWhiteSpace(activeHeadlineBlock.Text), TimeSpan.FromSeconds(1));
                Assert.False((bool)(awaitingViewportField.GetValue(control) ?? true));
                Assert.False(string.IsNullOrWhiteSpace(activeHeadlineBlock.Text));
            }
            finally
            {
                host?.Close();
            }
        });
    }

    [Fact]
    public void NewsFlasherControl_ResumesPlaybackWhenViewportBecomesReady()
    {
        RunOnSta(() =>
        {
            NewsFlasherControl control = new()
            {
                Width = 0,
                Height = 0
            };
            NewsFlasherViewModel viewModel = new()
            {
                Speed = 1d
            };
            viewModel.Headlines.Add(new NewsHeadlineViewModel
            {
                Text = "A zero-size news flasher should sleep quietly, then resume when real layout arrives."
            });

            MethodInfo tickMethod = typeof(NewsFlasherControl).GetMethod("OnPlaybackTick", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.OnPlaybackTick not found.");
            FieldInfo awaitingViewportField = typeof(NewsFlasherControl).GetField("_awaitingViewport", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._awaitingViewport not found.");
            FieldInfo timerField = typeof(NewsFlasherControl).GetField("_playbackTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._playbackTimer not found.");
            DispatcherTimer playbackTimer = (DispatcherTimer)(timerField.GetValue(control)
                ?? throw new InvalidOperationException("NewsFlasherControl._playbackTimer was null."));

            Window? host = null;
            try
            {
                control.DataContext = viewModel;
                host = HostControlForLayout(control, 120, 80);

                tickMethod.Invoke(control, [null, EventArgs.Empty]);
                Assert.True((bool)(awaitingViewportField.GetValue(control) ?? false));
                Assert.False(playbackTimer.IsEnabled);

                control.Width = 420;
                control.Height = 54;
                host.Width = 500;
                host.Height = 120;
                control.Measure(new Size(420, 54));
                control.Arrange(new Rect(0, 0, 420, 54));
                control.ApplyTemplate();
                host.UpdateLayout();
                control.UpdateLayout();

                Assert.False((bool)(awaitingViewportField.GetValue(control) ?? true));
                Assert.True(playbackTimer.IsEnabled);
            }
            finally
            {
                host?.Close();
            }
        });
    }

    [Fact]
    public void NewsFlasherControl_ViewportRecoveryRestartsCurrentHeadline()
    {
        RunOnSta(() =>
        {
            NewsFlasherControl control = new()
            {
                Width = 420,
                Height = 54
            };

            FieldInfo awaitingViewportField = typeof(NewsFlasherControl).GetField("_awaitingViewport", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._awaitingViewport not found.");
            FieldInfo pauseTicksField = typeof(NewsFlasherControl).GetField("_pauseTicksRemaining", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._pauseTicksRemaining not found.");
            FieldInfo activeTextField = typeof(NewsFlasherControl).GetField("_activeText", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._activeText not found.");
            FieldInfo segmentIndexField = typeof(NewsFlasherControl).GetField("_segmentIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._segmentIndex not found.");
            MethodInfo recoverMethod = typeof(NewsFlasherControl).GetMethod("RecoverPlaybackWhenViewportReady", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.RecoverPlaybackWhenViewportReady not found.");

            Window? host = null;
            try
            {
                host = HostControlForLayout(control, 500, 120);
                awaitingViewportField.SetValue(control, true);
                pauseTicksField.SetValue(control, 7);
                segmentIndexField.SetValue(control, 2);
                activeTextField.SetValue(control, "This active headline should restart after viewport recovery.");

                recoverMethod.Invoke(control, []);

                Assert.False((bool)(awaitingViewportField.GetValue(control) ?? true));
                Assert.Equal(0, Assert.IsType<int>(pauseTicksField.GetValue(control)));
                Assert.Equal(0, Assert.IsType<int>(segmentIndexField.GetValue(control)));
            }
            finally
            {
                host?.Close();
            }
        });
    }

    [Fact]
    public void NewsFlasherControl_ScrollsAfterSecondLineAndDefersRefreshUntilAfterAdvance()
    {
        RunOnSta(() =>
        {
            NewsFlasherControl control = new()
            {
                Width = 1180,
                Height = 54
            };
            NewsFlasherViewModel viewModel = new()
            {
                Speed = 1d
            };
            viewModel.Headlines.Add(new NewsHeadlineViewModel
            {
                Text = "Global markets brace for a remarkably long teleprinter headline that should wrap to a second line before the scroll phase begins."
            });

            control.DataContext = viewModel;
            control.ApplyTemplate();
            control.Measure(new Size(1180, 54));
            control.Arrange(new Rect(0, 0, 1180, 54));
            control.UpdateLayout();

            MethodInfo tickMethod = typeof(NewsFlasherControl).GetMethod("OnPlaybackTick", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.OnPlaybackTick not found.");
            FieldInfo phaseField = typeof(NewsFlasherControl).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._phase not found.");
            FieldInfo pendingRefreshField = typeof(NewsFlasherControl).GetField("_pendingRefresh", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._pendingRefresh not found.");
            TextBlock headlineBlock = (TextBlock)(control.FindName("ActiveHeadlineBlock")
                ?? throw new InvalidOperationException("ActiveHeadlineBlock not found."));

            bool sawScrolling = false;
            bool sawNegativeOffset = false;
            bool sawPendingRefresh = false;
            for (int tick = 0; tick < 260; tick++)
            {
                if (tick == 120)
                {
                    viewModel.Headlines[0].Text = "This updated teleprinter item should finish the current step before refreshing into a new line pair.";
                    PumpDispatcherUntil(() => pendingRefreshField.GetValue(control) is true, TimeSpan.FromMilliseconds(250));
                }

                tickMethod.Invoke(control, [control, EventArgs.Empty]);
                PumpDispatcherUntil(() => !string.Equals(phaseField.GetValue(control)?.ToString(), "Idle", StringComparison.Ordinal), TimeSpan.FromSeconds(1));
                string phaseName = phaseField.GetValue(control)?.ToString() ?? string.Empty;
                if (string.Equals(phaseName, "Scrolling", StringComparison.Ordinal))
                    sawScrolling = true;

                if (Canvas.GetTop(headlineBlock) < -0.1d)
                    sawNegativeOffset = true;

                if (pendingRefreshField.GetValue(control) is true)
                    sawPendingRefresh = true;
            }

            Assert.True(sawScrolling);
            Assert.True(sawNegativeOffset);
            Assert.True(sawPendingRefresh);
        });
    }

    [Fact]
    public void NewsFlasherControl_CarriesPriorBottomLineWithoutRetypingIt()
    {
        RunOnSta(() =>
        {
            NewsFlasherControl control = new()
            {
                Width = 360,
                Height = 54
            };

            control.ApplyTemplate();
            control.Measure(new Size(360, 54));
            control.Arrange(new Rect(0, 0, 360, 54));
            control.UpdateLayout();

            MethodInfo formatMethod = typeof(NewsFlasherControl).GetMethod("FormatHeadline", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.FormatHeadline not found.");
            MethodInfo buildWrappedLinesMethod = typeof(NewsFlasherControl).GetMethod("BuildWrappedLines", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.BuildWrappedLines not found.");
            MethodInfo prepareHeadlineMethod = typeof(NewsFlasherControl).GetMethod("PrepareHeadline", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.PrepareHeadline not found.");
            MethodInfo stepAdvanceHeadlineMethod = typeof(NewsFlasherControl).GetMethod("StepAdvanceHeadline", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.StepAdvanceHeadline not found.");
            FieldInfo segmentIndexField = typeof(NewsFlasherControl).GetField("_segmentIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._segmentIndex not found.");
            FieldInfo displayTopLineField = typeof(NewsFlasherControl).GetField("_displayTopLine", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._displayTopLine not found.");
            FieldInfo displayBottomLineField = typeof(NewsFlasherControl).GetField("_displayBottomLine", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._displayBottomLine not found.");
            FieldInfo visibleCharacterCountField = typeof(NewsFlasherControl).GetField("_visibleCharacterCount", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._visibleCharacterCount not found.");
            TextBlock headlineBlock = (TextBlock)(control.FindName("ActiveHeadlineBlock")
                ?? throw new InvalidOperationException("ActiveHeadlineBlock not found."));

            string text = Assert.IsType<string>(formatMethod.Invoke(null, ["alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november"]));
            IReadOnlyList<string> wrappedLines = Assert.IsAssignableFrom<IReadOnlyList<string>>(buildWrappedLinesMethod.Invoke(control, [text]));
            Assert.True(wrappedLines.Count >= 3);

            NewsHeadlineViewModel headline = new() { Text = text };
            prepareHeadlineMethod.Invoke(control, [headline]);

            stepAdvanceHeadlineMethod.Invoke(control, [1]);

            Assert.Equal(1, Assert.IsType<int>(segmentIndexField.GetValue(control)));
            Assert.Equal(wrappedLines[1], Assert.IsType<string>(displayTopLineField.GetValue(control)));
            Assert.Equal(wrappedLines[2], Assert.IsType<string>(displayBottomLineField.GetValue(control)));
            Assert.Equal(0, Assert.IsType<int>(visibleCharacterCountField.GetValue(control)));
            Assert.Equal(wrappedLines[1] + Environment.NewLine + TeleprinterCursorText(), headlineBlock.Text);
        });
    }

    [Fact]
    public void NewsFlasherControl_PausesAfterFinalSegmentBeforeNextHeadline()
    {
        RunOnSta(() =>
        {
            NewsFlasherControl control = new()
            {
                Width = 420,
                Height = 54
            };
            NewsFlasherViewModel viewModel = new()
            {
                Speed = 1d
            };
            viewModel.Headlines.Add(new NewsHeadlineViewModel { Text = "ALPHA BRAVO" });
            viewModel.Headlines.Add(new NewsHeadlineViewModel { Text = "CHARLIE DELTA" });

            control.DataContext = viewModel;
            control.Measure(new Size(420, 54));
            control.Arrange(new Rect(0, 0, 420, 54));
            control.UpdateLayout();

            MethodInfo subscribeMethod = typeof(NewsFlasherControl).GetMethod("SubscribeToFlasher", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.SubscribeToFlasher not found.");
            MethodInfo resetMethod = typeof(NewsFlasherControl).GetMethod("ResetPlayback", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.ResetPlayback not found.");
            MethodInfo tickMethod = typeof(NewsFlasherControl).GetMethod("OnPlaybackTick", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.OnPlaybackTick not found.");
            FieldInfo phaseField = typeof(NewsFlasherControl).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._phase not found.");
            FieldInfo pauseTicksField = typeof(NewsFlasherControl).GetField("_pauseTicksRemaining", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._pauseTicksRemaining not found.");
            FieldInfo headlineIndexField = typeof(NewsFlasherControl).GetField("_headlineIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._headlineIndex not found.");

            subscribeMethod.Invoke(control, [viewModel]);
            resetMethod.Invoke(control, []);

            bool sawPauseBetweenHeadlines = false;
            for (int tick = 0; tick < 120; tick++)
            {
                tickMethod.Invoke(control, [control, EventArgs.Empty]);
                PumpDispatcherUntil(() => !string.Equals(phaseField.GetValue(control)?.ToString(), "Idle", StringComparison.Ordinal), TimeSpan.FromSeconds(1));
                string phaseName = phaseField.GetValue(control)?.ToString() ?? string.Empty;
                if (string.Equals(phaseName, "PauseBetweenHeadlines", StringComparison.Ordinal))
                {
                    sawPauseBetweenHeadlines = true;
                    break;
                }
            }

            Assert.True(sawPauseBetweenHeadlines);
            Assert.True(Assert.IsType<int>(pauseTicksField.GetValue(control)) > 0);
            Assert.Equal(0, Assert.IsType<int>(headlineIndexField.GetValue(control)));
        });
    }

    [Fact]
    public void NewsFlasherControl_FormatHeadline_PreservesExplicitLineBreaks()
    {
        MethodInfo formatMethod = typeof(NewsFlasherControl).GetMethod("FormatHeadline", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NewsFlasherControl.FormatHeadline not found.");

        string formatted = Assert.IsType<string>(formatMethod.Invoke(null, ["first line\r\nsecond\tline\r\n\r\nthird line"]));

        Assert.Equal(
            "FIRST LINE" + Environment.NewLine +
            "SECOND LINE" + Environment.NewLine +
            "THIRD LINE",
            formatted);
    }

    [Fact]
    public void NewsFlasherControl_CachesAndClearsWidthMeasurements()
    {
        RunOnSta(() =>
        {
            NewsFlasherControl control = new()
            {
                Width = 420,
                Height = 54
            };

            control.ApplyTemplate();
            control.Measure(new Size(420, 54));
            control.Arrange(new Rect(0, 0, 420, 54));
            control.UpdateLayout();

            MethodInfo buildWrappedLinesMethod = typeof(NewsFlasherControl).GetMethod("BuildWrappedLines", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.BuildWrappedLines not found.");
            MethodInfo clearMeasurementCacheMethod = typeof(NewsFlasherControl).GetMethod("ClearMeasurementCache", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl.ClearMeasurementCache not found.");
            FieldInfo cacheField = typeof(NewsFlasherControl).GetField("_headlineWidthCache", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("NewsFlasherControl._headlineWidthCache not found.");

            buildWrappedLinesMethod.Invoke(control, ["ALPHA BRAVO CHARLIE DELTA ECHO FOXTROT"]);
            object cache = cacheField.GetValue(control) ?? throw new InvalidOperationException("Width cache not found.");
            int populatedCount = (int)(cache.GetType().GetProperty("Count")?.GetValue(cache) ?? 0);
            buildWrappedLinesMethod.Invoke(control, ["ALPHA BRAVO CHARLIE DELTA ECHO FOXTROT"]);
            int reusedCount = (int)(cache.GetType().GetProperty("Count")?.GetValue(cache) ?? -1);

            clearMeasurementCacheMethod.Invoke(control, []);
            int clearedCount = (int)(cache.GetType().GetProperty("Count")?.GetValue(cache) ?? -1);

            Assert.True(populatedCount > 0);
            Assert.Equal(populatedCount, reusedCount);
            Assert.Equal(0, clearedCount);

            for (int i = 0; i < 257; i++)
                buildWrappedLinesMethod.Invoke(control, [$"ALPHA-{i} BRAVO-{i} CHARLIE-{i}"]);

            int boundedCount = (int)(cache.GetType().GetProperty("Count")?.GetValue(cache) ?? -1);
            Assert.InRange(boundedCount, 1, 256);
        });
    }

    private static string TeleprinterCursorText() => " █";

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
            "VisualizerSceneControl.xaml.cs"));

        Assert.Contains("x:Name=\"ViewportHost\"", tapeXaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"4,0,4,0\"", tapeXaml, StringComparison.Ordinal);
        Assert.Contains("nameof(TapeItemViewModel.IsWaitingOnData)", tapeCode, StringComparison.Ordinal);
        Assert.Contains("Source = item", tapeCode, StringComparison.Ordinal);
        Assert.Contains("nameof(TapeItemViewModel.WaitingGlyphText)", tapeCode, StringComparison.Ordinal);
        Assert.Contains("nameof(TapeItemViewModel.WaitingGlyphForeground)", tapeCode, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"92\"", statusXaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", statusXaml, StringComparison.Ordinal);
        Assert.Contains("double statusHeight = Math.Max(72d, StatusBarHost.ActualHeight);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("double tapeTopMargin = Math.Clamp(statusHeight + 12d, 78d, 126d);", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualizerScene_ShowsBrandedAppTitleVersionWatermark_AndClockUsesStableMonospaceClockFonts()
    {
        string sceneXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml"));
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));
        string desktopWindowCode = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Desktop",
            "Windows",
            "MainWindow.xaml.cs"));
        string desktopWindowXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Desktop",
            "Windows",
            "MainWindow.xaml"));
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
        Assert.Contains("FooterAttributionWatermark", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("&#169; Supratim Sanyal. SANYALnet Labs.", sceneXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"&#169; Supratim Sanyal. SANYALnet Labs Non-Commercial License.\"", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("private const string FooterBaseText = \"\\u00A9 Supratim Sanyal. SANYALnet Labs.\";", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("FooterBaseText = \"\\u00A9 Supratim Sanyal. SANYALnet Labs Non-Commercial License.", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("BackgroundAttributions", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("UpdateFooterAttribution(backgroundPath);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("QueueBackgroundCatalogRescan", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("StartBackgroundCatalogRefresh(\"config-resume\")", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_ = RefreshSceneAfterValidationPauseAsync();", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ConfigureTimers();", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.InvokeAsync(() => ApplyBackgroundCatalogRefresh", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RotateBackgroundAsync(forceDifferent: false)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RotateBackgroundAsync(forceDifferent: true)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("BackgroundCatalogRescanned", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ClearBackgroundImageLayer(_activeBackgroundImage);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ClearBackgroundImageLayer(_inactiveBackgroundImage);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("TraceSceneState(\"BackgroundCleared\")", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"19\"", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("DropShadowEffect", sceneXaml, StringComparison.Ordinal);
        Assert.Contains("VersionWatermark.Text = PortfolioVersion.Version;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetAutomationId(VersionWatermark, \"VisualizerVersionWatermark\");", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(VersionWatermark, $\"Version {PortfolioVersion.Version}\");", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetHelpText(VersionWatermark, PortfolioVersion.Version);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("Title=\"DO NOT PANIC PORTFOLIO VISUALIZER\"", desktopWindowXaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetAutomationId(this, \"DesktopMainWindow\");", desktopWindowCode, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetHelpText(this, PortfolioVersion.Version);", desktopWindowCode, StringComparison.Ordinal);
        Assert.Contains("StableClockFont", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("StableClockFont", floatingClockXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SevenSegmentDigitConverter", statusBarXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SevenSegmentDigitConverter", floatingClockXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BackgroundCatalogRefreshDecision_InvalidCurrentPath_RotatesWithoutForceDifferent()
    {
        VisualizerSceneControl.BackgroundCatalogRefreshDecision decision =
            VisualizerSceneControl.DecideBackgroundCatalogRefresh(
                @"C:\old\background.jpg",
                [@"C:\old\background.jpg"],
                [@"C:\new\background.jpg"]);

        Assert.True(decision.CatalogChanged);
        Assert.False(decision.CurrentStillValid);
        Assert.True(decision.ShouldRotate);
        Assert.False(decision.ShouldForceDifferentRotation);
    }

    [Fact]
    public void BackgroundCatalogRefreshDecision_CatalogChangedButCurrentStillValid_ForcesDifferentRotation()
    {
        VisualizerSceneControl.BackgroundCatalogRefreshDecision decision =
            VisualizerSceneControl.DecideBackgroundCatalogRefresh(
                @"C:\shared\background.jpg",
                [@"C:\shared\background.jpg"],
                [@"C:\shared\background.jpg", @"C:\shared\second.jpg"]);

        Assert.True(decision.CatalogChanged);
        Assert.True(decision.CurrentStillValid);
        Assert.True(decision.ShouldRotate);
        Assert.True(decision.ShouldForceDifferentRotation);
    }

    [Fact]
    public void BackgroundCatalogRefreshDecision_UnchangedCatalogAndCurrentStillValid_DoesNotRotate()
    {
        VisualizerSceneControl.BackgroundCatalogRefreshDecision decision =
            VisualizerSceneControl.DecideBackgroundCatalogRefresh(
                @"C:\shared\background.jpg",
                [@"C:\shared\background.jpg", @"C:\shared\second.jpg"],
                [@"C:\shared\background.jpg", @"C:\shared\second.jpg"]);

        Assert.False(decision.CatalogChanged);
        Assert.True(decision.CurrentStillValid);
        Assert.False(decision.ShouldRotate);
        Assert.False(decision.ShouldForceDifferentRotation);
    }

    [Fact]
    public void VisualizerScene_HostsGlobalMarketsTapeAboveNewsTicker()
    {
        string sceneXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml"));

        int globalMarketsIndex = sceneXaml.IndexOf("x:Name=\"GlobalMarketsTapeHost\"", StringComparison.Ordinal);
        int newsIndex = sceneXaml.IndexOf("x:Name=\"NewsFlasherHost\"", StringComparison.Ordinal);
        Assert.True(globalMarketsIndex >= 0);
        Assert.True(newsIndex > globalMarketsIndex);
    }

    [Fact]
    public void VisualizerScene_LetsGraphCardsUseSceneWideSafeInsetBehindForeground()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));

        Assert.Contains("private const double GraphMotionSafeInset = 12d;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private Rect GetGraphMotionBounds()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("=> GetBaseMotionBounds();", sceneCodeBehind, StringComparison.Ordinal);
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
        Assert.Contains("ConcurrentDictionary<string, Lazy<ImageSource?>> FlagImageCache", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("FlagImageCache.GetOrAdd", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("LazyThreadSafetyMode.ExecutionAndPublication", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("FlagImageCache.TryRemove", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("GetFlagImageSource(city.FlagCode)", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("pack://application:,,,/PortfolioSaver.Render;component/Assets/Flags/", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("TraceLog.WarnState", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("FlagImageLoadFailed", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("\"flag_code\"", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("flagCode", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("Stretch = Stretch.Uniform", globalTapeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildUnitedStatesFlag()", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("FontSize = 10", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("Text = \"5D\"", globalTapeCode, StringComparison.Ordinal);
        Assert.Contains("Text = \"now\"", globalTapeCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ClockCityViewModel_SetMiniGraphPointsIfChanged_ReplacesCollectionOnlyWhenPointsChange()
    {
        ClockCityViewModel city = new();
        PointCollection original = city.MiniGraphPoints;
        List<string?> changedProperties = [];
        city.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        bool changed = city.SetMiniGraphPointsIfChanged([new Point(1, 2), new Point(3, 4)]);

        Assert.True(changed);
        Assert.NotSame(original, city.MiniGraphPoints);
        Assert.Equal(2, city.MiniGraphPoints.Count);
        Assert.Equal(new Point(1, 2), city.MiniGraphPoints[0]);
        Assert.Contains(nameof(ClockCityViewModel.MiniGraphPoints), changedProperties);

        PointCollection firstReplacement = city.MiniGraphPoints;
        changedProperties.Clear();

        changed = city.SetMiniGraphPointsIfChanged([new Point(1, 2), new Point(3, 4)]);

        Assert.False(changed);
        Assert.Same(firstReplacement, city.MiniGraphPoints);
        Assert.Empty(changedProperties);
    }

    [Fact]
    public void ClockCityViewModel_SetMiniGraphPointsIfChanged_CopiesFrozenSnapshotIntoMutableUiCollection()
    {
        ClockCityViewModel city = new();
        PointCollection frozenSnapshot = new([new Point(7, 8), new Point(9, 10)]);
        frozenSnapshot.Freeze();

        bool changed = city.SetMiniGraphPointsIfChanged(frozenSnapshot);

        Assert.True(changed);
        Assert.NotSame(frozenSnapshot, city.MiniGraphPoints);
        Assert.False(city.MiniGraphPoints.IsFrozen);
        Assert.Equal(frozenSnapshot.Count, city.MiniGraphPoints.Count);
        Assert.Equal(frozenSnapshot[0], city.MiniGraphPoints[0]);
        Assert.Equal(frozenSnapshot[1], city.MiniGraphPoints[1]);
    }

    [Fact]
    public void ClockCityViewModel_SetMiniGraphPointsIfChanged_RejectsNullInput()
    {
        ClockCityViewModel city = new();

        Assert.Throws<ArgumentNullException>(() => city.SetMiniGraphPointsIfChanged(null!));
    }

    [Fact]
    public void GlobalMarkets_UseLiveQuoteStateWithoutSeparateInitialWaitingLane()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));

        Assert.DoesNotContain("GlobalMarketsWaitingGlyph", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("new KeyValuePair<string, object?>(\"loading_exchange_count\", 0)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("new KeyValuePair<string, object?>(\"loading_exchange_symbols\", Array.Empty<string>())", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyExchangeCardMarketStatus(city, quote, referenceUtc);", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatingGraphCards_UseIndependentMotionAcrossSceneWideSafeInset_AndRightLabelGutter()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));
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

        Assert.Contains("private const int MaxVisibleGraphCards = 16;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("SeparateVisibleGraphCards(bounds);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("GraphCardSeparationGap", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("for (int i = 0; i < _graphs.Count && visibleGraphCount < MaxVisibleGraphCards; i++)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyGraphRefreshImpulse(graph, bounds);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("string? resetReason = ResetGraphRefreshImpulseIfNeeded(graph, bounds);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("GraphCardFlashStop", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private static readonly TimeSpan GraphSelectionRefreshInterval = TimeSpan.FromMinutes(10);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RunScheduledSceneAction(\"graph-selection\", RefreshGraphSelectionIfDue);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyGraphMotionVariance(", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyGraphRefreshTravel(", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("graph.PlotWidth = Math.Max(106d, graphWidth - 62d);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("Margin=\"2,3,12,0\"", floatingGraphXaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"24\"", floatingGraphXaml, StringComparison.Ordinal);
        Assert.Contains("private const int MaxSceneGraphCards = 16;", startupCoordinator, StringComparison.Ordinal);
        Assert.Contains(".Take(MaxSceneGraphCards)", startupCoordinator, StringComparison.Ordinal);
        Assert.Contains(".OrderByDescending(candidate => candidate.HasLiveMoverScore)", startupCoordinator, StringComparison.Ordinal);
        Assert.Contains(".ThenByDescending(candidate => candidate.Score)", startupCoordinator, StringComparison.Ordinal);
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
            "VisualizerSceneControl.xaml.cs"));

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
        Assert.DoesNotContain("ApplyWaitingMacroMeter(meter);", sceneCodeBehind, StringComparison.Ordinal);
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
            "VisualizerSceneControl.xaml.cs"));

        Assert.Contains("ItemsSource=\"{Binding MacroMeters}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding MarketStatusText}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ClockDateText}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ClockText}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding ProviderText}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"Auto\" />", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding UpdatedPrefixText}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding UpdatedTickerFieldText}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{Binding UpdatedTickerFieldForeground}\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"102\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"92\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"96\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"50\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"22\" />", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"64\" />", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"8\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"2\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("TextAlignment=\"Left\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"248\"", statusBarXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"700\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Contains("LineHeight=\"15\"", statusBarXaml, StringComparison.Ordinal);
        Assert.Matches(new Regex("Text=\\\"\\{Binding MarketStatusText\\}\\\"[\\s\\S]*FontFamily=\\\"\\{StaticResource StableClockFont\\}\\\"", RegexOptions.CultureInvariant), statusBarXaml);
        Assert.Matches(new Regex("Text=\\\"\\{Binding UpdatedPrefixText\\}\\\"[\\s\\S]*FontFamily=\\\"\\{StaticResource StableClockFont\\}\\\"", RegexOptions.CultureInvariant), statusBarXaml);
        Assert.Contains("Width=\"222\"", statusBarXaml, StringComparison.Ordinal);
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
        Assert.Contains("Width = SymbolHostWidth", tapeCode, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(1, 0, 0, 0)", tapeCode, StringComparison.Ordinal);
        Assert.Contains("FontSize = 11", tapeCode, StringComparison.Ordinal);
        Assert.Contains("RenderingEventArgs", File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "PortfolioSaver.Render", "Services", "TapeAnimationController.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void TickerTapeControl_UsesCachedFixedWidthMeasurementInsteadOfForcedLayout()
    {
        string tapeCode = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Render",
            "Controls",
            "TickerTapeControl.xaml.cs"));

        Assert.Contains("private const double SymbolHostWidth = 62d", tapeCode, StringComparison.Ordinal);
        Assert.Contains("private const double TapeItemFixedWidth = SymbolHostWidth", tapeCode, StringComparison.Ordinal);
        Assert.Contains("private readonly Dictionary<int, double> _sequenceWidthCache", tapeCode, StringComparison.Ordinal);
        Assert.Contains("GetCachedContentWidth(tape)", tapeCode, StringComparison.Ordinal);
        Assert.Contains("(double)itemCount * TapeItemFixedWidth", tapeCode, StringComparison.Ordinal);
        Assert.Contains("MaxDeferredViewportRetries", tapeCode, StringComparison.Ordinal);
        Assert.Contains("QueueMetricsUpdate(DispatcherPriority.Background)", tapeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("MeasureContentWidth", tapeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateLayout();", tapeCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TapeAnimationController_StartWithoutAttach_DoesNotSubscribe()
    {
        TapeAnimationController controller = new();

        controller.Start();

        Assert.False(controller.IsRunning);
    }

    [Fact]
    public void TapeAnimationController_UpdateBeforeStart_AppliesAnchorOffsetBeforeRendering()
    {
        RunOnSta(() =>
        {
            Border element = new() { Width = 100, Height = 20 };
            TapeAnimationController controller = new();

            try
            {
                controller.Attach(element);
                controller.Update(cycleDistance: 120d, pixelsPerSecond: 30d, ScrollDirection.Left, anchorOffset: 14d);
                controller.Start();

                TranslateTransform transform = Assert.IsType<TranslateTransform>(element.RenderTransform);
                Assert.Equal(14d, transform.X, precision: 3);
                Assert.True(controller.IsRunning);
            }
            finally
            {
                controller.Stop();
            }
        });
    }

    [Fact]
    public void TickerTapeControl_WithNoItems_LeavesAnimationStopped()
    {
        RunOnSta(() =>
        {
            TickerTapeControl control = new()
            {
                DataContext = new TapeViewModel(),
                Width = 600,
                Height = 40
            };
            Window? window = null;

            try
            {
                window = HostControlForLayout(control, 600, 40);
                control.RefreshMotionMetricsForTests();

                Assert.False(control.AnimationControllerForTests.IsRunning);
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                window?.Close();
            }
        });
    }

    [Fact]
    public void TickerTapeControl_WithMeasuredItems_StartsAnimationAfterMetricsUpdate()
    {
        RunOnSta(() =>
        {
            TapeViewModel tape = new() { Title = "TEST" };
            tape.Items.Add(new TapeItemViewModel
            {
                SymbolText = "AAPL",
                LastText = "123.45",
                ChangeText = "+1.23%"
            });

            TickerTapeControl control = new()
            {
                DataContext = tape,
                Width = 600,
                Height = 40
            };
            Window? window = null;

            try
            {
                window = HostControlForLayout(control, 600, 40);
                control.RefreshMotionMetricsForTests();

                Assert.True(control.AnimationControllerForTests.IsRunning);
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                window?.Close();
            }
        });
    }

    [Fact]
    public void TickerTapeControl_CachedWidthDrivesExpectedAnimationCycleDistance()
    {
        RunOnSta(() =>
        {
            TapeViewModel tape = new() { Title = "TEST" };
            tape.Items.Add(new TapeItemViewModel { SymbolText = "AAPL", LastText = "123.45", ChangeText = "+1.23%" });
            tape.Items.Add(new TapeItemViewModel { SymbolText = "MSFT", LastText = "456.78", ChangeText = "-0.45%" });

            TickerTapeControl control = new()
            {
                DataContext = tape,
                Width = 800,
                Height = 40
            };
            Window? window = null;

            try
            {
                window = HostControlForLayout(control, 800, 40);
                control.RefreshMotionMetricsForTests();

                Assert.True(control.AnimationControllerForTests.IsRunning);
                Assert.Equal(480d, control.AnimationControllerForTests.CycleDistanceForTests, precision: 3);
                Assert.True(control.TrackPanelWidthForTests >= control.AnimationControllerForTests.CycleDistanceForTests);
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                window?.Close();
            }
        });
    }

    [Fact]
    public void TickerTapeControl_StopsAndRestartsAcrossDataTransitions()
    {
        RunOnSta(() =>
        {
            TapeViewModel tape = CreateTapeWithItem();
            TickerTapeControl control = new()
            {
                DataContext = tape,
                Width = 600,
                Height = 40
            };
            Window? window = null;

            try
            {
                window = HostControlForLayout(control, 600, 40);
                control.RefreshMotionMetricsForTests();
                Assert.True(control.AnimationControllerForTests.IsRunning);

                control.DataContext = new TapeViewModel();
                control.RefreshMotionMetricsForTests();
                Assert.False(control.AnimationControllerForTests.IsRunning);

                control.DataContext = CreateTapeWithItem();
                control.RefreshMotionMetricsForTests();
                Assert.True(control.AnimationControllerForTests.IsRunning);
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                window?.Close();
            }
        });
    }

    [Fact]
    public void TickerTapeControl_DoesNotRestartAnimationAfterUnload()
    {
        RunOnSta(() =>
        {
            TickerTapeControl control = new()
            {
                DataContext = CreateTapeWithItem(),
                Width = 600,
                Height = 40
            };
            Window? window = null;

            try
            {
                window = HostControlForLayout(control, 600, 40);
                control.RefreshMotionMetricsForTests();
                Assert.True(control.AnimationControllerForTests.IsRunning);

                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                control.RefreshMotionMetricsForTests();

                Assert.False(control.AnimationControllerForTests.IsRunning);
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                window?.Close();
            }
        });
    }

    [Fact]
    public void GlobalMarketsTapeControl_WithNoExchangeCities_LeavesAnimationStopped()
    {
        RunOnSta(() =>
        {
            GlobalMarketsTapeControl control = new()
            {
                DataContext = new FloatingClockViewModel(),
                Width = 900,
                Height = 80
            };
            Window? window = null;

            try
            {
                window = HostControlForLayout(control, 900, 80);
                control.RefreshMotionMetricsForTests();

                Assert.False(control.AnimationControllerForTests.IsRunning);
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                window?.Close();
            }
        });
    }

    [Fact]
    public void GlobalMarketsTapeControl_WithOnlyPinnedCity_PreservesPinnedCardAndStopsScrollingAnimation()
    {
        RunOnSta(() =>
        {
            FloatingClockViewModel clock = new();
            clock.Cities.Add(new ClockCityViewModel
            {
                Key = "NewYorkNasdaq",
                Label = "New York",
                ShowExchangeDetails = true,
                ExchangeName = "Nasdaq",
                ExchangeSymbol = "^IXIC",
                IndexValueText = "12345.67",
                IndexChangeText = "+0.11%",
                TimeText = "09:30",
                ZoneText = "EDT",
                MarketStatusText = "OPEN"
            });

            GlobalMarketsTapeControl control = new()
            {
                DataContext = clock,
                Width = 900,
                Height = 80
            };
            Window? window = null;

            try
            {
                window = HostControlForLayout(control, 900, 80);
                control.RefreshMotionMetricsForTests();

                Assert.False(control.AnimationControllerForTests.IsRunning);
                Assert.NotNull(control.PinnedCardChildForTests);
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                window?.Close();
            }
        });
    }

    [Fact]
    public void GlobalMarketsTapeControl_WithMeasuredExchangeCities_StartsAnimationAfterMetricsUpdate()
    {
        RunOnSta(() =>
        {
            FloatingClockViewModel clock = new() { Title = "Global Markets" };
            clock.Cities.Add(new ClockCityViewModel
            {
                Key = "London",
                Label = "London",
                ShowExchangeDetails = true,
                ExchangeName = "FTSE 100",
                ExchangeSymbol = "^FTSE",
                IndexValueText = "8123.45",
                IndexChangeText = "+0.42%",
                TimeText = "12:34",
                ZoneText = "GMT",
                MarketStatusText = "OPEN"
            });

            GlobalMarketsTapeControl control = new()
            {
                DataContext = clock,
                Width = 900,
                Height = 80
            };
            Window? window = null;

            try
            {
                window = HostControlForLayout(control, 900, 80);
                control.RefreshMotionMetricsForTests();

                Assert.True(control.AnimationControllerForTests.IsRunning);
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                window?.Close();
            }
        });
    }

    [Fact]
    public void GlobalMarketsTapeControl_StopsAndRestartsAcrossDataTransitions()
    {
        RunOnSta(() =>
        {
            GlobalMarketsTapeControl control = new()
            {
                DataContext = CreateClockWithExchangeCity(),
                Width = 900,
                Height = 80
            };
            Window? window = null;

            try
            {
                window = HostControlForLayout(control, 900, 80);
                control.RefreshMotionMetricsForTests();
                Assert.True(control.AnimationControllerForTests.IsRunning);

                control.DataContext = new FloatingClockViewModel();
                control.RefreshMotionMetricsForTests();
                Assert.False(control.AnimationControllerForTests.IsRunning);

                control.DataContext = CreateClockWithExchangeCity();
                control.RefreshMotionMetricsForTests();
                Assert.True(control.AnimationControllerForTests.IsRunning);
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                window?.Close();
            }
        });
    }

    [Fact]
    public void GlobalMarketsTapeControl_DoesNotRestartAnimationAfterUnload()
    {
        RunOnSta(() =>
        {
            GlobalMarketsTapeControl control = new()
            {
                DataContext = CreateClockWithExchangeCity(),
                Width = 900,
                Height = 80
            };
            Window? window = null;

            try
            {
                window = HostControlForLayout(control, 900, 80);
                control.RefreshMotionMetricsForTests();
                Assert.True(control.AnimationControllerForTests.IsRunning);

                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                control.RefreshMotionMetricsForTests();

                Assert.False(control.AnimationControllerForTests.IsRunning);
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                window?.Close();
            }
        });
    }

    [Fact]
    public void VisualizerScene_EmitsDisplayedTapeSamplesForSoakComparison()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));

        Assert.Contains("TraceDisplayedTapeSample();", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void TraceDisplayedTapeSample()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DisplayedTapeSample", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DisplayedTapeLanes", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("lane{index + 1}={tape.Title}[", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("~{NormalizeTapeSnapshotValue(item.LastText)}~", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualizerScene_SeparatesTitleLaneFromTopStatusBand()
    {
        string sceneXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml"));

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
            VisualizerSceneControl control = new();
            MacroMeterViewModel meter = new();

            FieldInfo latestQuotesField = typeof(VisualizerSceneControl).GetField(
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

            MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
                "UpdateQuoteMeter",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("UpdateQuoteMeter method not found.");

            method.Invoke(control, [meter, "VIX", "^VIX", 60m, true]);

            Assert.Same(Brushes.OrangeRed, meter.AccentBrush);
            Assert.Equal("+2.0%", meter.ChangeText);
        });
    }

    [Fact]
    public void UpdateQuoteMeter_InvertsDollarStrengthColors_ForDxy()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new();
            MacroMeterViewModel meter = new();

            FieldInfo latestQuotesField = typeof(VisualizerSceneControl).GetField(
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

            MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
                "UpdateQuoteMeter",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("UpdateQuoteMeter method not found.");

            method.Invoke(control, [meter, "DXY", "DX-Y.NYB", 120m, true]);

            Assert.Same(Brushes.OrangeRed, meter.AccentBrush);
            Assert.Equal("+0.8%", meter.ChangeText);
        });
    }

    [Fact]
    public void UpdateQuoteMeter_UsesGreenForFallingDollarStrength()
    {
        RunOnSta(() =>
        {
            VisualizerSceneControl control = new();
            MacroMeterViewModel meter = new();

            FieldInfo latestQuotesField = typeof(VisualizerSceneControl).GetField(
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

            MethodInfo method = typeof(VisualizerSceneControl).GetMethod(
                "UpdateQuoteMeter",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("UpdateQuoteMeter method not found.");

            method.Invoke(control, [meter, "DXY", "DX-Y.NYB", 120m, true]);

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
            "VisualizerSceneControl.xaml.cs"));

        Assert.Contains("private void UpdateClocks(bool forceAncillaryRefresh = false)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_lastClockAncillaryRefreshUtc", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_lastStatusAncillaryRefreshUtc", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("UpdateClockEntries(referenceUtc, refreshClockAncillary);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("QueueWorldMarketsRefresh(refreshAncillary: force, reason: force ? \"clock-data-force\" : \"clock-data\");", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void UpdateClockEntries(DateTimeOffset referenceUtc, bool refreshAncillary)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private async Task<WorldMarketsLaneSnapshot> BuildWorldMarketsLaneSnapshotAsync(bool refreshAncillary, CancellationToken cancellationToken)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void ApplyWorldMarketsLaneSnapshot(WorldMarketsLaneSnapshot snapshot)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.VerifyAccess();", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_clockViewModel.Cities.Select(CloneClockCity).ToList()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ClockCityViewModel working = CloneClockCity(city);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("target.SetMiniGraphPointsIfChanged(source.MiniGraphPoints);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SetMiniGraphPointsIfChanged(city, history, 72d, 12d);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ArgumentNullException.ThrowIfNull(target);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("target.MiniGraphPoints = new PointCollection(source.MiniGraphPoints);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("city.MiniGraphPoints = BuildMiniGraphPoints(history, 72d, 12d);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("city.TimeText = FormatClockTimeWithZone(cityTime, zone);", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("FormatClockTimeWithZone(", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_statusViewModel.ClockText = FormatClockTimeWithZone(", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("TimeZoneInfo.Utc", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphMotionBounds_UseSceneWideSafeInsetBecauseGraphsSwimBehindForeground()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));

        Assert.Contains("private Rect GetGraphMotionBounds()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("=> GetBaseMotionBounds();", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphWarmup_PreservesInFlightWarmupAcrossRefreshTicks()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));

        Assert.Contains("private Task? _graphWarmupTask;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("if (preserveLayout && _graphWarmupTask is not null && !_graphWarmupTask.IsCompleted)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("TraceScene(\"RestartGraphWarmup skipped because a graph warmup is already running.\");", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_graphWarmupTask = WarmGraphsAsync(rotationSeed, preserveLayout, cancellation.Token);", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphWarmup_BatchesSynchronousLayoutFlushes()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));

        string applyOrUpdateGraph = ExtractMethodBody(sceneCodeBehind, "private void ApplyOrUpdateGraph");
        string warmGraphsAsync = ExtractMethodBody(sceneCodeBehind, "private async Task WarmGraphsAsync");

        Assert.Contains("await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);", warmGraphsAsync, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested();", warmGraphsAsync, StringComparison.Ordinal);
        Assert.Contains("PrepareGraphWarmupLayoutBatch();", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void PrepareGraphWarmupLayoutBatch()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_graphWarmupLayoutPrepared = false;", sceneCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateLayout();", applyOrUpdateGraph, StringComparison.Ordinal);
    }

    [Fact]
    public void PinnedNewYorkStatusBand_UsesCalendarOpenStateWhenQuoteSessionLagsAtMarketOpen()
    {
        string pinnedKey = GetPinnedNycExchangeKey();
        ClockCityViewModel city = new()
        {
            Key = pinnedKey,
            ShowExchangeDetails = true,
            ExchangeSymbol = "^IXIC"
        };
        ExchangeCalendarSet calendarSet = new();
        calendarSet.AddOrUpdate(CreateNewYorkMarketOpenCalendar(pinnedKey));

        MethodInfo method = GetPinnedNewYorkStatusBandMethod();

        string text = Assert.IsType<string>(method.Invoke(
            null,
            new object?[] { new List<ClockCityViewModel> { city }, calendarSet, Utc("2026-07-10T13:33:25.0000000Z"), new YFinanceExchangeTimingService() }));

        Assert.Contains("Market (New York): Open", text, StringComparison.Ordinal);
        Assert.Contains("Closing in", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Pre-Market", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PinnedNewYorkStatusBand_FieldBackedPathDelegatesToCalendarFormatter()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));
        string fieldBackedMethod = ExtractMethodBody(sceneCodeBehind, "private string BuildPinnedNewYorkStatusBandText");

        Assert.Contains("return BuildPinnedNewYorkStatusBandText(_clockViewModel.Cities, _exchangeCalendars, referenceUtc, _exchangeMarketCalendarService);", fieldBackedMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void PinnedNewYorkStatusBand_ReturnsPlaceholderWhenParameterizedCalendarIsMissing()
    {
        ClockCityViewModel city = new()
        {
            Key = GetPinnedNycExchangeKey(),
            ShowExchangeDetails = true,
            ExchangeSymbol = "^IXIC"
        };

        MethodInfo method = GetPinnedNewYorkStatusBandMethod();

        string text = Assert.IsType<string>(method.Invoke(
            null,
            new object?[] { new List<ClockCityViewModel> { city }, new ExchangeCalendarSet(), Utc("2026-07-10T13:33:25.0000000Z"), new YFinanceExchangeTimingService() }));

        Assert.Equal("Market (New York): --", text);
    }

    [Fact]
    public void VisualizerScene_ProvidesDeterministicDemoFlashPulses_ForVisualValidation()
    {
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));

        Assert.Contains("DemoFlashInterval = TimeSpan.FromSeconds(30)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("_nextDemoFlashTickUtc", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("StartDemoFlashSequence()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("if (_demoFlashTicks > 0 && now >= _nextDemoFlashTickUtc)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RunDemoFlashPulse()", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("TriggerValueFlash(Brushes.DeepSkyBlue)", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("TriggerCardFlash(Brushes.DeepSkyBlue)", sceneCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualizerScene_IncludesAnimatedMarketCritterOverlay()
    {
        string sceneXaml = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml"));
        string sceneCodeBehind = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Presentation",
            "Controls",
            "VisualizerSceneControl.xaml.cs"));
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
        Assert.Contains("private static void StepMarketBagSprite(", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("bag.Y = Math.Clamp(", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("critter.X = Math.Clamp(", sceneCodeBehind, StringComparison.Ordinal);
        Assert.Contains("critter.Y = Math.Clamp(", sceneCodeBehind, StringComparison.Ordinal);
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

    private static void PumpDispatcherUntil(Func<bool> condition)
        => PumpDispatcherUntil(condition, TimeSpan.FromSeconds(2));

    private static void PumpDispatcherUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            DispatcherFrame frame = new();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                new Action(() => frame.Continue = false),
                DispatcherPriority.Normal);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(1);
        }
    }

    private static Window HostControlForLayout(FrameworkElement control, double width, double height)
    {
        Window window = new()
        {
            Content = control,
            Width = width,
            Height = height,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };

        window.Show();
        window.UpdateLayout();
        control.UpdateLayout();
        return window;
    }

    private static TradingPeriodWindow Window(string startUtc, string endUtc)
        => new(Utc(startUtc), Utc(endUtc), "EDT", -14400);

    private static MethodInfo GetPinnedNewYorkStatusBandMethod()
        => typeof(VisualizerSceneControl).GetMethod(
            "BuildPinnedNewYorkStatusBandText",
            BindingFlags.NonPublic | BindingFlags.Static,
            [
                typeof(IReadOnlyList<ClockCityViewModel>),
                typeof(ExchangeCalendarSet),
                typeof(DateTimeOffset),
                typeof(YFinanceExchangeTimingService)
            ])
            ?? throw new InvalidOperationException("Pinned New York status formatter not found.");

    private static string GetPinnedNycExchangeKey()
    {
        FieldInfo field = typeof(VisualizerSceneControl).GetField(
            "PinnedNycExchangeKey",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Pinned New York exchange key constant not found.");

        string value = Assert.IsType<string>(field.GetValue(null));
        Assert.Equal("NewYorkNasdaq", value);
        return value;
    }

    private static DateTimeOffset Utc(string value)
        => DateTimeOffset.ParseExact(
            value,
            ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static ExchangeTradingCalendar CreateNewYorkMarketOpenCalendar(string cityKey)
        => new()
        {
            CityKey = cityKey,
            ExchangeSymbol = "^IXIC",
            CurrentTradingPeriod = new CurrentTradingPeriods(
                Pre: Window("2026-07-10T08:00:00Z", "2026-07-10T13:30:00Z"),
                Regular: Window("2026-07-10T13:30:00Z", "2026-07-10T20:00:00Z"),
                Post: Window("2026-07-10T20:00:00Z", "2026-07-11T00:00:00Z"))
        };

    private static TapeViewModel CreateTapeWithItem()
    {
        TapeViewModel tape = new() { Title = "TEST" };
        tape.Items.Add(new TapeItemViewModel
        {
            SymbolText = "AAPL",
            LastText = "123.45",
            ChangeText = "+1.23%"
        });
        return tape;
    }

    private static FloatingClockViewModel CreateClockWithExchangeCity()
    {
        FloatingClockViewModel clock = new() { Title = "Global Markets" };
        clock.Cities.Add(new ClockCityViewModel
        {
            Key = "London",
            Label = "London",
            ShowExchangeDetails = true,
            ExchangeName = "FTSE 100",
            ExchangeSymbol = "^FTSE",
            IndexValueText = "8123.45",
            IndexChangeText = "+0.42%",
            TimeText = "12:34",
            ZoneText = "GMT",
            MarketStatusText = "OPEN"
        });
        return clock;
    }

    private static string ExtractMethodBody(string source, string methodSignature)
    {
        int methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
        if (methodStart < 0)
            throw new InvalidOperationException($"Could not locate method signature: {methodSignature}");

        int bodyStart = source.IndexOf('{', methodStart);
        if (bodyStart < 0)
            throw new InvalidOperationException($"Could not locate method body: {methodSignature}");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}')
                depth--;

            if (depth == 0)
                return source.Substring(bodyStart, index - bodyStart + 1);
        }

        throw new InvalidOperationException($"Could not locate method body end: {methodSignature}");
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "DoNotPanicPortfolioVisualizer.sln");
            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}


