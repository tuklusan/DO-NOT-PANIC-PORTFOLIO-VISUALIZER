using System.Diagnostics;
using System.Windows;

namespace PortfolioSaver.Config.Windows;

public partial class DocumentWindow : Window
{
    private readonly string? _linkUrl;

    public DocumentWindow(string title, string body, string? linkUrl = null, string? linkButtonText = null)
    {
        InitializeComponent();
        Title = title;
        DocumentTextBlock.Text = body ?? string.Empty;
        _linkUrl = string.IsNullOrWhiteSpace(linkUrl) ? null : linkUrl.Trim();

        if (!string.IsNullOrWhiteSpace(_linkUrl))
        {
            OpenLinkButton.Visibility = Visibility.Visible;
            if (!string.IsNullOrWhiteSpace(linkButtonText))
                OpenLinkButton.Content = linkButtonText;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_linkUrl))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _linkUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to open link:{Environment.NewLine}{_linkUrl}{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Open Link Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
