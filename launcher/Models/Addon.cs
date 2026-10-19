using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LXIUM.Models;

/// <summary>lxium.json (docs/ai/addon.md 3장)</summary>
public sealed record Manifest(int Schema, string Id, string Name, string Version, string? Description,
    GameRef Game, Dictionary<string, VariantInfo> Variants)
{
    /// <summary>모드 패널 필터 태그. <see cref="Addon.KnownTags"/>에 없는 것은 무시</summary>
    public string[]? Tags { get; init; }
    /// <summary>먼저 설치돼 있어야 하는 애드온 id</summary>
    public string[]? Requires { get; init; }
    /// <summary>이 애드온이 대신하는 옛 id (이름을 바꾼 경우). 설치할 때 옛 것을 먼저 제거</summary>
    public string[]? Replaces { get; init; }
}

public sealed record GameRef(string Id, string Name, string Exe, int? SteamAppId);

public sealed record VariantInfo(string? MinLoaderVersion);

/// <summary>addons/ 폴더의 애드온 zip 하나</summary>
public sealed record Addon(string ZipPath, Manifest Manifest)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>쓸 수 있는 태그 id. 표시 이름은 lang의 "tag.&lt;id&gt;"</summary>
    public static readonly string[] KnownTags = ["performance", "utility", "fun", "story", "msmod"];

    /// <summary>zip 루트의 아이콘·설정 설명 파일 이름 (addon.md 2장)</summary>
    public const string IconFile = "addon.png";
    public static string LxConfigFile(string id) => $"{id}.lxconfig";

    public static Addon Load(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("lxium.json") ?? throw new InvalidDataException(Loc.T("err.manifestMissing"));
        using var s = entry.Open();
        return new Addon(zipPath, Validate(JsonSerializer.Deserialize<Manifest>(s, Json)));
    }

    /// <summary>lxium.json 필수 규칙 검사 (addon.md 3장). zip과 애드온 모음이 함께 씀</summary>
    public static Manifest Validate(Manifest? m)
    {
        if (m is null) throw new InvalidDataException(Loc.T("err.manifestEmpty"));
        if (m.Schema != 1) throw new InvalidDataException(Loc.T("err.schema", m.Schema));
        if (!Regex.IsMatch(m.Id ?? "", "^[A-Za-z0-9_.-]+$") || !Regex.IsMatch(m.Game?.Id ?? "", "^[a-z0-9-]+$"))
            throw new InvalidDataException(Loc.T("err.manifestId"));
        if (!Regex.IsMatch(m.Version ?? "", @"^\d+\.\d+\.\d+$")) throw new InvalidDataException(Loc.T("err.manifestVersion"));
        if (string.IsNullOrWhiteSpace(m.Name) || string.IsNullOrWhiteSpace(m.Game!.Exe)) throw new InvalidDataException(Loc.T("err.manifestName"));
        if (m.Variants is not { Count: > 0 }) throw new InvalidDataException(Loc.T("err.manifestVariants"));
        return m with { Tags = m.Tags?.Where(KnownTags.Contains).Distinct().ToArray() };
    }

    /// <summary>읽을 수 없는 zip은 건너뜁니다.</summary>
    public static List<Addon> LoadAll(string dir)
    {
        var list = new List<Addon>();
        if (!Directory.Exists(dir)) return list;
        foreach (var zip in Directory.EnumerateFiles(dir, "*.zip"))
            try { list.Add(Load(zip)); } catch (Exception) { }
        return list;
    }

    /// <summary>zip 루트의 addon.png를 cacheDir에 꺼내 경로를 돌려줍니다. 없으면 null</summary>
    public string? ExtractIcon(string cacheDir)
    {
        try
        {
            var dest = Path.Combine(cacheDir, $"{Manifest.Id}-{Manifest.Version}.png");
            if (File.Exists(dest)) return dest;
            using var zip = ZipFile.OpenRead(ZipPath);
            if (zip.GetEntry(IconFile) is not { } entry) return null;
            Directory.CreateDirectory(cacheDir);
            entry.ExtractToFile(dest, true);
            return dest;
        }
        catch (Exception) { return null; }
    }

    /// <summary>zip 루트의 &lt;id&gt;.lxconfig 내용. 없으면 null</summary>
    public string? ReadLxConfig()
    {
        try
        {
            using var zip = ZipFile.OpenRead(ZipPath);
            if (zip.GetEntry(LxConfigFile(Manifest.Id)) is not { } entry) return null;
            using var r = new StreamReader(entry.Open());
            return r.ReadToEnd();
        }
        catch (Exception) { return null; }
    }
}
