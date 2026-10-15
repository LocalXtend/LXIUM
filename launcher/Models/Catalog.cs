using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LXIUM.Models;

/// <summary>애드온 모음의 한 항목 = 애드온 zip 한 버전 (docs/ai/catalog.md)</summary>
public sealed record CatalogEntry(string Url, string Sha256, Manifest Manifest)
{
    /// <summary>이 항목을 준 모음의 name</summary>
    public string Source { get; init; } = "";
}

public sealed record CatalogDoc(int Schema, string Name, List<CatalogEntry> Addons);

/// <summary>애드온 모음(catalog.json) 받기·검사, 항목 zip 받기</summary>
public static class Catalogs
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>모음 JSON을 해석합니다. 규칙에 맞지 않는 항목은 errors에 이유를 남기고 뺍니다.</summary>
    public static List<CatalogEntry> Parse(string json, string url, List<string> errors)
    {
        var doc = JsonSerializer.Deserialize<CatalogDoc>(json, Addon.Json) ?? throw new InvalidDataException("비어 있음");
        if (doc.Schema != 1) throw new InvalidDataException($"지원하지 않는 schema {doc.Schema} (런처를 업데이트하세요)");
        var name = string.IsNullOrWhiteSpace(doc.Name) ? new Uri(url).Host : doc.Name;
        var list = new List<CatalogEntry>();
        foreach (var e in doc.Addons ?? [])
        {
            try
            {
                Addon.Validate(e.Manifest);
                if (!IsHttps(e.Url)) throw new InvalidDataException("url은 https만");
                if (!Regex.IsMatch(e.Sha256 ?? "", "^[0-9a-fA-F]{64}$")) throw new InvalidDataException("sha256 없음");
                list.Add(e with { Source = name });
            }
            catch (Exception ex)
            {
                errors.Add($"{name}: {e.Manifest?.Id} {e.Manifest?.Version} 건너뜀 ({ex.Message})");
            }
        }
        return list;
    }

    public static bool IsHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;

    /// <summary>모든 모음을 받습니다. 받지 못한 모음은 errors에 남기고 나머지로 계속합니다.</summary>
    public static async Task<(List<CatalogEntry> Entries, List<string> Errors)> FetchAsync(IEnumerable<string> urls)
    {
        var entries = new List<CatalogEntry>();
        var errors = new List<string>();
        foreach (var url in urls)
        {
            try
            {
                if (!IsHttps(url)) throw new InvalidDataException("https 주소만 쓸 수 있습니다");
                entries.AddRange(Parse(await Http.GetStringAsync(url), url, errors));
            }
            catch (HttpRequestException e)
            {
                errors.Add(e.StatusCode is { } code ? $"{url}: 받지 못함 (응답 {(int)code})" : $"{url}: 연결하지 못함 ({e.Message})");
            }
            catch (Exception e)
            {
                errors.Add($"{url}: {e.Message}");
            }
        }
        return (entries, errors);
    }

    /// <summary>항목 zip을 받아 sha256과 lxium.json(id·version)을 확인한 뒤 addons/에 &lt;id&gt;-&lt;version&gt;.zip으로 둡니다.</summary>
    public static async Task<Addon> DownloadAsync(CatalogEntry entry, string addonsDir)
    {
        await using var s = await Http.GetStreamAsync(entry.Url);
        return await StoreAsync(s, entry, addonsDir);
    }

    /// <summary>받은 내용(source)을 검사해 addons/에 둡니다. 검사에 실패하면 아무것도 남기지 않습니다.</summary>
    public static async Task<Addon> StoreAsync(Stream source, CatalogEntry entry, string addonsDir)
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await using (var f = File.Create(tmp))
                await source.CopyToAsync(f);
            string hash;
            await using (var f = File.OpenRead(tmp))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(f));
            if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("받은 파일의 sha256이 모음과 다릅니다. 파일이 바뀌었거나 손상됐습니다.");
            var m = Addon.Load(tmp).Manifest;
            if (m.Id != entry.Manifest.Id || m.Version != entry.Manifest.Version)
                throw new InvalidDataException($"zip의 lxium.json({m.Id} {m.Version})이 모음 항목과 다릅니다.");

            Directory.CreateDirectory(addonsDir);
            var dest = Path.Combine(addonsDir, $"{m.Id}-{m.Version}.zip");
            File.Move(tmp, dest, true);
            return Addon.Load(dest);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
