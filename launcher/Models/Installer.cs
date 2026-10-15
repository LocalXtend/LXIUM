using System.IO.Compression;
using System.Text.Json;

namespace LXIUM.Models;

/// <summary>설치 기록. 게임 루트의 .lxium/&lt;id&gt;.json에 저장. 경로는 게임 루트 기준 상대 경로(/ 구분)</summary>
public sealed record InstallRecord(string Id, string Version, string Variant, List<string> Files, List<string> Backups);

/// <summary>애드온 설치·제거 (docs/ai/addon.md 6장)</summary>
public static class Installer
{
    const string StateDir = ".lxium";

    static string RecordPath(string root, string id) => Path.Combine(root, StateDir, $"{id}.json");
    static string BackupDir(string root, string id) => Path.Combine(root, StateDir, "backup", id);

    public static InstallRecord? Read(string root, string id)
    {
        var path = RecordPath(root, id);
        return File.Exists(path) ? JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(path), Addon.Json) : null;
    }

    /// <summary>감지된 로더로 쓸 변형을 고릅니다. 고를 수 없으면 Error에 이유를 담습니다.</summary>
    public static (string? Variant, string? Error) PickVariant(Manifest m, string? loader)
    {
        if (loader is not null && m.Variants.ContainsKey(loader)) return (loader, null);
        if (m.Variants.ContainsKey("none")) return ("none", null);
        var wanted = string.Join(", ", m.Variants.Keys.Select(k => Loaders.Names.GetValueOrDefault(k, k)));
        return loader is null
            ? (null, $"게임에 모드 로더가 없습니다. {wanted}를 먼저 설치하세요.")
            : (null, $"이 모드는 {Loaders.Names.GetValueOrDefault(loader, loader)}를 지원하지 않습니다. 필요한 로더: {wanted}");
    }

    /// <summary>
    /// 경로 조각이나 파일 이름이 애드온 id와 같으면 그 애드온 자신의 파일입니다
    /// (BepInEx/plugins/&lt;id&gt;/, Mods/&lt;id&gt;.dll). 원본 게임 파일이 아니므로 백업·복원하지 않습니다.
    /// </summary>
    static bool IsOwn(string rel, string id) =>
        rel.Split('/').Any(s => s.Equals(id, StringComparison.OrdinalIgnoreCase)
                                || Path.GetFileNameWithoutExtension(s).Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>설치 기록 없이 게임 폴더에 있는 이 애드온의 파일 (수동 복사, 기록 손실). 기록이 있으면 빈 목록</summary>
    public static List<string> FindUntracked(Addon addon, string root)
    {
        var id = addon.Manifest.Id;
        if (Read(root, id) is not null) return [];
        using var zip = ZipFile.OpenRead(addon.ZipPath);
        return zip.Entries
            .Where(e => e.Name != "")
            .Select(e => e.FullName.Replace('\\', '/'))
            .Select(n => (Variant: n[..Math.Max(n.IndexOf('/'), 0)], Rel: n[(n.IndexOf('/') + 1)..]))
            .Where(x => addon.Manifest.Variants.ContainsKey(x.Variant) && IsOwn(x.Rel, id) && File.Exists(SafePath(root, x.Rel)))
            .Select(x => x.Rel).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 변형 폴더를 게임 루트에 복사합니다. 이미 설치돼 있으면(기록 없는 설치 포함) 먼저 제거합니다(업데이트).
    /// 덮어쓰는 파일은 백업하고, 중간에 실패해도 그때까지 한 일을 기록해 제거로 되돌릴 수 있게 합니다.
    /// </summary>
    public static void Install(Addon addon, string variant, string root)
    {
        var id = addon.Manifest.Id;
        Uninstall(addon, root);

        var record = new InstallRecord(id, addon.Manifest.Version, variant, [], []);
        var prefix = variant + "/";
        try
        {
            using var zip = ZipFile.OpenRead(addon.ZipPath);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || entry.Name == "") continue;
                var rel = name[prefix.Length..];
                var dest = SafePath(root, rel);
                if (File.Exists(dest))
                {
                    var backup = SafePath(BackupDir(root, id), rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(dest, backup, true);
                    record.Backups.Add(rel);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                record.Files.Add(rel);
                entry.ExtractToFile(dest, true);
            }
        }
        finally
        {
            Directory.CreateDirectory(Path.Combine(root, StateDir));
            File.WriteAllText(RecordPath(root, id), JsonSerializer.Serialize(record, Addon.Json));
        }
    }

    /// <summary>
    /// 기록된 파일 삭제 → 백업 복원 → 비게 된 폴더 삭제. 기록에 없는 파일(설정·로그)은 남깁니다.
    /// 기록이 없으면 게임 폴더에 있는 이 애드온 자신의 파일만 지웁니다.
    /// </summary>
    public static void Uninstall(Addon addon, string root) => Uninstall(root, addon.Manifest.Id, () => FindUntracked(addon, root));

    /// <summary>zip 없이 id로 제거. 기록이 없을 때 지울 파일은 untracked가 알려 줌 (없으면 아무것도 안 지움)</summary>
    public static void Uninstall(string root, string id, Func<List<string>>? untracked = null)
    {
        var record = Read(root, id);
        foreach (var rel in record?.Files ?? untracked?.Invoke() ?? [])
        {
            var path = SafePath(root, rel);
            if (File.Exists(path)) File.Delete(path);
            RemoveEmptyDirs(root, Path.GetDirectoryName(path)!);
        }
        if (record is null) return;
        var backupDir = BackupDir(root, id);
        foreach (var rel in record.Backups)
        {
            var backup = SafePath(backupDir, rel);
            // 0.1.x는 이미 있던 같은 모드 파일도 백업했으므로, 그런 백업은 복원하지 않음
            if (!File.Exists(backup) || IsOwn(rel, id)) continue;
            var dest = SafePath(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Move(backup, dest, true);
        }
        if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
        File.Delete(RecordPath(root, id));
        RemoveEmptyDirs(root, Path.Combine(root, StateDir, "backup"));
    }

    /// <summary>baseDir 밖을 가리키는 경로(절대 경로, ..)는 거부합니다.</summary>
    internal static string SafePath(string baseDir, string rel)
    {
        var full = Path.GetFullPath(Path.Combine(baseDir, rel));
        var basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir)) + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(rel) || !full.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"허용되지 않는 경로: {rel}");
        return full;
    }

    internal static void RemoveEmptyDirs(string root, string dir)
    {
        var stop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        // 이미 없는 폴더는 건너뛰고 위로 올라가며, 비어 있지 않은 폴더를 만나면 멈춥니다.
        for (var d = Path.GetFullPath(dir); d.Length > stop.Length; d = Path.GetDirectoryName(d)!)
        {
            if (!Directory.Exists(d)) continue;
            if (Directory.EnumerateFileSystemEntries(d).Any()) break;
            Directory.Delete(d);
        }
    }
}
