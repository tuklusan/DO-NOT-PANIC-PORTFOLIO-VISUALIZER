namespace PortfolioSaver.Render.Services;

public sealed class RoamingLayoutController
{
    private readonly Random _random = new();

    public double NextOffset(double maxDistance = 18)
    {
        return (_random.NextDouble() * 2 - 1) * maxDistance;
    }
}
