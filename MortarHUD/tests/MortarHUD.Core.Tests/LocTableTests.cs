using System.Text.RegularExpressions;
using MortarHUD.Localization;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>
/// 文案表的完整性（TDD §20 的界面文案）。
/// </summary>
/// <remarks>
/// 这些断言看着琐碎，但它们拦的是同一类事故：新加一条文案只填了中文，
/// 英文用户看到一个空串或一个键名。那种错误在中文环境下永远测不出来。
/// </remarks>
[Collection(LocalizationCollection.Name)]
public sealed class LocTableTests
{
    private static readonly Regex Han = new(@"[一-鿿]", RegexOptions.Compiled);

    [Fact]
    public void TableIsNotEmpty()
    {
        Assert.NotEmpty(Strings.All);
    }

    [Fact]
    public void EveryEntryHasBothLanguages()
    {
        var missing = Strings.All
            .Where(e => string.IsNullOrWhiteSpace(e.Value.Zh) || string.IsNullOrWhiteSpace(e.Value.En))
            .Select(e => e.Key)
            .ToList();

        Assert.True(missing.Count == 0, "这些键缺一门外语：" + string.Join(", ", missing));
    }

    /// <summary>英文那一列不该再出现汉字——那多半是复制粘贴时忘了翻。</summary>
    [Fact]
    public void EnglishColumnContainsNoChinese()
    {
        var untranslated = Strings.All
            .Where(e => Han.IsMatch(e.Value.En))
            .Select(e => $"{e.Key} → {e.Value.En}")
            .ToList();

        Assert.True(untranslated.Count == 0, "这些条目英文列还是中文：" + string.Join(" | ", untranslated));
    }

    /// <summary>键在表里查不到时返回键名本身，界面上会露出一个显眼的占位串而不是空白。</summary>
    [Fact]
    public void UnknownKeyFallsBackToTheKeyItself()
    {
        Assert.Equal("NoSuchKeyAtAll", Loc.T("NoSuchKeyAtAll"));
    }

    [Fact]
    public void ThaiIsTheDefaultLanguage()
    {
        // Loc.Language 是静态的，别的测试可能刚切过；这里验的是「默认值」本身。
        Assert.Equal(Loc.Thai, Loc.Normalize(null));
        Assert.Equal(Loc.Thai, Loc.Normalize("th-TH"));
        Assert.Equal(Loc.Thai, new MortarHUD.Core.Configuration.GeneralSettings().Language);
    }

    /// <summary>每个键都必须有泰文，泰文表里也不能有主表没有的键（拼错的键永远用不上）。</summary>
    [Fact]
    public void EveryEntryHasThai()
    {
        var missing = Strings.All.Keys.Where(k => !StringsThai.All.TryGetValue(k, out var th) || string.IsNullOrWhiteSpace(th)).ToList();
        var orphans = StringsThai.All.Keys.Where(k => !Strings.All.ContainsKey(k)).ToList();

        Assert.True(missing.Count == 0, "缺泰文：" + string.Join(", ", missing));
        Assert.True(orphans.Count == 0, "泰文表多出的键：" + string.Join(", ", orphans));
    }

    /// <summary>泰文的占位符必须与英文完全一致，否则 string.Format 会抛异常或丢参数。</summary>
    [Fact]
    public void ThaiPlaceholdersMatchEnglish()
    {
        var placeholder = new Regex(@"\{[^{}]+\}");
        static string Key(IEnumerable<string> items) => string.Join("|", items.OrderBy(x => x, StringComparer.Ordinal));

        var mismatched = Strings.All
            .Where(e => StringsThai.All.ContainsKey(e.Key))
            .Where(e => Key(placeholder.Matches(e.Value.En).Select(m => m.Value))
                        != Key(placeholder.Matches(StringsThai.All[e.Key]).Select(m => m.Value)))
            .Select(e => e.Key)
            .ToList();

        Assert.True(mismatched.Count == 0, "占位符不一致：" + string.Join(", ", mismatched));
    }

    /// <summary>{App} 会被替换成带版本号的程序名，不能原样露在界面上。</summary>
    [Fact]
    public void AppPlaceholderIsReplacedWithNameAndVersion()
    {
        var text = Loc.T("AppName");

        Assert.Equal(AppInfo.DisplayName, text);
        Assert.StartsWith("ซู-ปืนครก V", text);
        Assert.Matches(@"^V\d+\.\d+$", AppInfo.Version);
    }

    [Fact]
    public void SwitchingLanguageChangesLookup()
    {
        var original = Loc.Language;
        try
        {
            Loc.Language = Loc.English;
            Assert.Equal("Gun locked", Loc.T("GunLocked"));

            Loc.Language = Loc.Chinese;
            Assert.Equal("炮位已锁定", Loc.T("GunLocked"));

            Loc.Language = Loc.Thai;
            Assert.Equal("ล็อกตำแหน่งปืนแล้ว", Loc.T("GunLocked"));
        }
        finally
        {
            Loc.Language = original;
        }
    }

    /// <summary>设置里存的可能是 "en-US" 这类带区域的写法，也得认。</summary>
    [Theory]
    [InlineData("en", true)]
    [InlineData("en-US", true)]
    [InlineData("EN", true)]
    [InlineData("zh-CN", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LanguageCodeIsMatchedLoosely(string? code, bool expected)
    {
        Assert.Equal(expected, Loc.IsEnglishCode(code));
    }

    /// <summary>
    /// 首次启动按系统语言挑默认界面语言：中文系统用中文，其余一律英文。
    /// </summary>
    /// <remarks>
    /// 非中文的系统也给英文而不是中文——这个程序面向国际服玩家，
    /// 一个德语系统的人看到英文比看到中文有用。
    /// </remarks>
    [Theory]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-TW", "zh-CN")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("th-TH", "th")]
    [InlineData("en-US", "th")]
    [InlineData("en-GB", "th")]
    [InlineData("de-DE", "th")]
    [InlineData("", "th")]
    public void SystemLanguagePicksChineseOnlyForChineseOtherwiseThai(string culture, string expected)
    {
        var info = string.IsNullOrEmpty(culture)
            ? System.Globalization.CultureInfo.InvariantCulture
            : new System.Globalization.CultureInfo(culture);

        Assert.Equal(expected, SystemLanguage.Detect(info));
    }

    /// <summary>占位符模板走 string.Format，参数按顺序填进去。</summary>
    [Fact]
    public void FormatFillsPlaceholders()
    {
        var original = Loc.Language;
        try
        {
            Loc.Language = Loc.English;
            Assert.Equal("Applied at 12:34:56.", Loc.F("AppliedAt", new DateTime(2026, 1, 1, 12, 34, 56)));

            Loc.Language = Loc.Chinese;
            Assert.Equal("已应用（12:34:56）。", Loc.F("AppliedAt", new DateTime(2026, 1, 1, 12, 34, 56)));

            Loc.Language = Loc.Thai;
            Assert.Equal("นำไปใช้แล้วเมื่อ 12:34:56", Loc.F("AppliedAt", new DateTime(2026, 1, 1, 12, 34, 56)));
        }
        finally
        {
            Loc.Language = original;
        }
    }
}
