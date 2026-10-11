namespace MortarHUD.Localization;

/// <summary>
/// 界面文案的查表入口。
/// </summary>
/// <remarks>
/// <para>
/// 键是稳定的 PascalCase 标识符，中英文在 <see cref="Strings"/> 里成对存放，
/// 泰文在 <see cref="StringsThai"/> 里单独一张表。<c>LocTableTests</c> 会校验三列都不为空。
/// </para>
/// <para>
/// 语言由设置里的 <c>general.language</c> 驱动，启动时设定一次。
/// 取不到译文时依次回落：泰文 → 英文 → 中文，漏翻一条不该让程序起不来。
/// </para>
/// <para>
/// <strong>日志不走这里。</strong>日志是排查用的，保持中文；
/// 全项目只有面向用户看得见的那部分文案需要本地化。
/// </para>
/// </remarks>
public static class Loc
{
    /// <summary>中文。</summary>
    public const string Chinese = "zh-CN";

    /// <summary>英文。</summary>
    public const string English = "en";

    /// <summary>泰文（ซู-ปืนครก 的默认语言）。</summary>
    public const string Thai = "th";

    private static string _language = Thai;

    /// <summary>当前语言。"en*" → 英文，"zh*" → 中文，其余一律泰文。</summary>
    public static string Language
    {
        get => _language;
        set => _language = Normalize(value);
    }

    /// <summary>把设置里存的语言代码归一到三种之一。大小写与区域后缀都不敏感。</summary>
    public static string Normalize(string? language)
    {
        if (IsEnglishCode(language))
        {
            return English;
        }

        return !string.IsNullOrWhiteSpace(language)
               && language.TrimStart().StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? Chinese
            : Thai;
    }

    /// <summary>判断一个语言代码是不是英文。大小写与区域后缀都不敏感。</summary>
    public static bool IsEnglishCode(string? language)
        => !string.IsNullOrWhiteSpace(language)
           && language.TrimStart().StartsWith("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>当前是不是英文。</summary>
    public static bool IsEnglish => _language == English;

    /// <summary>当前是不是泰文。</summary>
    public static bool IsThai => _language == Thai;

    /// <summary>按当前语言取文案。文案里的 <c>{App}</c> 会替换成带版本号的程序名。</summary>
    public static string T(string key) => Strings.Lookup(key, _language).Replace("{App}", AppInfo.DisplayName);

    /// <summary>
    /// 取带占位符的文案并填充参数，格式沿用 <see cref="string.Format(string, object[])"/>。
    /// </summary>
    /// <remarks>
    /// 原来这些地方是 <c>$"已应用（{DateTime.Now:HH:mm:ss}）。"</c> 这样的内插字符串，
    /// 没法直接换成本地化调用——模板必须整条查表，参数另外传进来。
    /// </remarks>
    public static string F(string key, params object?[] args) => string.Format(T(key), args);
}
