using System.Text.Json;
using MortarHUD.Capture.ImageProcessing;
using MortarHUD.Capture.Ocr;
using MortarHUD.Core.Configuration;
using MortarHUD.Core.Parsing;
using MortarHUD.Core.Validation;
using OpenCvSharp;
using Xunit;

namespace MortarHUD.Ocr.Tests;

/// <summary>
/// ซู-ปืนครก V0.4：模板裁决引擎。样本是 2560x1600 / 150% 实机采集的原始 ROI，
/// 光标在 ROI 内 (-7, 110)。前三张 Tesseract 两种分页读出不同的值（3↔5），目视真值见 manifest。
/// </summary>
[Collection("OCR")]
public class TieBreakTests
{
    private static readonly string Directory_ = Path.Combine(
        FixtureRepository.RepoRoot, "tests", "Fixtures", "sumortar-2560x1600");

    private static readonly OpenCvSharp.Point CursorInRoi = new(-7, 110);

    [Fact]
    public void TieBreakAtlasShipsWithTheAppAndCoversEveryCharacter()
    {
        using var engine = TemplateOcrEngine.CreateTieBreak();

        Assert.NotNull(engine);
        Assert.Equal(TemplateOcrEngine.TieBreakName, engine!.Name);
        foreach (var c in "xy.0123456789")
        {
            Assert.Contains(c, engine.Coverage);
        }
    }

    [Fact]
    public async Task RealCapturesAreReadCorrectlyWithTheTieBreak()
    {
        var engines = OcrEngineFactory.WithTieBreak(
            OcrEngineFactory.Resolve("Auto", new OcrSettings(), FixtureRepository.TessdataDirectory));
        try
        {
            Assert.Contains(engines, e => e.Name == TemplateOcrEngine.TieBreakName);
            var recognizer = new CoordinateRecognizer(engines, PreprocessorFactory.AutoCandidates,
                new CoordinateTextParser(), new CoordinateValidator());

            var failures = new List<string>();
            foreach (var (file, x, y, _) in LoadManifest())
            {
                using var roi = Cv2.ImDecode(File.ReadAllBytes(Path.Combine(Directory_, file)), ImreadModes.Color);
                var result = await recognizer.RecognizeAsync(roi, CancellationToken.None, new(CursorInRoi.X, CursorInRoi.Y));

                if (!result.Success || result.Coordinate is not { } c
                    || Math.Abs(c.X - x) > .005 || Math.Abs(c.Y - y) > .005)
                {
                    failures.Add($"{file}: {result.Error} / {result.Coordinate} / "
                                 + CoordinateRecognizer.DescribeAttempts(result.Attempts));
                }
            }

            Assert.Empty(failures);
        }
        finally { foreach (var engine in engines) engine.Dispose(); }
    }

    /// <summary>不带裁决时，这些样本绝不能被采纳成错误的值（只能失败或读对）。</summary>
    [Fact]
    public async Task WithoutTheTieBreakAWrongValueIsNeverAccepted()
    {
        var engines = OcrEngineFactory.Resolve("Auto", new OcrSettings(), FixtureRepository.TessdataDirectory);
        try
        {
            var recognizer = new CoordinateRecognizer(engines, PreprocessorFactory.AutoCandidates,
                new CoordinateTextParser(), new CoordinateValidator());

            foreach (var (file, x, y, _) in LoadManifest())
            {
                using var roi = Cv2.ImDecode(File.ReadAllBytes(Path.Combine(Directory_, file)), ImreadModes.Color);
                var result = await recognizer.RecognizeAsync(roi, CancellationToken.None, new(CursorInRoi.X, CursorInRoi.Y));

                if (result.Success && result.Coordinate is { } c)
                {
                    Assert.True(Math.Abs(c.X - x) <= .005 && Math.Abs(c.Y - y) <= .005,
                        $"{file}: accepted wrong value {c}");
                }
            }
        }
        finally { foreach (var engine in engines) engine.Dispose(); }
    }

    private static IEnumerable<(string File, double X, double Y, bool Disagrees)> LoadManifest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory_, "manifest.json")));
        return manifest.RootElement.EnumerateArray()
            .Select(r => (r.GetProperty("file").GetString()!, r.GetProperty("expectedX").GetDouble(),
                r.GetProperty("expectedY").GetDouble(), r.GetProperty("tesseractDisagrees").GetBoolean()))
            .ToList();
    }
}
