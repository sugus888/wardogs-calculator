using System.Globalization;

namespace MortarHUD.Localization;

/// <summary>
/// 首次启动时按系统语言挑一个默认界面语言。
/// </summary>
/// <remarks>
/// <para>
/// ซู-ปืนครก 默认泰文：只有中文系统用中文，其余（包括英文 Windows）一律泰文——
/// 很多泰国玩家用的是英文版 Windows，按系统语言判断会把他们错配成英文。
/// </para>
/// <para>
/// 只用在<strong>首次</strong>启动。用户一旦存过设置，界面语言就归他管——
/// 后来换了系统语言也不该把人家挑好的改掉。
/// </para>
/// </remarks>
public static class SystemLanguage
{
    /// <summary>按当前系统的 UI 语言给出默认语言代码。</summary>
    public static string Detect() => Detect(CultureInfo.CurrentUICulture);

    /// <summary>同上，但语言可注入，便于测试。</summary>
    public static string Detect(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? Loc.Chinese
            : Loc.Thai;
    }
}
