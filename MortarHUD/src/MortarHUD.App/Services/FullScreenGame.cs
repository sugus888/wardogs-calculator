using MortarHUD.Platform.Windows.Mouse;
using MortarHUD.Platform.Windows.WindowStyles;

namespace MortarHUD.App.Services;

/// <summary>
/// 判断前台窗口是不是全屏游戏（ซู-ปืนครก）。
/// </summary>
/// <remarks>
/// 不知道游戏进程名，所以按「外部程序 + 客户区正好铺满所在显示器」判断，
/// 无边框全屏的游戏满足，桌面、浏览器之类的普通窗口不满足。
/// </remarks>
public static class FullScreenGame
{
    public static bool IsFullScreen(IntPtr window)
    {
        if (!CaptureContext.IsExternal(window) || !CaptureContext.TryGetClientBounds(window, out var client))
        {
            return false;
        }

        var monitor = OverlayWindowController.GetMonitorBoundsForPoint(
            client.X + client.Width / 2, client.Y + client.Height / 2);

        return monitor != System.Drawing.Rectangle.Empty && client == monitor;
    }
}
