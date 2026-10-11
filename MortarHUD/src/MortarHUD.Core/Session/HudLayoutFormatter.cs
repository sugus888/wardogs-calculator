using System.Globalization;
using MortarHUD.Core.Ballistics;
using MortarHUD.Localization;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Themes;

namespace MortarHUD.Core.Session;

/// <summary>
/// 把 <see cref="MortarSession"/> 的内容排成 HUD 要显示的行（TDD §20 / §25）。
/// </summary>
/// <remarks>
/// 放在 Core 而不是 App，是为了让布局能被单元测试覆盖——
/// 数值格式（尤其是方位角的补零）很容易写错，而且错了在游戏里不一定看得出来。
/// </remarks>
public static class HudLayoutFormatter
{
    /// <summary>方位角的整数位固定补到 3 位，避免 9.0° 和 100.0° 抖动时整行宽度跳变。</summary>
    private const int BearingIntegerDigits = 3;

    public static string FormatBearing(double degrees, int decimals)
    {
        decimals = Math.Clamp(decimals, 0, 3);

        // 360.0 会被四舍五入显示成 360——那不在 [0,360) 里，压回 0。
        if (degrees >= 360.0)
        {
            degrees -= 360.0;
        }

        var text = degrees.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);

        var targetWidth = BearingIntegerDigits + (decimals > 0 ? 1 + decimals : 0);
        return text.PadLeft(targetWidth, '0') + "°";
    }

    public static string FormatRange(double meters, int decimals)
    {
        decimals = Math.Clamp(decimals, 0, 3);
        return meters.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture) + "m";
    }

    public static string FormatCoordinate(double value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>按布局与显示字段生成正文行。</summary>
    public static IReadOnlyList<HudLine> BuildLines(MortarSession session, HudSettings hud)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(hud);

        var theme = hud.CurrentTheme;
        var bearing = FormatBearing(session.Solution.BearingDegrees, hud.BearingDecimals);
        var range = FormatRange(session.Solution.RangeMeters, hud.RangeDecimals);

        // 还没解算完就不要显示 000.0° / 0m，那是误导。
        var hasSolution = session is { Gun: not null, Target: not null };

        var lines = theme.Layout switch
        {
            HudLayout.Minimal => BuildMinimal(hud, hasSolution, bearing, range),
            HudLayout.Horizontal => BuildHorizontal(hud, hasSolution, bearing, range),
            HudLayout.Detailed => BuildDetailed(session, hud, hasSolution, bearing, range),
            _ => BuildCompact(hud, hasSolution, bearing, range),
        };

        return AppendWeapon(lines, session, hud, theme.Layout, hasSolution);
    }

    // ------------------------------------------------------------ 武器与 MIL（ซู-ปืนครก）

    /// <summary>
    /// 一行 MIL：标签 + 数值。L81 只有一张表（MIL），SPH-2 有低伸（LO）和高抛（HI）两张。
    /// </summary>
    /// <remarks>
    /// 标签都控制在 3 个字符宽（"MIL"、"LO "、"HI "），数值与 AZ / RNG 那两行对齐。
    /// </remarks>
    public static IReadOnlyList<HudSegment> BuildMilSegments(WeaponProfile weapon, ElevationSolution elevation, bool hasSolution)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(elevation);

        var rows = new List<(string Label, int Gap, BallisticTable? Table, MilSolution? Value)>(2);

        if (weapon.Single is not null)
        {
            rows.Add(("MIL", 1, weapon.Single, elevation.Single));
        }

        if (weapon.Low is not null)
        {
            rows.Add(("LO", 2, weapon.Low, elevation.Low));
        }

        if (weapon.High is not null)
        {
            rows.Add(("HI", 2, weapon.High, elevation.High));
        }

        var segments = new List<HudSegment>(rows.Count);

        foreach (var (label, gap, _, value) in rows)
        {
            if (!hasSolution)
            {
                segments.Add(new HudSegment(label, "--", HudSegmentKind.Primary) { Gap = gap });
            }
            else if (!elevation.InRange)
            {
                // 射程外是有用的信息，不能和「还没解算」一样显示 "--"。
                segments.Add(new HudSegment(label, Loc.T("OutOfRangeShort"), HudSegmentKind.Status) { Gap = gap });
            }
            else
            {
                segments.Add(new HudSegment(label, value?.Format() ?? "--", HudSegmentKind.Primary) { Gap = gap });
            }
        }

        return segments;
    }

    private static IReadOnlyList<HudLine> AppendWeapon(
        IReadOnlyList<HudLine> lines, MortarSession session, HudSettings hud, HudLayout layout, bool hasSolution)
    {
        if (!hud.ShowMil || session.Weapon is not { } weapon)
        {
            return lines;
        }

        var mil = BuildMilSegments(weapon, session.Elevation, hasSolution);
        var result = new List<HudLine>(lines.Count + mil.Count + 1);

        switch (layout)
        {
            case HudLayout.Minimal:
                // 极简：只有数值。武器名也不显示，切换武器时状态行会提示。
                result.AddRange(lines);
                result.AddRange(mil.Select(m => new HudLine([new HudSegment("", m.Value, m.Kind)])));
                break;

            case HudLayout.Horizontal:
            {
                var segments = new List<HudSegment>
                {
                    new(weapon.ShortName, "", HudSegmentKind.Secondary) { Gap = 0 },
                };

                foreach (var line in lines)
                {
                    segments.Add(new HudSegment("   ", "", HudSegmentKind.Secondary) { Gap = 0 });
                    segments.AddRange(line.Segments);
                }

                foreach (var m in mil)
                {
                    segments.Add(new HudSegment("   ", "", HudSegmentKind.Secondary) { Gap = 0 });
                    segments.Add(m);
                }

                result.Add(new HudLine(segments));
                break;
            }

            default:
                result.Add(new HudLine([new HudSegment(weapon.ShortName, "", HudSegmentKind.Secondary) { Gap = 0 }]));
                result.AddRange(lines);
                result.AddRange(mil.Select(m => new HudLine([m])));
                break;
        }

        return result;
    }

    private static IReadOnlyList<HudLine> BuildMinimal(
        HudSettings hud, bool hasSolution, string bearing, string range)
    {
        var lines = new List<HudLine>(2);

        if (hud.ShowAz)
        {
            lines.Add(new HudLine([new HudSegment("", hasSolution ? bearing : "--", HudSegmentKind.Primary)]));
        }

        if (hud.ShowRng)
        {
            lines.Add(new HudLine([new HudSegment("", hasSolution ? range : "--", HudSegmentKind.Primary)]));
        }

        return lines;
    }

    private static IReadOnlyList<HudLine> BuildCompact(
        HudSettings hud, bool hasSolution, string bearing, string range)
    {
        var lines = new List<HudLine>(2);

        if (hud.ShowAz)
        {
            lines.Add(new HudLine(
                [new HudSegment("AZ", hasSolution ? bearing : "--", HudSegmentKind.Primary) { Gap = 2 }]));
        }

        if (hud.ShowRng)
        {
            lines.Add(new HudLine(
                [new HudSegment("RNG", hasSolution ? range : "--", HudSegmentKind.Primary)]));
        }

        return lines;
    }

    private static IReadOnlyList<HudLine> BuildHorizontal(
        HudSettings hud, bool hasSolution, string bearing, string range)
    {
        var segments = new List<HudSegment>(3);

        if (hud.ShowAz)
        {
            segments.Add(new HudSegment("AZ", hasSolution ? bearing : "--", HudSegmentKind.Primary) { Gap = 1 });
        }

        if (hud.ShowRng)
        {
            if (segments.Count > 0)
            {
                segments.Add(new HudSegment("   ", "", HudSegmentKind.Secondary) { Gap = 0 });
            }

            segments.Add(new HudSegment("RNG", hasSolution ? range : "--", HudSegmentKind.Primary));
        }

        return segments.Count == 0 ? [] : [new HudLine(segments)];
    }

    private static IReadOnlyList<HudLine> BuildDetailed(
        MortarSession session, HudSettings hud, bool hasSolution, string bearing, string range)
    {
        var lines = new List<HudLine>(4);

        if (hud.ShowGun && session.Gun is { } gun)
        {
            lines.Add(new HudLine(
            [
                new HudSegment("GUN", "", HudSegmentKind.Secondary) { Gap = 4 },
                new HudSegment("", FormatCoordinate(gun.X), HudSegmentKind.Primary),
                new HudSegment(" ", "", HudSegmentKind.Primary) { Gap = 0 },
                new HudSegment("", FormatCoordinate(gun.Y), HudSegmentKind.Primary),
            ]));
        }

        if (hud.ShowTarget && session.Target is { } target)
        {
            lines.Add(new HudLine(
            [
                new HudSegment("TGT", "", HudSegmentKind.Secondary) { Gap = 4 },
                new HudSegment("", FormatCoordinate(target.X), HudSegmentKind.Primary),
                new HudSegment(" ", "", HudSegmentKind.Primary) { Gap = 0 },
                new HudSegment("", FormatCoordinate(target.Y), HudSegmentKind.Primary),
            ]));
        }

        if (hud.ShowAz)
        {
            lines.Add(new HudLine(
                [new HudSegment("AZ", hasSolution ? bearing : "--", HudSegmentKind.Primary) { Gap = 1 }]));
        }

        if (hud.ShowRng)
        {
            lines.Add(new HudLine(
                [new HudSegment("RNG", hasSolution ? range : "--", HudSegmentKind.Primary)]));
        }

        return lines;
    }

    /// <summary>把状态枚举翻成 HUD 上显示的短句（TDD §20 / §38）。</summary>
    public static string DescribeStatus(MortarStatusKind status) => status switch
    {
        MortarStatusKind.GunLocked => Loc.T("GunLocked"),
        MortarStatusKind.TargetLocked => Loc.T("TargetLocked"),
        MortarStatusKind.OcrFailed => Loc.T("RecognitionFailed"),
        MortarStatusKind.NoGunPosition => Loc.T("NoGunRecorded"),
        MortarStatusKind.InvalidCoordinate => Loc.T("CoordinateOutOfRange"),
        MortarStatusKind.CaptureFailed => Loc.T("CaptureFailed"),
        MortarStatusKind.CaptureCancelled => Loc.T("CursorMovedCancelled"),
        MortarStatusKind.AutoCalibrateSkipped => Loc.T("AutoCalibrationNotReady"),
        MortarStatusKind.WeaponChanged => Loc.T("WeaponChanged"),
        _ => "",
    };

    /// <summary>
    /// 失败提示的副行。TDD §16 明确要求 OCR 失败时必须让用户看到「目标没变」，
    /// 否则用户会以为新目标已经锁上了。
    /// </summary>
    public static string? DescribeStatusDetail(MortarStatusKind status) => status switch
    {
        MortarStatusKind.OcrFailed => Loc.T("TargetUnchanged"),
        MortarStatusKind.InvalidCoordinate => Loc.T("TargetUnchanged"),
        MortarStatusKind.CaptureFailed => Loc.T("TargetUnchanged"),
        MortarStatusKind.CaptureCancelled => Loc.T("TargetUnchanged"),
        MortarStatusKind.AutoCalibrateSkipped => Loc.T("AutoCalibrationRetryHint"),
        _ => null,
    };
}
