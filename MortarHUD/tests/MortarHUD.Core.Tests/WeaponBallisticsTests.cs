using MortarHUD.Core.Ballistics;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Models;
using MortarHUD.Core.Session;
using MortarHUD.Core.Themes;
using MortarHUD.Localization;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>
/// 射表与 MIL 显示（ซู-ปืนครก）。
/// </summary>
/// <remarks>
/// 期望值不是手算的：由 wardogs-artillery.com 自己的 <c>js/features/weapons.js</c>
/// （getWeaponElevationSolutions + formatMilSolution）对同一份 data/weapons.json 跑出来。
/// HUD 和网页对同一距离必须给出完全相同的 MIL。
/// </remarks>
[Collection(LocalizationCollection.Name)]
public sealed class WeaponBallisticsTests
{
    [Theory]
    [InlineData("mortar", 100, false, null, null, null)]
    [InlineData("mortar", 131.9, false, null, null, null)]
    [InlineData("mortar", 132, true, "850", null, null)]
    [InlineData("mortar", 150, true, "831", null, null)]
    [InlineData("mortar", 152.3, true, "829", null, null)]
    [InlineData("mortar", 200, true, "788", null, null)]
    [InlineData("mortar", 255.5, true, "735", null, null)]
    [InlineData("mortar", 327, true, "662", null, null)]
    [InlineData("mortar", 400, true, "583", null, null)]
    [InlineData("mortar", 500, true, "461", null, null)]
    [InlineData("mortar", 600, true, "315", null, null)]
    [InlineData("mortar", 683.9, true, "150", null, null)]
    [InlineData("mortar", 684, true, "150", null, null)]
    [InlineData("mortar", 684.1, false, null, null, null)]
    [InlineData("mortar", 700, false, null, null, null)]
    [InlineData("spg", 700, false, null, null, null)]
    [InlineData("spg", 780, true, null, null, "1390")]
    [InlineData("spg", 900, true, null, null, "1363")]
    [InlineData("spg", 1181, true, null, "20", "1296")]
    [InlineData("spg", 1200, true, null, "24", "1291")]
    [InlineData("spg", 1500, true, null, "84", "1213")]
    [InlineData("spg", 1800.4, true, null, "152", "1124")]
    [InlineData("spg", 2000, true, null, "206", "1057")]
    [InlineData("spg", 2400, true, null, "359", "877")]
    [InlineData("spg", 2628, true, null, "590", "630")]
    [InlineData("spg", 2628.5, true, null, "595", "625")]
    [InlineData("spg", 2629, true, null, "600", "610–620")]
    [InlineData("spg", 2629.1, false, null, null, null)]
    public void MatchesTheWebsiteCalculator(string weaponId, double distance, bool inRange, string? single, string? low, string? high)
    {
        var weapon = WeaponCatalog.Find(weaponId);
        Assert.Equal(weaponId, weapon.Id);

        var solution = weapon.Solve(distance);

        Assert.Equal(inRange, solution.InRange);
        Assert.Equal(single, solution.Single?.Format());
        Assert.Equal(low, solution.Low?.Format());
        Assert.Equal(high, solution.High?.Format());
    }

    [Fact]
    public void CatalogHasL81AndSph2()
    {
        Assert.Contains(WeaponCatalog.All, w => w is { Id: "mortar", ShortName: "L81" });
        Assert.Contains(WeaponCatalog.All, w => w is { Id: "spg", ShortName: "SPH-2" });
    }

    [Fact]
    public void UnknownWeaponFallsBackToL81()
    {
        Assert.Equal("mortar", WeaponCatalog.Find("no-such-weapon").Id);
        Assert.Equal("mortar", WeaponCatalog.Find(null).Id);
    }

    [Fact]
    public void NextCyclesThroughAllWeaponsAndWraps()
    {
        var first = WeaponCatalog.All[0];
        var seen = new List<string>();
        var current = first.Id;

        for (var i = 0; i < WeaponCatalog.All.Count; i++)
        {
            current = WeaponCatalog.Next(current).Id;
            seen.Add(current);
        }

        Assert.Equal(first.Id, current);
        Assert.Equal(WeaponCatalog.All.Count, seen.Distinct().Count());
    }

    [Fact]
    public void ParseRejectsAFileWithoutWeapons()
    {
        Assert.Throws<InvalidDataException>(() => WeaponCatalog.Parse("{}"));
        Assert.Throws<InvalidDataException>(() => WeaponCatalog.Parse("{\"weapons\": []}"));
    }

