using System.Windows;
using System.Windows.Media;
using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Render.Services;

public sealed class TapeAnimationController
{
    private UIElement? _element;
    private TranslateTransform? _transform;
    private double _cycleDistance;
    private double _pixelsPerSecond;
    private double _progress;
    private double _anchorOffset;
    private ScrollDirection _direction = ScrollDirection.Left;
    private DateTime _lastFrameUtc = DateTime.UtcNow;
    private bool _running;

    public void Attach(UIElement element)
    {
        if (ReferenceEquals(_element, element))
            return;

        _element = element;
        _transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = _transform;
        ApplyOffset();
    }

    public void Update(double cycleDistance, double pixelsPerSecond, ScrollDirection direction, double anchorOffset = 0d)
    {
        _cycleDistance = Math.Max(1d, cycleDistance);
        _pixelsPerSecond = Math.Max(1d, pixelsPerSecond);
        _direction = direction;
        _anchorOffset = anchorOffset;
        NormalizeProgress();

        ApplyOffset();
    }

    public void Start()
    {
        if (_running)
            return;

        _running = true;
        _lastFrameUtc = DateTime.UtcNow;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        if (!_running)
            return;

        CompositionTarget.Rendering -= OnRendering;
        _running = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_transform is null || _cycleDistance <= 0 || _pixelsPerSecond <= 0)
            return;

        DateTime now = DateTime.UtcNow;
        double elapsedSeconds = Math.Max(0.001d, (now - _lastFrameUtc).TotalSeconds);
        _lastFrameUtc = now;

        _progress += _pixelsPerSecond * elapsedSeconds;
        NormalizeProgress();

        ApplyOffset();
    }

    private void NormalizeProgress()
    {
        if (_cycleDistance <= 0)
            return;

        _progress %= _cycleDistance;
        if (_progress < 0d)
            _progress += _cycleDistance;
    }

    private void ApplyOffset()
    {
        if (_transform is not null)
            _transform.X = _anchorOffset + (_direction == ScrollDirection.Right ? _progress : -_progress);
    }
}
