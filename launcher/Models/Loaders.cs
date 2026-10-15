using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LXIUM.Models;

public sealed record LoaderInfo(string Id, string Name, string? Version)
{
    public string Label => Version is null ? Name : $"{Name} {Version}";
}

/// <summary>로더 감지 (docs/ai/addon.md 4장). 새 로더는 규격 표에 먼저 추가한 뒤 여기에 추가합니다.</summary>
public static class Loaders
{
    static readonly (string Id, string Name, string[] Probes)[] Table =
    [
        ("bepinex5", "BepInEx 5", ["BepInEx/core/BepInEx.dll"]),
        ("bepinex6", "BepInEx 6", ["BepInEx/core/BepInEx.Core.dll"]),
        ("melonloader", "MelonLoader", ["MelonLoader/net35/MelonLoader.dll", "MelonLoader/net6/MelonLoader.dll"]),
    ];

    public static readonly Dictionary<string, string> Names =
        Table.ToDictionary(t => t.Id, t => t.Name).Append(new("none", "로더 없음")).ToDictionary();

    public static List<LoaderInfo> Detect(string root)
    {
        var found = new List<LoaderInfo>();
        foreach (var (id, name, probes) in Table)
        {
            var dll = probes.Select(p => Path.Combine(root, p)).FirstOrDefault(File.Exists);
            if (dll is null) continue;
            var ver = FileVersionInfo.GetVersionInfo(dll).ProductVersion?.Split('+')[0];
            found.Add(new LoaderInfo(id, name, string.IsNullOrWhiteSpace(ver) ? null : ver));
        }
        return found;
    }

    /// <summary>
    /// 로더 버전 비교. "6.0.0-be.785"처럼 숫자 부분을 먼저 비교하고, 같으면 정식 &gt; 프리릴리스, 둘 다 프리릴리스면 그 숫자로 비교.
    /// </summary>
    public static int Compare(string a, string b)
    {
        var (ca, pa) = Split(a);
        var (cb, pb) = Split(b);
        var c = CompareNums(ca, cb);
        if (c != 0) return c;
        if (pa == "" || pb == "") return (pa == "" ? 1 : 0) - (pb == "" ? 1 : 0);
        return CompareNums(pa, pb);
    }

    static (string Core, string Pre) Split(string v)
    {
        v = v.Split('+')[0];
        var i = v.IndexOf('-');
        return i < 0 ? (v, "") : (v[..i], v[(i + 1)..]);
    }

    static int CompareNums(string x, string y)
    {
        var a = Regex.Matches(x, @"\d+").Select(m => long.Parse(m.Value)).ToList();
        var b = Regex.Matches(y, @"\d+").Select(m => long.Parse(m.Value)).ToList();
        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var c = (i < a.Count ? a[i] : 0).CompareTo(i < b.Count ? b[i] : 0);
            if (c != 0) return c;
        }
        return 0;
    }
}
