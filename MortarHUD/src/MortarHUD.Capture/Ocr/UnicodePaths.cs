using OpenCvSharp;

namespace MortarHUD.Capture.Ocr;

/// <summary>
/// 让 OCR 资源在含非 ASCII 字符的路径下也能用（ซู-ปืนครก）。
/// </summary>
/// <remarks>
/// <para>
/// 单文件 EXE 会把内容解压到 <c>%TEMP%\.net\&lt;EXE 名&gt;\…</c>。程序名是泰文「ซู-ปืนครก.exe」，
/// 解压目录里就带泰文；Windows 用户名是泰文时，%TEMP% 本身也带泰文。
/// </para>
/// <para>
/// 托管代码（File.*）对这种路径没问题，但两个原生库不行：
/// OpenCV 的 <c>imread / imwrite</c> 与 Tesseract 打开 traineddata 都走 ANSI 文件 API，
/// 遇到当前代码页表示不了的字符就打不开文件。
/// 所以图片一律由托管代码读写字节再交给 OpenCV 编解码；tessdata 则复制到纯 ASCII 目录。
/// </para>
/// </remarks>
public static class UnicodePaths
{
    /// <summary>纯 ASCII 的缓存根目录名，位于 <c>C:\ProgramData</c> 之下。</summary>
    private const string CacheFolderName = "SuMortar";

    public static bool IsAscii(string path) => path.All(c => c < 128);

    /// <summary>用托管 IO 读文件再解码，代替 <see cref="Cv2.ImRead"/>。</summary>
    public static Mat ReadImage(string path, ImreadModes mode = ImreadModes.Color)
    {
        var bytes = File.ReadAllBytes(path);
        return Cv2.ImDecode(bytes, mode);
    }

    /// <summary>编码后用托管 IO 写文件，代替 <see cref="Cv2.ImWrite(string, Mat, int[])"/>。</summary>
    public static void WriteImage(string path, Mat image)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".png";
        }

        if (!Cv2.ImEncode(extension, image, out var bytes))
        {
            throw new IOException($"Failed to encode image as {extension}: {path}");
        }

        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// 目录路径是纯 ASCII 就原样返回；否则把目录内容复制到 <c>C:\ProgramData\SuMortar\&lt;name&gt;</c>
    /// 并返回那个目录。复制失败或 ProgramData 本身也不是 ASCII 时退回原路径。
    /// </summary>
    public static string EnsureAsciiDirectory(string directory, string name)
    {
        if (IsAscii(directory) || !Directory.Exists(directory))
        {
            return directory;
        }

        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                return directory;
            }

            var target = Path.Combine(root, CacheFolderName, name);
            if (!IsAscii(target))
            {
                return directory;
            }

            Directory.CreateDirectory(target);

            foreach (var source in Directory.EnumerateFiles(directory))
            {
                var destination = Path.Combine(target, Path.GetFileName(source));
                var sourceInfo = new FileInfo(source);
                var destinationInfo = new FileInfo(destination);

                // 只在缺失或大小不同（换了版本的语言包）时复制。
                if (!destinationInfo.Exists || destinationInfo.Length != sourceInfo.Length)
                {
                    File.Copy(source, destination, overwrite: true);
                }
            }

            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"复制到 ASCII 目录失败：{ex.Message}");
            return directory;
        }
    }
}
