using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LXIUM.Models;

/// <summary>lxium.json (docs/ai/addon.md 3장)</summary>
public sealed record Manifest(int Schema, string Id, string Name, string Version, string? Description,
    GameRef Game, Dictionary<string, VariantInfo> Variants);

public sealed record GameRef(string Id, string Name, string Exe, int? SteamAppId);

public sealed record VariantInfo(string? MinLoaderVersion);

/// <summary>addons/ 폴더의 애드온 zip 하나</summary>
public sealed record Addon(string ZipPath, Manifest Manifest)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static Addon Load(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("lxium.json") ?? throw new InvalidDataException("lxium.json 없음");
        using var s = entry.Open();
        var m = JsonSerializer.Deserialize<Manifest>(s, Json) ?? throw new InvalidDataException("lxium.json 비어 있음");
        if (m.Schema != 1) throw new InvalidDataException($"지원하지 않는 schema {m.Schema}");
        if (!Regex.IsMatch(m.Id ?? "", "^[A-Za-z0-9_.-]+$") || !Regex.IsMatch(m.Game?.Id ?? "", "^[a-z0-9-]+$"))
            throw new InvalidDataException("id 형식 오류");
        if (m.Variants is not { Count: > 0 }) throw new InvalidDataException("variants 없음");
        return new Addon(zipPath, m);
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
}
