using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LXIUM.Models;

/// <summary>모음에서 얻은 애드온 zip 한 버전 (docs/ai/catalog.md)</summary>
public sealed record CatalogEntry(string Url, string Sha256, Manifest Manifest)
{
    /// <summary>이 항목을 준 모음의 name</summary>
    public string Source { get; init; } = "";
    /// <summary>아직 받지 않은 애드온의 아이콘 주소 (GitHub raw addon.png 등)</summary>
    public string? IconUrl { get; init; }
    /// <summary>외부 모드: 받은 zip에서 변형별로 가져올 경로 접두사. 있으면 받은 뒤 LXIUM 애드온 zip으로 다시 묶음</summary>
    public Dictionary<string, string[]>? Include { get; init; }
}

/// <summary>
/// catalog.json의 addons 한 항목. 세 가지 모양:
/// 고정 = url + sha256 + manifest (schema 1과 같음),
/// GitHub = github (LXIUM 규격 저장소: 릴리스 태그 &lt;id&gt;-v&lt;버전&gt;, asset &lt;id&gt;-&lt;버전&gt;.zip, 태그의 lxium.json),
/// 외부 = github + asset + manifest(버전 없이) + include (LXIUM 규격이 아닌 모드 릴리스를 다시 묶음)
/// </summary>
public sealed record CatalogItem(string? Url, string? Sha256, Manifest? Manifest)
{
    public string? Github { get; init; }
    public string? Asset { get; init; }
    /// <summary>외부: 태그에서 버전을 꺼낼 정규식 (그룹 1). 기본 (\d+\.\d+\.\d+)</summary>
    public string? Version { get; init; }
    public Dictionary<string, string[]>? Include { get; init; }
    public string? Icon { get; init; }
}

public sealed record CatalogDoc(int Schema, string Name, List<CatalogItem> Addons);

/// <summary>애드온 모음(catalog.json) 받기·해석, GitHub 릴리스 읽기, 항목 zip 받기·검사</summary>
public static class Catalogs
{
    /// <summary>읽을 수 있는 가장 높은 schema</summary>
    public const int Schema = 2;
    static readonly Regex NativeTag = new(@"^(?<id>[A-Za-z0-9_.-]+)-v(?<ver>\d+\.\d+\.\d+)$");

