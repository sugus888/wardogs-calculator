using MortarHUD.Core.Parsing;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>
/// 坐标解析测试（TDD §42）。
/// </summary>
/// <remarks>
/// 这组用例是「OCR 错误不会静默变成错误坐标」的第一道防线。
/// 特别是拒绝类用例——它们比接受类用例重要得多：
/// 接受失败只是让用户重按一次，而错误接受会让炮弹打到错误的地方，
/// 而且用户不会察觉。
/// </remarks>
public class CoordinateTextParserTests
{
    private static readonly CoordinateTextParser Parser = new();

    // ------------------------------------------------------------ 接受的格式

    [Theory]
    [InlineData("x107.66 y114.54")]
    [InlineData("y114.54 x107.66")]
    [InlineData("X107.66 Y114.54")]
    [InlineData("x107.66\ny114.54")]
    [InlineData("y114.54\nx107.66")]
    public void Parse_BothAxesInAnyOrder_Succeeds(string text)
    {
        var result = Parser.Parse(text);

        Assert.True(result.Success, result.Error);
        Assert.Equal(107.66, result.X!.Value, precision: 9);
        Assert.Equal(114.54, result.Y!.Value, precision: 9);
    }

    [Theory]
    [InlineData("x:107.66 y:114.54")]
    [InlineData("x：107.66 y：114.54")]
    [InlineData("x: 107.66 y: 114.54")]
    [InlineData("x=107.66 y=114.54")]
    public void Parse_SeparatorVariants_Succeeds(string text)
    {
        var result = Parser.Parse(text);

        Assert.True(result.Success, result.Error);
        Assert.Equal(107.66, result.X!.Value, precision: 9);
    }

    /// <summary>
    /// OCR 轻微错认（I/l 当成 1、O/o 当成 0）可以修正，但只发生在数字上下文里。
    /// </summary>
    [Theory]
    [InlineData("xI07.66 yI14.54", 107.66, 114.54)]
    [InlineData("xIO7.66 yII4.54", 107.66, 114.54)]
    [InlineData("xl07.66 yl14.54", 107.66, 114.54)]
    [InlineData("x1O7.66 y11O.O7", 107.66, 110.07)]
    public void Parse_RepairableOcrErrors_Succeeds(string text, double expectedX, double expectedY)
    {
        var result = Parser.Parse(text);

        Assert.True(result.Success, result.Error);
        Assert.Equal(expectedX, result.X!.Value, precision: 9);
        Assert.Equal(expectedY, result.Y!.Value, precision: 9);
    }

    /// <summary>实测 Tesseract 会在轴字母后多吐一个点，例如 "y.109.78"。</summary>
    [Theory]
    [InlineData("y.109.78 x.98.09")]
    [InlineData("-7x98.09 y109.78")]
    [InlineData("y109.78-: x98.09")]
    public void Parse_NoiseAroundAxisLetter_Succeeds(string text)
    {
        var result = Parser.Parse(text);

        Assert.True(result.Success, result.Error);
    }

    // ------------------------------------------------------------ 必须拒绝

    /// <summary>
    /// TDD §15 的核心禁令：不许把 10766 猜成 107.66。
    /// 小数点必须由 OCR 自己读出来。
    /// </summary>
    [Theory]
    [InlineData("x10766 y11454")]
    [InlineData("x10766\ny11454")]
    [InlineData("x 10766 y 11454")]
    public void Parse_MissingDecimalPoint_IsRejected(string text)
    {
        var result = Parser.Parse(text);

        Assert.False(result.Success);
        Assert.Equal("X_NOT_FOUND", result.Error);
    }

    /// <summary>
    /// 小数位被吞掉一位（x98.09 → x98.9）必须拒绝：
    /// 直接当成 98.9 用下去是 81 米的误差，比报错危险得多。
    /// </summary>
    [Theory]
    [InlineData("x98.9 y109.78")]
    [InlineData("x107.6 y114.54")]
    [InlineData("x98.093 y109.78")]
    public void Parse_WrongFractionDigitCount_IsRejected(string text)
    {
        var result = Parser.Parse(text);

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("107.66 114.54")]
    [InlineData("x107.66")]
    [InlineData("y114.54")]
    [InlineData("地图区域 无文字")]
    public void Parse_MissingAxisOrEmpty_IsRejected(string text)
    {
        var result = Parser.Parse(text);

        Assert.False(result.Success);
    }

    /// <summary>
    /// 同一轴出现两个不同数值时判失败：宁可让用户重来，
    /// 也不要赌哪个才是真的（TDD §16 的精神）。
    /// </summary>
    [Fact]
    public void Parse_AmbiguousAxis_IsRejected()
    {
        var result = Parser.Parse("x107.66 x98.09 y114.54");

        Assert.False(result.Success);
        Assert.Equal("AMBIGUOUS_X", result.Error);
    }

