using System.IO.Compression;
using System.Text.Json;

namespace LXIUM.Models;

public sealed record LauncherRelease(string Version, string ZipUrl);

/// <summary>
/// 런처 자체 업데이트. GitHub 릴리스 중 태그 LXIUM-v&lt;버전&gt;의 *-portable.zip을 받아
/// 실행 중인 exe를 .old로 이름만 바꾸고(Windows는 실행 중 파일의 이름 바꾸기를 허용) 새 exe를 넣습니다.
/// settings.json은 건드리지 않습니다.
/// </summary>
public static class Updater
{
    public const string Repo = "LocalXtend/LXIUM";

    public static string Current => typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>현재보다 새 정식 릴리스가 있으면 그 정보, 없으면 null</summary>
    public static async Task<LauncherRelease?> CheckAsync()
    {
        using var res = await Net.Http.GetAsync($"https://api.github.com/repos/{Repo}/releases?per_page=30");
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException(Loc.T("err.http", Repo, (int)res.StatusCode));
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return Pick(doc.RootElement, Current);
    }

    /// <summary>릴리스 목록에서 current보다 새 최신 정식 LXIUM 릴리스를 고릅니다.</summary>
    public static LauncherRelease? Pick(JsonElement releases, string current)
    {
        LauncherRelease? best = null;
        foreach (var rel in releases.EnumerateArray())
        {
            var tag = rel.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith("LXIUM-v") || rel.GetProperty("prerelease").GetBoolean() || rel.GetProperty("draft").GetBoolean()) continue;
            var version = tag["LXIUM-v".Length..];
            var zip = rel.GetProperty("assets").EnumerateArray()
                .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
                .FirstOrDefault(a => a.Name.EndsWith("-portable.zip"));
            if (zip.Url == "" || Versions.Compare(version, current) <= 0) continue;
            if (best is null || Versions.Compare(version, best.Version) > 0) best = new LauncherRelease(version, zip.Url);
        }
        return best;
    }

    /// <summary>새 버전을 appDir에 풉니다. 끝나면 exePath를 다시 실행하고 지금 프로세스를 끝내야 합니다.</summary>
    public static async Task ApplyAsync(LauncherRelease release, string appDir, string exePath, IProgress<double>? progress = null)
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await Net.DownloadAsync(release.ZipUrl, tmp, progress);
            await Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(tmp);
                var exeName = Path.GetFileName(exePath);
                if (zip.GetEntry(exeName) is null) throw new InvalidDataException(Loc.T("err.updateNoExe", exeName));
                foreach (var entry in zip.Entries.Where(e => e.Name != ""))
                {
                    var rel = entry.FullName.Replace('\\', '/');
                    if (rel.Equals(AppSettings.FileName, StringComparison.OrdinalIgnoreCase)) continue;
                    var dest = rel.Equals(exeName, StringComparison.OrdinalIgnoreCase) ? exePath + ".new" : Installer.SafePath(appDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, true);
                }
                File.Move(exePath, exePath + ".old", true);
                File.Move(exePath + ".new", exePath);
            });
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>지난 업데이트가 남긴 .old 파일 정리 (시작할 때)</summary>
    public static void CleanupOld(string exePath)
    {
        try { File.Delete(exePath + ".old"); } catch (Exception) { }
    }
}
