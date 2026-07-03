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
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Screensaver.Controls;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class Nb048BehaviorTests
{
    [Fact]
    public void StartupCoordinator_BuildSceneAsync_SeparatesCachedStartupNewsFromRefreshLane()
    {
        MethodInfo buildScene = RequirePublicMethod(
            typeof(StartupCoordinator),
            nameof(StartupCoordinator.BuildSceneAsync),
            typeof(Task<ScreensaverSceneState>),
            typeof(int),
            typeof(CancellationToken));
        MethodInfo buildBootstrap = RequirePublicMethod(
            typeof(StartupCoordinator),
            nameof(StartupCoordinator.BuildBootstrapScene),
            typeof(ScreensaverSceneState));
        MethodInfo buildNews = RequirePublicMethod(
            typeof(StartupCoordinator),
            nameof(StartupCoordinator.BuildNewsViewModelAsync),
            typeof(Task<NewsFlasherViewModel>),
            typeof(AppSettings),
            typeof(bool),
            typeof(CancellationToken));

        Assert.Equal(2, buildScene.GetParameters().Length);
        Assert.Empty(buildBootstrap.GetParameters());
        Assert.Equal(3, buildNews.GetParameters().Length);
    }

    [Fact]
    public void ScreensaverSceneControl_UsesIndependentBackgroundNewsRefreshLane()
    {
        Type control = typeof(ScreensaverSceneControl);
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

        RequireMethod(control, flags, "StartNewsRefreshLoop", typeof(void));
        RequireMethod(control, flags, "RunNewsRefreshLoopAsync", typeof(Task), typeof(CancellationToken));
        RequireMethod(control, flags, "RefreshNewsLaneAsync", typeof(Task), typeof(bool), typeof(CancellationToken));
        Assert.Equal(typeof(Task), RequireField(control, flags, "_newsRefreshTask").FieldType);
        Assert.Equal(typeof(CancellationTokenSource), RequireField(control, flags, "_newsRefreshCancellation").FieldType);
    }

    private static MethodInfo RequirePublicMethod(Type type, string name, Type returnType, params Type[] parameterTypes)
    {
        MethodInfo? method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public, parameterTypes);
        Assert.NotNull(method);
        Assert.Equal(returnType, method.ReturnType);
        return method;
    }

    private static MethodInfo RequireMethod(Type type, BindingFlags flags, string name, Type returnType, params Type[] parameterTypes)
    {
        MethodInfo? method = type.GetMethod(name, flags, parameterTypes);
        Assert.NotNull(method);
        Assert.Equal(returnType, method.ReturnType);
        return method;
    }

    [Fact]
    public void StartupCoordinator_DoesNotReintroduceLiveNewsBlockingIntoSceneBuild()
    {
        string source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "src", "PortfolioSaver.Presentation", "Services", "StartupCoordinator.cs"));

        Assert.DoesNotContain("Task<IReadOnlyList<string>> headlinesTask", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await Task.WhenAll(quotesTask, backgroundsTask, headlinesTask);", source, StringComparison.Ordinal);
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


