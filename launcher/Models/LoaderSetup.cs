using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LXIUM.Models;

/// <summary>
/// 모드 로더 설치·제거. 공식 배포처에서만 받습니다.
/// BepInEx 5: GitHub BepInEx/BepInEx 릴리스, BepInEx 6: builds.bepinex.dev bleeding edge, MelonLoader: GitHub LavaGang/MelonLoader 릴리스.
/// 세 배포 zip 모두 루트가 게임 루트에 그대로 풀리는 구조입니다.
/// </summary>
public static class LoaderSetup
{
    /// <summary>로더가 게임 루트에 두는 파일·폴더. 제거할 때 지웁니다 (폴더는 안의 모드·설정까지).</summary>
    public static readonly Dictionary<string, string[]> Footprint = new()
    {
        ["bepinex5"] = ["BepInEx/", "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt"],
        ["bepinex6"] = ["BepInEx/", "dotnet/", "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt"],
        ["melonloader"] = ["MelonLoader/", "Mods/", "Plugins/", "UserData/", "UserLibs/", "version.dll", "dobby.dll", "NOTICE.txt"],
    };

    public static bool Supports(string loader) => Footprint.ContainsKey(loader);

    public static bool IsIl2Cpp(string root) => File.Exists(Path.Combine(root, "GameAssembly.dll"));

    /// <summary>PE 헤더의 Machine 값으로 64비트(x64) 실행 파일인지 확인합니다.</summary>
    public static bool Is64Bit(string exe)
    {
        using var f = File.OpenRead(exe);
        using var r = new BinaryReader(f);
        f.Position = 0x3C;
        f.Position = r.ReadInt32() + 4;
        return r.ReadUInt16() == 0x8664;
    }

    /// <summary>게임(비트·백엔드)에 맞는 최신 배포 zip 주소와 버전</summary>
    public static async Task<(string Url, string Version)> FindAsync(string loader, bool x64, bool il2cpp)
    {
        var arch = x64 ? "x64" : "x86";
        switch (loader)
        {
            case "bepinex5":
                if (il2cpp) throw new InvalidOperationException(Loc.T("err.bep5Il2cpp"));
                return await GitHubAssetAsync("BepInEx/BepInEx", "v5.", $@"^BepInEx_win_{arch}_[\d.]+\.zip$");
            case "bepinex6":
            {
                var html = await Net.Http.GetStringAsync("https://builds.bepinex.dev/projects/bepinex_be");
                var backend = il2cpp ? "IL2CPP" : "Mono";
                // 빌드 목록은 최신순. 첫 링크가 최신 빌드
                var m = Regex.Match(html, $@"href=""(/projects/bepinex_be/\d+/BepInEx-Unity\.{backend}-win-{arch}-(6\.0\.0-be\.\d+)[^""]*\.zip)""");
                if (!m.Success) throw new InvalidOperationException(Loc.T("err.loaderNotFound", "builds.bepinex.dev"));
                return ("https://builds.bepinex.dev" + m.Groups[1].Value, m.Groups[2].Value);
            }
            case "melonloader":
                return await GitHubAssetAsync("LavaGang/MelonLoader", "v", $@"^MelonLoader\.{arch}\.zip$");
            default:
                throw new InvalidOperationException(Loc.T("err.loaderUnsupported", loader));
        }
    }

    /// <summary>정식 릴리스 중 태그가 tagPrefix로 시작하는 최신 것의 asset</summary>
    static async Task<(string, string)> GitHubAssetAsync(string repo, string tagPrefix, string assetPattern)
    {
        using var doc = JsonDocument.Parse(await Net.Http.GetStringAsync($"https://api.github.com/repos/{repo}/releases?per_page=30"));
        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            var tag = rel.GetProperty("tag_name").GetString() ?? "";
            if (rel.GetProperty("prerelease").GetBoolean() || rel.GetProperty("draft").GetBoolean() || !tag.StartsWith(tagPrefix)) continue;
            foreach (var asset in rel.GetProperty("assets").EnumerateArray())
                if (Regex.IsMatch(asset.GetProperty("name").GetString() ?? "", assetPattern))
                    return (asset.GetProperty("browser_download_url").GetString()!, tag.TrimStart('v'));
        }
        throw new InvalidOperationException(Loc.T("err.loaderNotFound", repo));
    }

    /// <summary>이 게임에 맞는 최신 로더 버전 (업데이트 확인용)</summary>
    public static async Task<string> LatestAsync(string loader, string root, string exe) =>
        (await FindAsync(loader, Is64Bit(Path.Combine(root, exe)), IsIl2Cpp(root))).Version;

    /// <summary>
    /// 최신 로더를 받아 게임 루트에 풉니다(이미 있으면 덮어써서 업데이트). 설정·모드 폴더는 배포 zip에 없어 그대로 남습니다.
    /// 설치한 버전을 돌려줍니다. progress = 받기 0~1
    /// </summary>
    public static async Task<string> InstallAsync(string loader, string root, string exe, IProgress<double>? progress = null)
    {
        var (url, version) = await FindAsync(loader, Is64Bit(Path.Combine(root, exe)), IsIl2Cpp(root));
        var tmp = Path.GetTempFileName();
        try
        {
            await Net.DownloadAsync(url, tmp, progress);
            await Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(tmp);
                foreach (var entry in zip.Entries.Where(e => e.Name != ""))
                {
                    var dest = Installer.SafePath(root, entry.FullName.Replace('\\', '/'));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, true);
                }
            });
        }
        finally
        {
            File.Delete(tmp);
        }
        return version;
    }

    /// <summary>로더 파일·폴더를 지웁니다. changelog.txt는 BepInEx 것일 때만 지웁니다(게임 파일일 수 있음).</summary>
    public static void Uninstall(string loader, string root)
    {
        foreach (var item in Footprint[loader])
        {
            var path = Installer.SafePath(root, item.TrimEnd('/'));
            if (item.EndsWith('/')) { if (Directory.Exists(path)) Directory.Delete(path, true); }
            else if (File.Exists(path) && (item != "changelog.txt" || File.ReadAllText(path).Contains("BepInEx")))
                File.Delete(path);
        }
    }
}
