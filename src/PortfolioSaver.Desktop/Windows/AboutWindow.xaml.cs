using System.Windows;
using PortfolioSaver.Shared;
using PortfolioSaver.Shared.Licensing;

namespace PortfolioSaver.Desktop.Windows;

public partial class AboutWindow : Window
{
    public string VersionText => $"Version: {PortfolioVersion.SemanticVersion}";
    public string PublisherText => $"Publisher: {AppIdentity.PublisherName}";
    public string AuthorText => $"Author: {AppIdentity.AuthorName}";
    public string LicenseText => $"License: {AppIdentity.LicenseName}";
    public string FullLicenseText => MitLicenseService.GetFallbackMitText();

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
        => Close();
}
