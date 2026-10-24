using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DeskDig.Core;
using DeskDig.Interop;

namespace DeskDig.Palette;

/// <summary>
/// DWM ライブサムネイルを自分の矩形に描かせる要素。レイアウトが変わるたびに宛先矩形を更新し、
/// ScrollViewer のビューポート外は切り取る。
/// </summary>
internal sealed class ThumbnailSurface : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(WindowId), typeof(ThumbnailSurface),
        new PropertyMetadata(default(WindowId), (d, _) => ((ThumbnailSurface)d).Reattach()));

    private DwmThumbnail? _thumbnail;
    private Window? _window;
    private ScrollViewer? _viewport;

    public WindowId Source
    {
        get => (WindowId)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public ThumbnailSurface()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        IsVisibleChanged += (_, _) => Update();
    }

    private void Reattach()
    {
        if (!IsLoaded) return;
        Detach();
        Attach();
    }

    private void Attach()
    {
        if (_thumbnail is not null || Source.Value == 0) return;
        _window = Window.GetWindow(this);
        if (_window is null) return;
        nint hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == 0) return;
        _thumbnail = DwmThumbnail.Register(hwnd, Source);
        if (_thumbnail is null) return;
        _viewport = FindAncestor<ScrollViewer>(this);
        _window.LayoutUpdated += OnLayoutUpdated;
        Update();
    }

    public void Detach()
    {
        if (_window is not null) _window.LayoutUpdated -= OnLayoutUpdated;
        _thumbnail?.Dispose();
        _thumbnail = null;
        _window = null;
        _viewport = null;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => Update();

    private void Update()
    {
        if (_thumbnail is null || _window is null) return;
        if (!IsVisible || RenderSize.Width <= 0 || RenderSize.Height <= 0 || PresentationSource.FromVisual(this) is not { RootVisual: Visual root } source)
        {
            _thumbnail.Hide();
            return;
        }

        var sourceSize = _thumbnail.SourceSize();
        if (sourceSize is null || sourceSize.Value.IsEmpty)
        {
            _thumbnail.Hide();
            return;
        }

        Rect box;
        try
        {
            box = TransformToAncestor(root).TransformBounds(new Rect(RenderSize));
        }
        catch (InvalidOperationException)
        {
            _thumbnail.Hide();
            return;
        }

        // 元のアスペクト比を保って box に収める
        double srcW = sourceSize.Value.Width, srcH = sourceSize.Value.Height;
        double scale = Math.Min(box.Width / srcW, box.Height / srcH);
        double fitW = srcW * scale, fitH = srcH * scale;
        var fitted = new Rect(box.X + (box.Width - fitW) / 2, box.Y + (box.Height - fitH) / 2, fitW, fitH);

        Rect clip = fitted;
        if (_viewport is not null)
        {
            try
            {
                var viewportRect = _viewport.TransformToAncestor(root).TransformBounds(new Rect(new Size(_viewport.ViewportWidth, _viewport.ViewportHeight)));
                clip.Intersect(viewportRect);
            }
            catch (InvalidOperationException)
            {
                // ビューポートの座標が取れない場合は切り取らない
            }
        }
        if (clip.IsEmpty || clip.Width < 1 || clip.Height < 1)
        {
            _thumbnail.Hide();
            return;
        }

        // 切り取られた分だけ元画像側も比例して切り取る
        double sx = srcW / fitted.Width, sy = srcH / fitted.Height;
        var sourceCrop = new Bounds(
            (int)Math.Round((clip.X - fitted.X) * sx),
            (int)Math.Round((clip.Y - fitted.Y) * sy),
            Math.Max(1, (int)Math.Round(clip.Width * sx)),
            Math.Max(1, (int)Math.Round(clip.Height * sy)));

        var toDevice = source.CompositionTarget.TransformToDevice;
        var dest = new Bounds(
            (int)Math.Round(clip.X * toDevice.M11),
            (int)Math.Round(clip.Y * toDevice.M22),
            Math.Max(1, (int)Math.Round(clip.Width * toDevice.M11)),
            Math.Max(1, (int)Math.Round(clip.Height * toDevice.M22)));

        _thumbnail.Show(dest, sourceCrop);
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
