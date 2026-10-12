namespace MortarHUD.Core.Configuration;

/// <summary>เป้ากลางจอ (ซู-ปืนครก)：样式。</summary>
public enum CrosshairStyle
{
    /// <summary>十字（中间留空）。</summary>
    Cross,

    /// <summary>中心圆点。</summary>
    Dot,

    /// <summary>十字 + 中心圆点。</summary>
    CrossDot,

    /// <summary>圆圈 + 中心圆点。</summary>
    Circle,
}

/// <summary>
/// 屏幕中央准星（ซู-ปืนครก V0.3）。
/// </summary>
/// <remarks>
/// 只是一个画在游戏窗口中央、鼠标穿透的透明窗口，不读游戏任何数据。
/// 尺寸单位都是<strong>物理像素</strong>，与游戏画面一致，不随 Windows 缩放变化。
/// </remarks>
public sealed class CrosshairSettings
{
    public bool Enabled { get; set; }

    public CrosshairStyle Style { get; set; } = CrosshairStyle.Cross;

    /// <summary>#RRGGBB。</summary>
    public string Color { get; set; } = "#00FF66";

    /// <summary>十字每条臂的长度 / 圆圈半径（像素）。</summary>
    public double Size { get; set; } = 10;

    /// <summary>线宽（像素）。圆点直径 = 线宽 × 2。</summary>
    public double Thickness { get; set; } = 2;

    /// <summary>十字中心留空的半径（像素）。</summary>
    public double Gap { get; set; } = 4;

    public double Opacity { get; set; } = 0.9;

    /// <summary>黑色描边，亮背景上也看得清。</summary>
    public bool Outline { get; set; } = true;

    /// <summary>相对窗口中心的偏移（像素），用于和游戏自带准星对齐。</summary>
    public double OffsetX { get; set; }

    public double OffsetY { get; set; }

    public const double MinSize = 2, MaxSize = 60;
    public const double MinThickness = 1, MaxThickness = 10;
    public const double MinGap = 0, MaxGap = 30;
    public const double MinOpacity = 0.1, MaxOpacity = 1;
    public const double MaxOffset = 100;

    /// <summary>把所有数值收回合法范围（读坏的设置文件、滑块越界时用）。</summary>
    public void Clamp()
    {
        if (!Enum.IsDefined(Style)) Style = CrosshairStyle.Cross;
        Size = ClampFinite(Size, MinSize, MaxSize, 10);
        Thickness = ClampFinite(Thickness, MinThickness, MaxThickness, 2);
        Gap = ClampFinite(Gap, MinGap, MaxGap, 4);
        Opacity = ClampFinite(Opacity, MinOpacity, MaxOpacity, 0.9);
        OffsetX = ClampFinite(OffsetX, -MaxOffset, MaxOffset, 0);
        OffsetY = ClampFinite(OffsetY, -MaxOffset, MaxOffset, 0);
        if (string.IsNullOrWhiteSpace(Color)) Color = "#00FF66";
    }

    private static double ClampFinite(double value, double min, double max, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public static CrosshairSettings CreateDefault() => new();

    public CrosshairSettings Clone() => (CrosshairSettings)MemberwiseClone();
}