    [Fact]
    public void InterpolationIsLinearBetweenTableRows()
    {
        var table = new BallisticTable([(100, 900), (200, 800)]);

        Assert.Equal(850, table.Interpolate(150)!.Mil);
        Assert.Null(table.Interpolate(99));
        Assert.Null(table.Interpolate(201));
    }

    [Fact]
    public void DuplicateDistanceGivesARangeInsteadOfAGuess()
    {
        var table = new BallisticTable([(100, 610), (100, 620)]);
        var solution = table.Interpolate(100)!;

        Assert.Null(solution.Mil);
        Assert.Equal("610–620", solution.Format());
    }

    // ------------------------------------------------------------ HUD

    private static MortarSession SolvedSession(string weaponId)
    {
        var session = new MortarSession(new MortarCalculator(100.0));
        session.SelectWeapon(WeaponCatalog.Find(weaponId));
        session.LockGun(new MapCoordinate(98.09, 109.78));
        session.LockTarget(new MapCoordinate(99.58, 110.07));
        return session;
    }

    private static HudSettings Hud(HudLayout layout)
    {
        var theme = HudThemeLibrary.CreateDefaultGreen();
        theme.Layout = layout;
        return new HudSettings { CurrentTheme = theme };
    }

    /// <summary>示例目标约 152m，L81 射表给 829 MIL（与网页一致）。</summary>
    [Fact]
    public void Compact_L81_ShowsWeaponAzRngAndMil()
    {
        var lines = HudLayoutFormatter.BuildLines(SolvedSession("mortar"), Hud(HudLayout.Compact))
            .Select(l => l.ToText()).ToList();

        Assert.Equal(["L81", "AZ  079.0°", "RNG 152m", "MIL 829"], lines);
    }

    [Fact]
    public void Compact_Sph2_OutOfRangeIsShownNotHidden()
    {
        var original = Loc.Language;
        try
        {
            Loc.Language = Loc.Thai;
            var lines = HudLayoutFormatter.BuildLines(SolvedSession("spg"), Hud(HudLayout.Compact))
                .Select(l => l.ToText()).ToList();

            // 152m 远小于 SPH-2 最小射程 780m：两条弹道都显示「นอกระยะ」。
            Assert.Equal(["SPH-2", "AZ  079.0°", "RNG 152m", "LO  นอกระยะ", "HI  นอกระยะ"], lines);
        }
        finally
        {
            Loc.Language = original;
        }
    }

    [Fact]
    public void NoTarget_ShowsPlaceholdersNotZero()
    {
        var session = new MortarSession(new MortarCalculator(100.0));
        session.SelectWeapon(WeaponCatalog.Find("mortar"));
        session.LockGun(new MapCoordinate(98.09, 109.78));

        var lines = HudLayoutFormatter.BuildLines(session, Hud(HudLayout.Compact)).Select(l => l.ToText()).ToList();

        Assert.Equal("MIL --", lines[^1]);
    }

    [Fact]
    public void Minimal_ShowsOnlyValues()
    {
        var lines = HudLayoutFormatter.BuildLines(SolvedSession("mortar"), Hud(HudLayout.Minimal))
            .Select(l => l.ToText()).ToList();

        Assert.Equal(["079.0°", "152m", "829"], lines);
    }

    [Fact]
    public void Horizontal_PutsEverythingOnOneLine()
    {
        var lines = HudLayoutFormatter.BuildLines(SolvedSession("mortar"), Hud(HudLayout.Horizontal));

        Assert.Single(lines);
        Assert.Equal("L81   AZ 079.0°   RNG 152m   MIL 829", lines[0].ToText());
    }

    [Fact]
    public void ShowMilOff_FallsBackToOriginalLayout()
    {
        var hud = Hud(HudLayout.Compact);
        hud.ShowMil = false;

        var lines = HudLayoutFormatter.BuildLines(SolvedSession("mortar"), hud).Select(l => l.ToText()).ToList();

        Assert.Equal(["AZ  079.0°", "RNG 152m"], lines);
    }

    [Fact]
    public void SwitchingWeaponKeepsGunTargetAndSolution()
    {
        var session = SolvedSession("mortar");
        var before = session.Solution;

        session.SelectWeapon(WeaponCatalog.Find("spg"), announce: true);

        Assert.Equal(before, session.Solution);
        Assert.NotNull(session.Gun);
        Assert.NotNull(session.Target);
        Assert.Equal(MortarStatusKind.WeaponChanged, session.LastStatus);
        Assert.False(session.Elevation.InRange);
    }
}
