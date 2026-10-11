using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace MortarHUD.Core.Ballistics;

/// <summary>
/// 一次查表得到的仰角（MIL）。
/// </summary>
/// <remarks>
/// 表里同一距离可能对应多个 MIL（例如 SPH-2 高抛表在最大射程附近），
/// 这时 <see cref="Mil"/> 无法确定，只能给出区间 <see cref="MinMil"/>–<see cref="MaxMil"/>。
/// 规则与 wardogs-artillery.com 的 <c>js/features/weapons.js</c> 完全一致。
/// </remarks>
public sealed record MilSolution(double? Mil, double MinMil, double MaxMil)
{
    /// <summary>与网页 <c>formatMilSolution</c> 相同：区间显示为 "min–max"，否则取整。</summary>
    public string Format()
    {
        var min = (int)Math.Round(MinMil, MidpointRounding.AwayFromZero);
        var max = (int)Math.Round(MaxMil, MidpointRounding.AwayFromZero);

        if (min != max)
        {
            return $"{min.ToString(CultureInfo.InvariantCulture)}–{max.ToString(CultureInfo.InvariantCulture)}";
        }

        var value = (int)Math.Round(Mil ?? MinMil, MidpointRounding.AwayFromZero);
        return value.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>距离（米）→ 仰角（MIL）表，按网页同样的方式线性插值。</summary>
public sealed class BallisticTable
{
    private const double Epsilon = 1e-6;

    private readonly IReadOnlyList<(double Distance, double[] Mils)> _groups;

    public BallisticTable(IEnumerable<(double Distance, double Mil)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _groups = entries
            .Where(e => double.IsFinite(e.Distance) && double.IsFinite(e.Mil))
            .OrderBy(e => e.Distance)
            .ThenBy(e => e.Mil)
            .GroupBy(e => e.Distance)
            .Select(g => (g.Key, g.Select(e => e.Mil).ToArray()))
            .ToList();
    }

    public bool IsEmpty => _groups.Count == 0;

    /// <summary>查表。距离不在表的覆盖范围内时返回 null。</summary>
    public MilSolution? Interpolate(double distanceMeters)
    {
        if (_groups.Count == 0 || !double.IsFinite(distanceMeters))
        {
            return null;
        }

        foreach (var (distance, mils) in _groups)
        {
            if (Math.Abs(distance - distanceMeters) <= Epsilon)
            {
                return new MilSolution(mils.Length == 1 ? mils[0] : null, mils.Min(), mils.Max());
            }
        }

        for (var i = 0; i < _groups.Count - 1; i++)
        {
            var left = _groups[i];
            var right = _groups[i + 1];

            if (distanceMeters <= left.Distance || distanceMeters >= right.Distance)
            {
                continue;
            }

            // 同一距离有多个 MIL 时，挑与相邻点最接近的那个，避免跨分支插值。
            var leftMil = Closest(left.Mils, right.Mils.Average());
            var rightMil = Closest(right.Mils, leftMil);

            var factor = (distanceMeters - left.Distance) / (right.Distance - left.Distance);
            var mil = leftMil + factor * (rightMil - leftMil);

            return new MilSolution(mil, mil, mil);
        }

        return null;
    }

    private static double Closest(double[] values, double target)
    {
        var best = values[0];

        foreach (var value in values)
        {
            if (Math.Abs(value - target) < Math.Abs(best - target))
            {
                best = value;
            }
        }

        return best;
    }
}

/// <summary>某距离上一件武器的全部射表解。</summary>
/// <param name="InRange">距离是否在武器的最小 / 最大射程之内。</param>
/// <param name="Single">单一射表（L81）。</param>
/// <param name="Low">低伸弹道（SPH-2）。</param>
/// <param name="High">高抛弹道（SPH-2）。</param>
public sealed record ElevationSolution(bool InRange, MilSolution? Single, MilSolution? Low, MilSolution? High)
{
    public static ElevationSolution None { get; } = new(false, null, null, null);
}

/// <summary>一件曲射武器的射程与射表。</summary>
public sealed class WeaponProfile
{
    public WeaponProfile(
        string id,
        string shortName,
        double minRangeMeters,
        double maxRangeMeters,
        BallisticTable? single,
        BallisticTable? low,
        BallisticTable? high)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Weapon id is required.", nameof(id));
        }

        if (!double.IsFinite(minRangeMeters) || !double.IsFinite(maxRangeMeters)
            || minRangeMeters < 0 || maxRangeMeters <= 0 || minRangeMeters > maxRangeMeters)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRangeMeters), "Invalid weapon range.");
        }

        Id = id;
        ShortName = shortName;
        MinRangeMeters = minRangeMeters;
        MaxRangeMeters = maxRangeMeters;
        Single = single is { IsEmpty: false } ? single : null;
        Low = low is { IsEmpty: false } ? low : null;
        High = high is { IsEmpty: false } ? high : null;
    }

    public string Id { get; }

    /// <summary>HUD 上显示的短名，例如 "L81"、"SPH-2"。</summary>
    public string ShortName { get; }

    public double MinRangeMeters { get; }

    public double MaxRangeMeters { get; }

    public BallisticTable? Single { get; }

    public BallisticTable? Low { get; }

    public BallisticTable? High { get; }

    /// <summary>与网页 <c>getWeaponElevationSolutions</c> 相同：射程外不给 MIL。</summary>
    public ElevationSolution Solve(double rangeMeters)
    {
        if (!double.IsFinite(rangeMeters))
        {
            return ElevationSolution.None;
        }

        var inRange = rangeMeters + 1e-6 >= MinRangeMeters && rangeMeters <= MaxRangeMeters + 1e-6;

        if (!inRange)
        {
            return new ElevationSolution(false, null, null, null);
        }

        return new ElevationSolution(
            true,
            Single?.Interpolate(rangeMeters),
            Low?.Interpolate(rangeMeters),
            High?.Interpolate(rangeMeters));
    }
}

