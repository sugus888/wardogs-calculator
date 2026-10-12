using MortarHUD.Core.Configuration;
using MortarHUD.Core.Crosshair;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>เป้ากลางจอ（ซู-ปืนครก V0.3）的几何与设置。</summary>
public sealed class CrosshairTests
{
    [Fact]
    public void CrossLeavesTheConfiguredGapAtTheCenter()
    {
        var shape = CrosshairGeometry.Build(new CrosshairSettings { Style = CrosshairStyle.Cross, Gap = 4, Size = 10, Thickness = 2 });

        Assert.Equal(4, shape.Lines.Count);
        Assert.Empty(shape.Circles);
        Assert.Contains(new CrosshairLine(-14, 0, -4, 0), shape.Lines);
        Assert.Contains(new CrosshairLine(4, 0, 14, 0), shape.Lines);
        Assert.Contains(new CrosshairLine(0, -14, 0, -4), shape.Lines);
        Assert.Contains(new CrosshairLine(0, 4, 0, 14), shape.Lines);
        Assert.Equal(2, shape.Thickness);
    }

    [Theory]
    [InlineData(CrosshairStyle.Dot, 0, 1)]
    [InlineData(CrosshairStyle.CrossDot, 4, 1)]
    [InlineData(CrosshairStyle.Circle, 0, 2)]
    public void StylesProduceTheExpectedParts(CrosshairStyle style, int lines, int circles)
    {
        var shape = CrosshairGeometry.Build(new CrosshairSettings { Style = style });

        Assert.Equal(lines, shape.Lines.Count);
        Assert.Equal(circles, shape.Circles.Count);
    }

    [Fact]
    public void OffsetMovesEveryPart()
    {
        var shape = CrosshairGeometry.Build(new CrosshairSettings { Style = CrosshairStyle.CrossDot, OffsetX = 5, OffsetY = -3, Gap = 0, Size = 10 });

        Assert.Contains(new CrosshairLine(5, -3, 15, -3), shape.Lines);
        Assert.All(shape.Circles, c => Assert.Equal((5.0, -3.0), (c.CenterX, c.CenterY)));
    }

    [Fact]
    public void ClampRepairsOutOfRangeAndGarbageValues()
    {
        var settings = new CrosshairSettings
        {
            Style = (CrosshairStyle)99,
            Size = 9999,
            Thickness = double.NaN,
            Gap = -5,
            Opacity = 0,
            OffsetX = 1e9,
            Color = "",
        };

        settings.Clamp();

        Assert.Equal(CrosshairStyle.Cross, settings.Style);
        Assert.Equal(CrosshairSettings.MaxSize, settings.Size);
        Assert.Equal(2, settings.Thickness);
        Assert.Equal(CrosshairSettings.MinGap, settings.Gap);
        Assert.Equal(CrosshairSettings.MinOpacity, settings.Opacity);
        Assert.Equal(CrosshairSettings.MaxOffset, settings.OffsetX);
        Assert.Equal("#00FF66", settings.Color);
    }

    [Fact]
    public void BuildDoesNotMutateTheSettings()
    {
        var settings = new CrosshairSettings { Size = 9999 };

        _ = CrosshairGeometry.Build(settings);

        Assert.Equal(9999, settings.Size);
    }

    [Fact]
    public void CrosshairIsOffByDefaultAndHotkeysHaveDefaults()
    {
        var settings = new MortarHudSettings();

        Assert.False(settings.Crosshair.Enabled);
        Assert.Equal("F11", settings.Hotkeys.ToggleCrosshair);
        Assert.Equal("Insert", settings.Hotkeys.QuickMenu);
        Assert.Equal(settings.Crosshair.Size, settings.Clone().Crosshair.Size);
        Assert.NotSame(settings.Crosshair, settings.Clone().Crosshair);
    }
}
