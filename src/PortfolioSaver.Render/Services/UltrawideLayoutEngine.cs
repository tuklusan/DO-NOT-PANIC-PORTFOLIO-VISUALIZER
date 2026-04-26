namespace PortfolioSaver.Render.Services;

public sealed class UltrawideLayoutEngine
{
    public int RecommendTapeCount(double width)
    {
        if (width >= 3200) return 4;
        if (width >= 2400) return 3;
        return 2;
    }
}