    /// <summary>两个数值相同时不算歧义，正常接受。</summary>
    [Fact]
    public void Parse_RepeatedIdenticalAxis_IsAccepted()
    {
        var result = Parser.Parse("x107.66 x107.66 y114.54");

        Assert.True(result.Success, result.Error);
        Assert.Equal(107.66, result.X!.Value, precision: 9);
    }

    /// <summary>
    /// 轴字母左边若紧跟另一个字母，说明它是某个单词的一部分，
    /// 例如地图上的 "Max107.66"。这种不能当成坐标。
    /// </summary>
    [Fact]
    public void Parse_AxisLetterInsideWord_IsIgnored()
    {
        var result = Parser.Parse("Max107.66 May114.54");

        Assert.False(result.Success);
    }

    // ------------------------------------------------------------ 其它

    [Fact]
    public void Parse_NegativeCoordinate_IsAccepted()
    {
        var result = Parser.Parse("x-107.66 y-14.54");

        Assert.True(result.Success, result.Error);
        Assert.Equal(-107.66, result.X!.Value, precision: 9);
        Assert.Equal(-14.54, result.Y!.Value, precision: 9);
    }

    [Fact]
    public void Parse_LookalikeUnicode_Normalized()
    {
        // 全角数字 + 乘号（OCR 常把 x 吐成 ×）
        var result = Parser.Parse("×１０７.６６ ｙ１１４.５４");

        Assert.True(result.Success, result.Error);
        Assert.Equal(107.66, result.X!.Value, precision: 9);
    }

    [Fact]
    public void Parse_ExposesRepairedTextForDiagnostics()
    {
        var result = Parser.Parse("xI07.66 yI14.54");

        Assert.True(result.Success);
        Assert.Contains("x107.66", result.RepairedText, StringComparison.Ordinal);
        Assert.Contains("y114.54", result.RepairedText, StringComparison.Ordinal);
    }

    /// <summary>关掉小数点强制要求后，整数也能被接受——供特殊地图使用。</summary>
    [Fact]
    public void Parse_DecimalPointNotRequired_AcceptsInteger()
    {
        var lenient = new CoordinateTextParser(new CoordinateTextParserOptions
        {
            RequireDecimalPoint = false,
            AllowedFractionDigits = [0, 2],
        });

        var result = lenient.Parse("x107 y114");

        Assert.True(result.Success, result.Error);
        Assert.Equal(107.0, result.X!.Value, precision: 9);
    }

    // ------------------------------------------------------------ ซู-ปืนครก：轴字母丢失

    /// <summary>
    /// 实机日志里最常见的失败：x 字母没识别出来（"y109.78\n98.09"）。
    /// 游戏固定 y 在上、x 在下，y 之后唯一的纯数字行就是 x。
    /// </summary>
    [Theory]
    [InlineData("y109.78\n98.09", 98.09, 109.78)]
    [InlineData("y109.78\n\n98.09", 98.09, 109.78)]
    [InlineData("y122.43\n.44.03", 44.03, 122.43)]
    [InlineData("114.47\nx54.98", 54.98, 114.47)]
    public void Parse_MissingAxisLetter_IsInferredFromLineOrder(string text, double x, double y)
    {
        var result = Parser.Parse(text);

        Assert.True(result.Success, result.Error);
        Assert.Equal(x, result.X!.Value, 3);
        Assert.Equal(y, result.Y!.Value, 3);
    }

    /// <summary>补轴字母时不放松任何规则：整数、多候选、行序不对都不猜。</summary>
    [Theory]
    [InlineData("y109.78\n98")]            // 没有小数点
    [InlineData("y109.78\n98.9")]          // 小数位不对
    [InlineData("y109.78\n98.09\n97.10")] // 两个候选
    [InlineData("98.09\ny109.78")]         // x 应该在 y 下面
    [InlineData("x56.44\n22")]             // y 行缺失且只有整数
    [InlineData("y109.78\nab98.09")]       // 行里有字母，不是纯数字
    public void Parse_MissingAxisLetter_DoesNotGuess(string text)
    {
        Assert.False(Parser.Parse(text).Success);
    }

    [Fact]
    public void Parse_MissingAxisLetter_CanBeDisabled()
    {
        var strict = new CoordinateTextParser(new CoordinateTextParserOptions { InferMissingAxisFromLineOrder = false });

        Assert.Equal("X_NOT_FOUND", strict.Parse("y109.78\n98.09").Error);
    }
}