    public static bool IsHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
    static bool IsRepo(string? repo) => Regex.IsMatch(repo ?? "", @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");

    /// <summary>모든 모음을 받습니다. 받지 못한 모음·항목은 errors에 남기고 나머지로 계속합니다.</summary>
    public static async Task<(List<CatalogEntry> Entries, List<string> Errors)> FetchAsync(IEnumerable<string> urls, string cacheDir)
    {
        var entries = new List<CatalogEntry>();
        var errors = new List<string>();
        foreach (var url in urls)
        {
            try
            {
                if (!IsHttps(url)) throw new InvalidDataException(Loc.T("err.httpsOnly"));
                var doc = ParseDoc(await Net.Http.GetStringAsync(url), url);
                entries.AddRange(await ResolveAsync(doc, cacheDir, errors));
            }
            catch (HttpRequestException e)
            {
                errors.Add(e.StatusCode is { } code ? Loc.T("err.http", url, (int)code) : Loc.T("err.connect", url, e.Message));
            }
            catch (Exception e)
            {
                errors.Add($"{url}: {e.Message}");
            }
        }
        return (entries, errors);
    }

    public static CatalogDoc ParseDoc(string json, string url)
    {
        var doc = JsonSerializer.Deserialize<CatalogDoc>(json, Addon.Json) ?? throw new InvalidDataException(Loc.T("err.manifestEmpty"));
        if (doc.Schema is < 1 or > Schema) throw new InvalidDataException(Loc.T("err.schema", doc.Schema));
        return doc with { Name = string.IsNullOrWhiteSpace(doc.Name) ? new Uri(url).Host : doc.Name, Addons = doc.Addons ?? [] };
    }

    /// <summary>고정 항목만 해석 (네트워크 없음). GitHub 항목은 ResolveAsync가 처리</summary>
    public static List<CatalogEntry> Parse(string json, string url, List<string> errors)
    {
        var doc = ParseDoc(json, url);
        return doc.Addons.Where(a => a.Github is null).Select(a => Fixed(doc.Name, a, errors)).OfType<CatalogEntry>().ToList();
    }

    static CatalogEntry? Fixed(string source, CatalogItem a, List<string> errors)
    {
        try
        {
            var m = Addon.Validate(a.Manifest);
            if (!IsHttps(a.Url)) throw new InvalidDataException(Loc.T("err.httpsOnly"));
            if (!Regex.IsMatch(a.Sha256 ?? "", "^[0-9a-fA-F]{64}$")) throw new InvalidDataException(Loc.T("err.noSha256"));
            return new CatalogEntry(a.Url!, a.Sha256!, m) { Source = source, IconUrl = IsHttps(a.Icon) ? a.Icon : null, Include = a.Include };
        }
        catch (Exception e)
        {
            errors.Add(Loc.T("err.itemSkipped", source, a.Manifest?.Id, a.Manifest?.Version, e.Message));
            return null;
        }
    }

    public static async Task<List<CatalogEntry>> ResolveAsync(CatalogDoc doc, string cacheDir, List<string> errors)
    {
        var list = new List<CatalogEntry>();
        foreach (var a in doc.Addons)
        {
            if (a.Github is null)
            {
                if (Fixed(doc.Name, a, errors) is { } e) list.Add(e);
                continue;
            }
            try
            {
                if (!IsRepo(a.Github)) throw new InvalidDataException(a.Github);
                using var releases = JsonDocument.Parse(await ReleasesAsync(a.Github, cacheDir));
                foreach (var rel in releases.RootElement.EnumerateArray())
                {
                    if (rel.GetProperty("draft").GetBoolean() || rel.GetProperty("prerelease").GetBoolean()) continue;
                    var tag = rel.GetProperty("tag_name").GetString() ?? "";
                    try
                    {
                        if (await FromReleaseAsync(doc.Name, a, tag, rel, cacheDir) is { } e) list.Add(e);
                    }
                    catch (Exception e)
                    {
                        errors.Add(Loc.T("err.itemSkipped", doc.Name, a.Github, tag, e.Message));
                    }
                }
            }
            catch (Exception e)
            {
                errors.Add($"{doc.Name}: {a.Github}: {e.Message}");
            }
        }
        return list;
    }

    /// <summary>릴리스 하나 → 항목. 이 항목과 상관없는 릴리스(태그·asset이 맞지 않음)는 null</summary>
    static async Task<CatalogEntry?> FromReleaseAsync(string source, CatalogItem a, string tag, JsonElement rel, string cacheDir)
    {
        var assets = rel.GetProperty("assets").EnumerateArray()
            .Select(x => (Name: x.GetProperty("name").GetString() ?? "", Url: x.GetProperty("browser_download_url").GetString() ?? "",
                Digest: x.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : ""))
            .ToList();
        Manifest m;
        (string Name, string Url, string Digest) asset;
        if (a.Manifest is null)
        {
            if (NativeTag.Match(tag) is not { Success: true } t) return null;
            var (id, ver) = (t.Groups["id"].Value, t.Groups["ver"].Value);
            asset = assets.FirstOrDefault(x => x.Name == $"{id}-{ver}.zip");
            if (asset.Name == "") return null;
            m = Addon.Validate(JsonSerializer.Deserialize<Manifest>(await RawAsync(a.Github!, tag, "lxium.json", cacheDir), Addon.Json));
            if (m.Id != id || m.Version != ver) throw new InvalidDataException(Loc.T("err.zipMismatch", m.Id, m.Version));
        }
        else
        {
            var v = Regex.Match(tag, a.Version ?? @"(\d+\.\d+\.\d+)");
            if (!v.Success || a.Asset is null || a.Include is not { Count: > 0 }) return null;
            asset = assets.FirstOrDefault(x => Regex.IsMatch(x.Name, a.Asset));
            if (asset.Name == "") return null;
            m = Addon.Validate(a.Manifest with { Version = v.Groups[1].Value });
        }
        if (!asset.Digest.StartsWith("sha256:")) throw new InvalidDataException(Loc.T("err.noSha256"));
        if (!IsHttps(asset.Url)) throw new InvalidDataException(Loc.T("err.httpsOnly"));
        return new CatalogEntry(asset.Url, asset.Digest["sha256:".Length..], m)
        {
            Source = source,
            Include = a.Manifest is null ? null : a.Include,
            IconUrl = IsHttps(a.Icon) ? a.Icon : a.Manifest is null ? $"https://raw.githubusercontent.com/{a.Github}/{tag}/{Addon.IconFile}" : null,
        };
    }

    /// <summary>
    /// GitHub 릴리스 목록. ETag로 캐시해 바뀌지 않았으면 304(요청 한도에 세지 않음)로 캐시를 씁니다.
    /// 받지 못하면 캐시가 있을 때 캐시를 씁니다.
    /// </summary>
    static async Task<string> ReleasesAsync(string repo, string cacheDir)
    {
        var file = Path.Combine(cacheDir, "github", repo.Replace('/', '_') + ".json");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases?per_page=50");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (File.Exists(file) && File.Exists(file + ".etag")) req.Headers.TryAddWithoutValidation("If-None-Match", File.ReadAllText(file + ".etag"));
        try
        {
            using var res = await Net.Http.SendAsync(req);
            if (res.StatusCode == System.Net.HttpStatusCode.NotModified && File.Exists(file)) return File.ReadAllText(file);
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadAsStringAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, body);
            if (res.Headers.ETag is { } etag) File.WriteAllText(file + ".etag", etag.ToString());
            return body;
        }
        catch (Exception) when (File.Exists(file))
        {
            return File.ReadAllText(file);
        }
    }