/// <summary>
/// 武器射表目录。数据来自 wardogs-calculator 仓库的 <c>data/weapons.json</c>，
/// 编译时嵌入程序集——网页和 HUD 永远用同一份射表。
/// </summary>
public static class WeaponCatalog
{
    public const string DefaultWeaponId = "mortar";

    private const string ResourceName = "MortarHUD.Core.Ballistics.weapons.json";

    private static readonly Lazy<IReadOnlyList<WeaponProfile>> Embedded = new(LoadEmbedded);

    /// <summary>网页里的 id → HUD 短名。未知武器退回英文名。</summary>
    private static readonly Dictionary<string, string> ShortNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mortar"] = "L81",
        ["spg"] = "SPH-2",
    };

    public static IReadOnlyList<WeaponProfile> All => Embedded.Value;

    /// <summary>按 id 查武器；找不到时退回默认武器（L81）。</summary>
    public static WeaponProfile Find(string? id)
        => All.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase))
           ?? All.FirstOrDefault(w => w.Id == DefaultWeaponId)
           ?? All[0];

    /// <summary>循环切换到下一件武器。</summary>
    public static WeaponProfile Next(string? currentId)
    {
        var list = All;
        var index = -1;

        for (var i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i].Id, currentId, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        return list[(index + 1) % list.Count];
    }

    /// <summary>解析 weapons.json 的内容（测试也用它）。</summary>
    public static IReadOnlyList<WeaponProfile> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("weapons", out var weapons)
            || weapons.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("weapons.json has no \"weapons\" array.");
        }

        var result = new List<WeaponProfile>();

        foreach (var item in weapons.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString()?.Trim() : null;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var maxKm = ReadNumber(item, "maxRangeKm") ?? ReadNumber(item, "rangeKm");
            var minKm = ReadNumber(item, "minRangeKm") ?? 0;

            if (maxKm is not { } max || max <= 0 || minKm < 0 || minKm > max)
            {
                continue;
            }

            BallisticTable? single = null, low = null, high = null;

            if (item.TryGetProperty("ballistics", out var ballistics) && ballistics.ValueKind == JsonValueKind.Object)
            {
                single = ReadTable(ballistics, "single");
                low = ReadTable(ballistics, "low");
                high = ReadTable(ballistics, "high");
            }

            result.Add(new WeaponProfile(
                id,
                ResolveShortName(id, item),
                minKm * 1000.0,
                max * 1000.0,
                single,
                low,
                high));
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("weapons.json contains no usable weapon.");
        }

        return result;
    }

    private static IReadOnlyList<WeaponProfile> LoadEmbedded()
    {
        using var stream = typeof(WeaponCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static string ResolveShortName(string id, JsonElement item)
    {
        if (ShortNames.TryGetValue(id, out var shortName))
        {
            return shortName;
        }

        if (item.TryGetProperty("names", out var names)
            && names.TryGetProperty("en", out var en)
            && en.GetString() is { Length: > 0 } english)
        {
            return english;
        }

        return id;
    }

    private static double? ReadNumber(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static BallisticTable? ReadTable(JsonElement ballistics, string name)
    {
        if (!ballistics.TryGetProperty(name, out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var entries = new List<(double, double)>();

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 2)
            {
                continue;
            }

            var distance = row[0];
            var mil = row[1];

            if (distance.ValueKind == JsonValueKind.Number && mil.ValueKind == JsonValueKind.Number)
            {
                entries.Add((distance.GetDouble(), mil.GetDouble()));
            }
        }

        return entries.Count == 0 ? null : new BallisticTable(entries);
    }
}
