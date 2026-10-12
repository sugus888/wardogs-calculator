using MortarHUD.Core.Configuration;

namespace MortarHUD.Core.Crosshair;

/// <summary>一段线（以准星中心为原点，像素）。</summary>
public readonly record struct CrosshairLine(double X1, double Y1, double X2, double Y2);

/// <summary>一个圆（以准星中心为原点，像素）。<see cref="Filled"/> 为 false 时只描边。</summary>
public readonly record struct CrosshairCircle(double CenterX, double CenterY, double Radius, bool Filled);

/// <summary>准星由哪些线和圆组成。</summary>
public sealed record CrosshairShape(IReadOnlyList<CrosshairLine> Lines, IReadOnlyList<CrosshairCircle> Circles, double Thickness);

/// <summary>
/// 把 <see cref="CrosshairSettings"/> 换算成几何图形。放在 Core 里以便单元测试，
/// App 侧只负责照着画。
/// </summary>
public static class CrosshairGeometry
{
    public static CrosshairShape Build(CrosshairSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var s = settings.Clone();
        s.Clamp();

        var lines = new List<CrosshairLine>(4);
        var circles = new List<CrosshairCircle>(2);
        var (ox, oy) = (s.OffsetX, s.OffsetY);

        if (s.Style is CrosshairStyle.Cross or CrosshairStyle.CrossDot)
        {
            var inner = s.Gap;
            var outer = s.Gap + s.Size;
            lines.Add(new(ox - outer, oy, ox - inner, oy));
            lines.Add(new(ox + inner, oy, ox + outer, oy));
            lines.Add(new(ox, oy - outer, ox, oy - inner));
            lines.Add(new(ox, oy + inner, ox, oy + outer));
        }

        if (s.Style is CrosshairStyle.Circle)
        {
            circles.Add(new(ox, oy, s.Size, Filled: false));
        }

        if (s.Style is CrosshairStyle.Dot or CrosshairStyle.CrossDot or CrosshairStyle.Circle)
        {
            circles.Add(new(ox, oy, s.Thickness, Filled: true));
        }

        return new CrosshairShape(lines, circles, s.Thickness);
    }
}
