using System.Globalization;

namespace MortarHUD.Localization;

/// <summary>
/// 程序名与版本号的唯一来源。
/// </summary>
/// <remarks>
/// 版本号来自仓库根 <c>Directory.Build.props</c> 的 <c>&lt;Version&gt;</c>，所有项目共用同一个值，
/// 所以这里读本程序集的版本即可。显示格式为 "V主.次"（例如 V0.1），
/// 每次发布新版本时只改 Directory.Build.props 一处。
/// </remarks>
public static class AppInfo
{
    /// <summary>程序名（不带版本号）。</summary>
    public const string Name = "ซู-ปืนครก";

    private static readonly Lazy<string> VersionText = new(() =>
    {
        var version = typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 1);
        return string.Create(CultureInfo.InvariantCulture, $"V{version.Major}.{version.Minor}");
    });

    /// <summary>显示用版本号，例如 "V0.1"。</summary>
    public static string Version => VersionText.Value;

    /// <summary>窗口标题、托盘等处显示的完整名字，例如 "ซู-ปืนครก V0.1"。</summary>
    public static string DisplayName => $"{Name} {Version}";
}
