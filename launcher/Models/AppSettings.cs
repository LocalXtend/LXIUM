using System.Text.Json;

namespace LXIUM.Models;

/// <summary>
/// 런처 설정. 실행 파일 옆 settings.json (포터블). 런처 업데이트는 이 파일을 덮어쓰지 않으므로 설정이 유지됩니다.
/// </summary>
public sealed class AppSettings
{
    /// <summary>공식 애드온 모음. 저장소 루트의 catalog.json</summary>
    public const string OfficialCatalog = "https://raw.githubusercontent.com/LocalXtend/LXIUM/dev/catalog.json";
    public const string FileName = "settings.json";

    /// <summary>시작할 때 런처 업데이트·애드온 모음을 확인</summary>
    public bool AutoUpdate { get; set; } = true;
    public List<string> Catalogs { get; set; } = [OfficialCatalog];
    /// <summary>런처 언어 (lang/&lt;code&gt;.json). 없으면 처음 실행 때 언어 선택 화면을 보여 줌</summary>
    public string? Language { get; set; }

    /// <summary>애드온 모음을 공식 모음 하나로 되돌림</summary>
    public void ResetCatalogs() => Catalogs = [OfficialCatalog];

    /// <summary>파일이 없거나 깨졌으면 기본값</summary>
    public static AppSettings Load(string dir)
    {
        try
        {
            var path = Path.Combine(dir, FileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Addon.Json) ?? new() : new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    public void Save(string dir) => File.WriteAllText(Path.Combine(dir, FileName), JsonSerializer.Serialize(this, Addon.Json));
}