    /// <summary>태그의 파일 (raw.githubusercontent). 태그는 바뀌지 않으므로 한 번 받으면 계속 캐시를 씀</summary>
    static async Task<string> RawAsync(string repo, string tag, string path, string cacheDir)
    {
        var file = Path.Combine(cacheDir, "github", repo.Replace('/', '_'), tag, path);
        if (File.Exists(file)) return File.ReadAllText(file);
        var body = await Net.Http.GetStringAsync($"https://raw.githubusercontent.com/{repo}/{tag}/{path}");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, body);
        return body;
    }

    /// <summary>항목 zip을 받아 검사한 뒤 addons/에 &lt;id&gt;-&lt;version&gt;.zip으로 둡니다.</summary>
    public static async Task<Addon> DownloadAsync(CatalogEntry entry, string addonsDir, IProgress<double>? progress = null)
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await Net.DownloadAsync(entry.Url, tmp, progress);
            await using var s = File.OpenRead(tmp);
            return await StoreAsync(s, entry, addonsDir);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>
    /// 받은 내용(source)의 sha256을 확인하고, 외부 모드면 LXIUM 애드온 zip으로 다시 묶은 뒤,
    /// lxium.json의 id·version이 항목과 같은지 확인해 addons/에 둡니다. 검사에 실패하면 아무것도 남기지 않습니다.
    /// </summary>
    public static async Task<Addon> StoreAsync(Stream source, CatalogEntry entry, string addonsDir)
    {
        var tmp = Path.GetTempFileName();
        var packed = tmp + ".lxium.zip";
        try
        {
            await using (var f = File.Create(tmp))
                await source.CopyToAsync(f);
            string hash;
            await using (var f = File.OpenRead(tmp))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(f));
            if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Loc.T("err.sha256"));

            var zipPath = tmp;
            if (entry.Include is not null)
            {
                Repackage(tmp, packed, entry.Manifest, entry.Include);
                zipPath = packed;
            }
            var m = Addon.Load(zipPath).Manifest;
            if (m.Id != entry.Manifest.Id || m.Version != entry.Manifest.Version)
                throw new InvalidDataException(Loc.T("err.zipMismatch", m.Id, m.Version));

            Directory.CreateDirectory(addonsDir);
            var dest = Path.Combine(addonsDir, $"{m.Id}-{m.Version}.zip");
            File.Move(zipPath, dest, true);
            return Addon.Load(dest);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            if (File.Exists(packed)) File.Delete(packed);
        }
    }

    /// <summary>외부 모드 zip에서 include 경로만 골라 lxium.json + 변형 폴더 구조로 다시 묶습니다.</summary>
    public static void Repackage(string sourceZip, string destZip, Manifest manifest, Dictionary<string, string[]> include)
    {
        using var src = ZipFile.OpenRead(sourceZip);
        using var dst = ZipFile.Open(destZip, ZipArchiveMode.Create);
        using (var w = new StreamWriter(dst.CreateEntry("lxium.json").Open()))
            w.Write(JsonSerializer.Serialize(manifest, Addon.Json));
        var copied = 0;
        foreach (var (variant, prefixes) in include)
            foreach (var e in src.Entries.Where(e => e.Name != ""))
            {
                var name = e.FullName.Replace('\\', '/');
                if (!prefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                using var from = e.Open();
                using var to = dst.CreateEntry($"{variant}/{name}").Open();
                from.CopyTo(to);
                copied++;
            }
        if (copied == 0) throw new InvalidDataException(Loc.T("err.repackEmpty"));
    }
}
