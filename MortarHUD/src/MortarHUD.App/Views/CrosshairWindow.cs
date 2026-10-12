using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Crosshair;
using MortarHUD.Platform.Windows.WindowStyles;

namespace MortarHUD.App.Views;

/// <summary>
/// เป้ากลางจอ：一个小的透明、置顶、鼠标穿透窗口，正中心对准游戏窗口客户区中心。
/// </summary>
/// <remarks>
/// 窗口本身只有 <see cref="SidePixels"/> 见方，不铺满全屏——分层窗口越大，合成开销越高。
/// 与 <see cref="SightRulerWindow"/> 一样从不抢焦点。
/// </remarks>
public sealed class CrosshairWindow : Window
{
    /// <summary>窗口边长（物理像素），足够容纳最大尺寸 + 最大偏移。</summary>
    public const int SidePixels = 2 * (int)(CrosshairSettings.MaxOffset + CrosshairSettings.MaxGap
                                            + CrosshairSettings.MaxSize + CrosshairSettings.MaxThickness + 8);

    private IntPtr _handle;
    private System.Drawing.Rectangle _bounds;

    public CrosshairRenderer Renderer { get; } = new();

    public CrosshairWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        IsHitTestVisible = false;
        Title = "ซู-ปืนครก crosshair";
        Content = Renderer;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        OverlayWindowController.ApplyOverlayStyles(_handle, true);
    }

    /// <summary>以 <paramref name="client"/>（游戏客户区，物理像素）中心为准显示准星。</summary>
    public void Display(System.Drawing.Rectangle client, CrosshairSettings settings)
    {
        if (!IsVisible)
        {
            Show();
        }

        var bounds = new System.Drawing.Rectangle(
            client.X + client.Width / 2 - SidePixels / 2,
            client.Y + client.Height / 2 - SidePixels / 2,
            SidePixels,
            SidePixels);

        if (_bounds != bounds)
        {
            _bounds = bounds;
            OverlayWindowController.PlaceInClientBounds(_handle, bounds);
        }

        Renderer.Update(settings);
        OverlayWindowController.ShowWithoutActivating(_handle);
    }
}

/// <summary>照 <see cref="CrosshairGeometry"/> 画准星。设置里的像素按当前 DPI 换算成 WPF 的 DIP。</summary>
public sealed class CrosshairRenderer : FrameworkElement
{
    private CrosshairSettings _settings = new();

    public void Update(CrosshairSettings settings)
    {
        _settings = settings.Clone();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var shape = CrosshairGeometry.Build(_settings);
        var dpi = VisualTreeHelper.GetDpi(this);
        var scale = 1.0 / Math.Max(dpi.DpiScaleX, 0.01);
        var cx = ActualWidth / 2;
        var cy = ActualHeight / 2;

        var color = HudRenderer.ParseColor(_settings.Color);
        var brush = new SolidColorBrush(color) { Opacity = Math.Clamp(_settings.Opacity, 0.1, 1) };
        brush.Freeze();
        var outline = new SolidColorBrush(Color.FromArgb((byte)(200 * brush.Opacity), 0, 0, 0));
        outline.Freeze();

        var thickness = shape.Thickness * scale;
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        var outlinePen = new Pen(outline, thickness + 2 * scale) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };

        Point P(double x, double y) => new(cx + x * scale, cy + y * scale);

        // 先画描边，再画本体，避免描边盖住相邻线段。
        if (_settings.Outline)
        {
            foreach (var line in shape.Lines)
            {
                dc.DrawLine(outlinePen, P(line.X1, line.Y1), P(line.X2, line.Y2));
            }

            foreach (var circle in shape.Circles)
            {
                var r = circle.Radius * scale;
                if (circle.Filled)
                {
                    dc.DrawEllipse(outline, null, P(circle.CenterX, circle.CenterY), r + scale, r + scale);
                }
                else
                {
                    dc.DrawEllipse(null, outlinePen, P(circle.CenterX, circle.CenterY), r, r);
                }
            }
        }

        foreach (var line in shape.Lines)
        {
            dc.DrawLine(pen, P(line.X1, line.Y1), P(line.X2, line.Y2));
        }

        foreach (var circle in shape.Circles)
        {
            var r = circle.Radius * scale;
            if (circle.Filled)
            {
                dc.DrawEllipse(brush, null, P(circle.CenterX, circle.CenterY), r, r);
            }
            else
            {
                dc.DrawEllipse(null, pen, P(circle.CenterX, circle.CenterY), r, r);
            }
        }
    }
}
