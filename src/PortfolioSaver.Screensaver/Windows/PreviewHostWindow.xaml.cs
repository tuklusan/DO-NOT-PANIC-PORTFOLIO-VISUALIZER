using System.Windows;
using System.Windows.Interop;
using PortfolioSaver.Screensaver.Services;

namespace PortfolioSaver.Screensaver.Windows;

public partial class PreviewHostWindow : Window
{
    private readonly IntPtr _previewHandle;

    public PreviewHostWindow(IntPtr previewHandle)
    {
        _previewHandle = previewHandle;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_previewHandle == IntPtr.Zero)
            return;

        WindowInteropHelper helper = new(this);
        NativeMethods.SetParent(helper.Handle, _previewHandle);
        if (NativeMethods.GetClientRect(_previewHandle, out NativeMethods.RECT rect))
        {
            Width = rect.Right - rect.Left;
            Height = rect.Bottom - rect.Top;
        }
    }
}
