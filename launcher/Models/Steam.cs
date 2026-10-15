using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LXIUM.Models;

/// <summary>Steam 라이브러리 폴더 탐색과 게임 루트 찾기</summary>
public static class Steam
{
    /// <summary>Steam 설치 폴더 + libraryfolders.vdf의 모든 라이브러리 폴더</summary>
    public static List<string> FindLibraries()
    {
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        if (steam is null) return [];
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        var libs = File.Exists(vdf) ? ReadValues(File.ReadAllText(vdf), "path") : [];
        return libs.Prepend(steam).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).ToList();
    }

    /// <summary>VDF/ACF 텍스트에서 "key" "value" 쌍의 값을 모두 읽습니다.</summary>
    public static List<string> ReadValues(string vdf, string key) =>
        Regex.Matches(vdf, $"\"{Regex.Escape(key)}\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value.Replace(@"\\", @"\")).ToList();

    /// <summary>steamAppId의 appmanifest로 먼저 찾고, 없으면 모든 라이브러리의 common/*에서 exe를 찾습니다.</summary>
    public static string? FindGame(IEnumerable<string> libraries, GameRef game)
    {
        var libs = libraries.ToList();
        if (game.SteamAppId is int id)
            foreach (var lib in libs)
            {
                var acf = Path.Combine(lib, "steamapps", $"appmanifest_{id}.acf");
                if (!File.Exists(acf)) continue;
                var dir = ReadValues(File.ReadAllText(acf), "installdir").FirstOrDefault();
                var root = dir is null ? null : Path.Combine(lib, "steamapps", "common", dir);
                if (root is not null && File.Exists(Path.Combine(root, game.Exe))) return root;
            }
        foreach (var lib in libs)
        {
            var common = Path.Combine(lib, "steamapps", "common");
            if (!Directory.Exists(common)) continue;
            foreach (var root in Directory.EnumerateDirectories(common))
                if (File.Exists(Path.Combine(root, game.Exe))) return root;
        }
        return null;
    }
}
