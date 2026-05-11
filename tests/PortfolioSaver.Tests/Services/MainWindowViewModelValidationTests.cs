using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Threading;
using PortfolioSaver.Config.ViewModels;
using PortfolioSaver.Core.Models;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class MainWindowViewModelValidationTests
{
    [Fact]
    public void DisableInvalidSymbols_AutoDisablesMatchingTickers()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();

        TickerItemEditorViewModel badTicker = new(new TickerItem { Symbol = "BAD1", Enabled = true });
        TickerItemEditorViewModel goodTicker = new(new TickerItem { Symbol = "GOOD1", Enabled = true });
        TickerGroupEditorViewModel group = new();
        group.Tickers.Clear();
        group.Tickers.Add(badTicker);
        group.Tickers.Add(goodTicker);
        vm.Groups.Clear();
        vm.Groups.Add(group);

        int disabled = InvokePrivate<int>(vm, "DisableInvalidSymbols", [new[] { "bad1" }]);

        Assert.Equal(1, disabled);
        Assert.False(badTicker.Enabled);
        Assert.Equal(SymbolValidationState.Invalid, badTicker.ValidationState);
        Assert.True(goodTicker.Enabled);
    }

    [Fact]
    public void OnEditorChanged_WithDataEdit_InvalidatesValidatedState()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        InvokePrivate<object?>(vm, "OnEditorChanged", [null, new PropertyChangedEventArgs(nameof(TickerItemEditorViewModel.Symbol))]);

        Assert.False(vm.IsValidated);
        Assert.Equal("Validate", vm.PrimaryButtonText);
        Assert.Equal("Configuration changed. Click Validate.", vm.StatusMessage);
        Assert.Equal(string.Empty, GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.False(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void OnEditorChanged_WithValidationStateUpdate_DoesNotInvalidate()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        InvokePrivate<object?>(vm, "OnEditorChanged", [null, new PropertyChangedEventArgs(nameof(TickerItemEditorViewModel.ValidationState))]);

        Assert.True(vm.IsValidated);
        Assert.Equal("Validate", vm.PrimaryButtonText);
        Assert.Equal("fingerprint", GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.True(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public async Task OnStateTimerTickAsync_DoesNotTriggerBackgroundSymbolValidation()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();

        TickerItemEditorViewModel ticker = new(new TickerItem { Symbol = "AAPL", Enabled = true });
        ticker.ValidationState = SymbolValidationState.Unknown;
        ticker.ValidationMessage = "Pending validation";
        TickerGroupEditorViewModel group = new();
        group.Tickers.Clear();
        group.Tickers.Add(ticker);
        vm.Groups.Clear();
        vm.Groups.Add(group);

        Task task = InvokePrivate<Task>(vm, "OnStateTimerTickAsync", []);
        await task;

        Assert.Equal(SymbolValidationState.Unknown, ticker.ValidationState);
        Assert.Equal("Pending validation", ticker.ValidationMessage);
    }

    private static MainWindowViewModel CreateIsolatedViewModel()
    {
        MainWindowViewModel vm = new();
        DispatcherTimer timer = GetPrivateField<DispatcherTimer>(vm, "_stateTimer");
        timer.Stop();
        return vm;
    }

    private static T InvokePrivate<T>(object instance, string methodName, object?[] args)
    {
        MethodInfo? method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        object? value = method!.Invoke(instance, args);
        return value is null ? default! : (T)value;
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        object? value = field!.GetValue(instance);
        return Assert.IsType<T>(value);
    }
}
