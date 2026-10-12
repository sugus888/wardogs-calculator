namespace MortarHUD.Capture.Ocr;

/// <summary>
/// 找字形模板库目录（与 <see cref="TessdataLocator"/> 同一套查找策略）。
/// </summary>
public static class GlyphAtlasLocator
{
    public const string FolderName = "glyphs";

    /// <summary>ซู-ปืนครก：从 2560x1600 实机采集学到的字形，只用于 Tesseract 分歧时的裁决。</summary>
    public const string TieBreakFolderName = "glyphs-sumortar";

    public static string Resolve(string? configuredPath = null) => Resolve(configuredPath, FolderName);

    public static string Resolve(string? configuredPath, string folderName)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && IsUsable(configuredPath))
        {
            return configuredPath;
        }

        var baseDirectory = AppContext.BaseDirectory;

        foreach (var candidate in EnumerateCandidates(baseDirectory, folderName))
        {
            if (IsUsable(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(baseDirectory, "Models", folderName);
    }

    private static IEnumerable<string> EnumerateCandidates(string baseDirectory, string folderName)
    {
        yield return Path.Combine(baseDirectory, "Models", folderName);
        yield return Path.Combine(baseDirectory, folderName);

        var directory = new DirectoryInfo(baseDirectory);

        // 开发时从 bin 目录往上找项目目录。
        for (var depth = 0; depth < 6 && directory is not null; depth++)
        {
            yield return Path.Combine(directory.FullName, "Models", folderName);
            directory = directory.Parent;
        }
    }

    public static bool IsUsable(string? directory)
        => !string.IsNullOrWhiteSpace(directory)
           && Directory.Exists(directory)
           && File.Exists(Path.Combine(directory, GlyphAtlas.SpriteFileName))
           && File.Exists(Path.Combine(directory, GlyphAtlas.LabelsFileName));
}
