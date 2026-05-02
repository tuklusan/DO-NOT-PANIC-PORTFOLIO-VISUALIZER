using PortfolioSaver.Shared;

namespace PortfolioSaver.Desktop.ViewModels;

public sealed class DesktopShellViewModel
{
    public string Title => PortfolioVersion.DisplayName;
    public string ProductName => PortfolioVersion.ProductName;
    public string Version => PortfolioVersion.SemanticVersion;
    public string BaselineLabel => PortfolioVersion.BaselineLabel;
}
