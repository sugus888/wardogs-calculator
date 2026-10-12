using System.Runtime.InteropServices;

namespace MortarHUD.Platform.Windows.Mouse;

/// <summary>
/// 把我们自己的窗口切到前台，关闭后再把焦点还给游戏（ซู-ปืนครก เมนูด่วน）。
/// </summary>
/// <remarks>
/// Windows 限制后台进程抢前台：直接 SetForegroundWindow 常常只会让任务栏闪烁，
/// 窗口却留在全屏游戏后面——这正是「还得把游戏最小化才能改设置」的原因。
/// 标准做法是把我们的输入线程临时挂到当前前台线程上（AttachThreadInput），
/// 再调用 SetForegroundWindow。这不模拟任何键鼠输入，也不碰游戏进程本身。
/// </remarks>
public static class ForegroundWindow
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    /// <summary>尽力把 <paramref name="window"/> 切到前台。成功返回 true。</summary>
    public static bool Activate(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window))
        {
            return false;
        }

        if (SetForegroundWindow(window) && GetForegroundWindow() == window)
        {
            return true;
        }

        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread
                       && AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        return GetForegroundWindow() == window;
    }
}
