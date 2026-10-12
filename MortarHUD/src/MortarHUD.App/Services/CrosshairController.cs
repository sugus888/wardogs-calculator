using System.Windows.Threading;
using MortarHUD.App.Views;
using MortarHUD.Core.Configuration;
using MortarHUD.Platform.Windows.Mouse;

namespace MortarHUD.App.Services;

/// <summary>
/// 决定准星画在哪个窗口上、什么时候隐藏。
/// </summary>
/// <remarks>
/// <para>
/// 我们不知道游戏进程叫什么，所以按「前台窗口是外部程序、且客户区铺满整个显示器」判断是全屏游戏；
/// 采集过坐标的窗口（<see cref="Follow"/>）也一定算。这样桌面、浏览器之类的普通窗口上不会冒出准星。
/// </para>
/// <para>
/// 快捷菜单打开时前台是我们自己的窗口，此时继续画在上一次的游戏窗口上，方便边调边看。
/// </para>
/// </remarks>
public sealed class CrosshairController : IDisposable
{
    private readonly CrosshairWindow _window = new();
    private readonly Func<MortarHudSettings> _settings;
    private readonly Func<bool> _previewing;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private IntPtr _gameWindow;

    public CrosshairController(Func<MortarHudSettings> settings, Func<bool> previewing)
    {
        _settings = settings;
        _previewing = previewing;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>最近一次确认为游戏的窗口（快捷菜单关闭后把焦点还给它）。</summary>
    public IntPtr GameWindow => _gameWindow;

    public void Follow(IntPtr window)
    {
        if (CaptureContext.IsExternal(window))
        {
            _gameWindow = window;
        }
    }

    private void OnTick(object? sender, EventArgs e) => Refresh();

    public void Refresh()
    {
        var settings = _settings();
        var foreground = CaptureContext.Foreground;

        if (foreground != _gameWindow && FullScreenGame.IsFullScreen(foreground))
        {
            _gameWindow = foreground;
        }

        var target = foreground == _gameWindow || (_previewing() && _gameWindow != IntPtr.Zero)
            ? _gameWindow
            : IntPtr.Zero;

        if (!settings.Crosshair.Enabled || !settings.Hud.Visible || target == IntPtr.Zero
            || !CaptureContext.TryGetClientBounds(target, out var bounds))
        {
            _window.Hide();
            return;
        }

        _window.Display(bounds, settings.Crosshair);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _window.Close();
    }
}
