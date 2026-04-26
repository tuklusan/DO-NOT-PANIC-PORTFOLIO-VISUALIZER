namespace PortfolioSaver.Render.ViewModels;

public sealed class NetworkWaitingViewModel : FloatingSpriteViewModel
{
    private string _titleText = "Waiting for network";
    private string _detailText = "Retrying live data soon.";

    public string TitleText
    {
        get => _titleText;
        set => SetProperty(ref _titleText, value);
    }

    public string DetailText
    {
        get => _detailText;
        set => SetProperty(ref _detailText, value);
    }
}
