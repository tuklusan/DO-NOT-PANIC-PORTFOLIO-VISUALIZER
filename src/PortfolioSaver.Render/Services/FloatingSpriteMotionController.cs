using System.Windows;
using PortfolioSaver.Render.ViewModels;

namespace PortfolioSaver.Render.Services;

public sealed class FloatingSpriteMotionController
{
    public void Step(FloatingSpriteViewModel sprite, Rect bounds, double elapsedSeconds)
    {
        sprite.X += sprite.VelocityX * elapsedSeconds;
        sprite.Y += sprite.VelocityY * elapsedSeconds;

        if (!sprite.BounceWithinViewport)
            return;

        double minX = bounds.Left;
        double maxX = Math.Max(bounds.Left, bounds.Right - sprite.Width);
        double minY = bounds.Top;
        double maxY = Math.Max(bounds.Top, bounds.Bottom - sprite.Height);

        if (sprite.X <= minX)
        {
            sprite.X = minX;
            sprite.VelocityX = Math.Abs(sprite.VelocityX);
        }
        else if (sprite.X >= maxX)
        {
            sprite.X = maxX;
            sprite.VelocityX = -Math.Abs(sprite.VelocityX);
        }

        if (sprite.Y <= minY)
        {
            sprite.Y = minY;
            sprite.VelocityY = Math.Abs(sprite.VelocityY);
        }
        else if (sprite.Y >= maxY)
        {
            sprite.Y = maxY;
            sprite.VelocityY = -Math.Abs(sprite.VelocityY);
        }
    }
}
