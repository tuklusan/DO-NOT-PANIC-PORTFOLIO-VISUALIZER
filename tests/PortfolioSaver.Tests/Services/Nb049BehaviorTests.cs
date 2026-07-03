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
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Screensaver.Controls;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class Nb049BehaviorTests
{
    [Fact]
    public void ScreensaverSceneControl_UsesIndependentMacroRefreshLane()
    {
        Type control = typeof(ScreensaverSceneControl);
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

        RequireMethod(control, flags, "StartMacroLane", typeof(void));
        RequireMethod(control, flags, "RunMacroLaneAsync", typeof(Task), typeof(CancellationToken));
                Type snapshotType = RequireNestedType(control, flags, "MacroLaneSnapshot");
        Type snapshotTaskType = typeof(Task<>).MakeGenericType(snapshotType);
        RequireMethod(control, flags, "BuildMacroLaneSnapshotAsync", snapshotTaskType, typeof(CancellationToken));
        RequireMethod(control, flags, "QueueMacroRefresh", typeof(void), typeof(string));
        RequireMethod(control, flags, "HasMeaningfulMacroDelta", typeof(bool), typeof(IReadOnlyDictionary<string, QuoteSnapshot>), typeof(IReadOnlyDictionary<string, QuoteSnapshot>));
        Assert.Single(RequireMethod(control, flags, "ApplyMacroLaneSnapshot", typeof(void)).GetParameters());
        Assert.Equal(typeof(SemaphoreSlim), RequireField(control, flags, "_macroLaneSignal").FieldType);
        Assert.Equal(typeof(Task), RequireField(control, flags, "_macroLaneTask").FieldType);
        Assert.Equal(typeof(int), RequireField(control, flags, "_macroLaneDirty").FieldType);
    }

    private static MethodInfo RequireMethod(Type type, BindingFlags flags, string name, Type? returnType, params Type[] parameterTypes)
    {
        MethodInfo? method = parameterTypes.Length == 0
            ? type.GetMethod(name, flags)
            : type.GetMethod(name, flags, parameterTypes);
        Assert.NotNull(method);
        if (returnType is not null)
            Assert.Equal(returnType, method.ReturnType);
        return method;
    }

    private static Type RequireNestedType(Type type, BindingFlags flags, string name)
    {
        Type? nestedType = type.GetNestedType(name, flags);
        Assert.NotNull(nestedType);
        return nestedType;
    }

    [Fact]
    public void ScreensaverSceneControl_DoesNotReintroduceFullMacroMeterRefreshIntoQuoteLoop()
    {
        string source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "src", "PortfolioSaver.Presentation", "Controls", "ScreensaverSceneControl.xaml.cs"));

        Assert.DoesNotContain("UpdateStatusMacroMeters(force: true);", source, StringComparison.Ordinal);
    }

    private static FieldInfo RequireField(Type type, BindingFlags flags, string name)
    {
        FieldInfo? field = type.GetField(name, flags);
        Assert.NotNull(field);
        return field;
    }
    private static string GetRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DoNotPanicPortfolioVisualizer.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
            throw new DirectoryNotFoundException("DoNotPanicPortfolioVisualizer.sln not found from test base directory.");

        return directory.FullName;
    }

}
