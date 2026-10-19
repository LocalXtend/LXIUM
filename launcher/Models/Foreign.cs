using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LXIUM.Models;

/// <summary>
/// LXIUM 규격이 아닌 모드 파일을 addons/에서 알아보고 LXIUM 애드온으로 바꿈 (docs/ai/addon.md 10장).
/// 사용자가 직접 받아 넣은 파일만 다룸 (모음에 등록하지 않음). 원본 파일은 건드리지 않고 cache/converted에 바꾼 zip을 둠.
/// </summary>
public static class Foreign
{
    /// <summary>마법소녀의 마녀재판</summary>
    static readonly GameRef Manosaba = new("manosaba", "魔法少女ノ魔女裁判", "manosaba.exe", 3101040);

    public const string ManosabaLoaderId = "ManosabaLoader";
    const string LoaderDll = "BepInEx/plugins/ManosabaLoader/ManosabaLoader.dll";

    /// <summary>알아보는 파일이면 LXIUM 애드온, 아니면 null</summary>
    public static Addon? Recognize(string file, string cacheDir)
    {
        try
        {
            return Path.GetExtension(file).ToLowerInvariant() switch
            {
                ".zip" => ManosabaLoader(file, cacheDir),
                ".msmod" => Msmod(file, cacheDir),
                _ => null,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// ManosabaLoader(KOKOTO-DEV/ManosabaMod_KR 등) 배포 zip. BepInEx 전체가 들어 있어도 플러그인 폴더만 bepinex6 변형으로 다시 묶음
    /// (로더는 런처가 관리, 설정 파일은 넣지 않음). 버전 = 플러그인 DLL 버전, 없으면 파일 이름의 _V2_1_0
    /// </summary>
    static Addon? ManosabaLoader(string zipPath, string cacheDir)
    {
        string? dllVersion;
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var dll = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(LoaderDll, StringComparison.OrdinalIgnoreCase));
            if (dll is null) return null;
            dllVersion = FileVersion(dll);
        }
        var version = dllVersion
            ?? (Regex.Match(Path.GetFileName(zipPath), @"_V(\d+)_(\d+)_(\d+)", RegexOptions.IgnoreCase) is { Success: true } m
                ? $"{m.Groups[1].Value}.{m.Groups[2].Value}.{m.Groups[3].Value}" : "0.0.0");
        var manifest = new Manifest(1, ManosabaLoaderId, "Manosaba Loader", version, "ManosabaLoader (.msmod) · KOKOTO-DEV · LGPL-2.1",
            Manosaba, new() { ["bepinex6"] = new("6.0.0-be.785") }) { Tags = ["msmod", "utility"] };
        return Converted(zipPath, cacheDir, manifest, dest => Catalogs.Repackage(zipPath, dest, manifest, new() { ["bepinex6"] = ["BepInEx/plugins/ManosabaLoader/"] }));
    }

    /// <summary>
    /// ManosabaLoader 모드 팩(.msmod). 게임 폴더의 ManosabaMod/에 그대로 넣고, ManosabaLoader를 먼저 요구함.
    /// 암호화돼 있어 안을 읽지 않음: 이름 = 파일 이름, 버전 = 파일 수정 날짜(년.월.일)
    /// </summary>
    static Addon? Msmod(string file, string cacheDir)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var safe = Regex.Replace(name, "[^A-Za-z0-9_.-]", "_");
        // 이름에 한글 등이 있으면 안전한 id가 겹칠 수 있어 원래 이름의 해시를 붙임
        var id = $"msmod.{safe}-{(uint)StableHash(name):x8}";
        var version = File.GetLastWriteTime(file).ToString("yyyy.M.d");
        var manifest = new Manifest(1, id, name, version, "ManosabaLoader .msmod", Manosaba, new() { ["bepinex6"] = new(null) })
        {
            Tags = ["msmod"],
            Requires = [ManosabaLoaderId],
        };
        return Converted(file, cacheDir, manifest, dest =>
        {
            using var zip = ZipFile.Open(dest, ZipArchiveMode.Create);
            using (var w = new StreamWriter(zip.CreateEntry("lxium.json").Open()))
                w.Write(JsonSerializer.Serialize(manifest, Addon.Json));
            zip.CreateEntryFromFile(file, $"bepinex6/ManosabaMod/{Path.GetFileName(file)}");
        });
    }

    /// <summary>바꾼 zip을 원본 크기·수정 시각으로 캐시. 원본이 그대로면 다시 만들지 않음</summary>
    static Addon Converted(string source, string cacheDir, Manifest manifest, Action<string> build)
    {
        var info = new FileInfo(source);
        var dir = Path.Combine(cacheDir, "converted");
        var dest = Path.Combine(dir, $"{manifest.Id}-{manifest.Version}-{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}.zip");
        if (!File.Exists(dest))
        {
            Directory.CreateDirectory(dir);
            var tmp = dest + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            build(tmp);
            File.Move(tmp, dest, true);
        }
        return Addon.Load(dest);
    }

    /// <summary>zip 안 DLL의 제품 버전 x.y.z. 읽을 수 없으면 null</summary>
    static string? FileVersion(ZipArchiveEntry dll)
    {
        var tmp = Path.GetTempFileName();
        try
        {
            dll.ExtractToFile(tmp, true);
            var v = FileVersionInfo.GetVersionInfo(tmp).ProductVersion?.Split('+')[0];
            return v is not null && Regex.IsMatch(v, @"^\d+\.\d+\.\d+$") ? v : null;
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>실행마다 같은 값이 나오는 문자열 해시 (string.GetHashCode는 실행마다 다름)</summary>
    static int StableHash(string s)
    {
        unchecked
        {
            var h = (int)2166136261;
            foreach (var c in s) h = (h ^ c) * 16777619;
            return h;
        }
    }
}
