using System.ComponentModel;
using System.Text.Json;

namespace LXIUM.Models;

public sealed record LanguageInfo(string Code, string Name);

/// <summary>
/// 런처 문구 (lang/&lt;code&gt;.json). 키가 없으면 ko.json, 그래도 없으면 키 자체를 보여 줍니다.
/// 언어를 바꾸면 PropertyChanged("Item[]")로 화면의 모든 {l:T} 바인딩이 다시 그려집니다.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>기준 언어. 모든 키가 있어야 함</summary>
    public const string Base = "ko";

    public static Loc I { get; } = new();

    Dictionary<string, string> strings = [];
    Dictionary<string, string> fallback = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Code { get; private set; } = Base;

    public string this[string key] => strings.GetValueOrDefault(key) ?? fallback.GetValueOrDefault(key) ?? key;

    /// <summary>지금 언어의 문구. args가 있으면 {0} {1} 자리에 넣음</summary>
    public static string T(string key, params object?[] args) => args.Length == 0 ? I[key] : string.Format(I[key], args);

    public static string LangDir(string appDir) => Path.Combine(appDir, "lang");

    /// <summary>lang 폴더의 언어들. 파일의 "_name"이 표시 이름</summary>
    public static List<LanguageInfo> Available(string appDir)
    {
        var dir = LangDir(appDir);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.json")
            .Select(f => new LanguageInfo(Path.GetFileNameWithoutExtension(f), Read(f).GetValueOrDefault("_name") ?? Path.GetFileNameWithoutExtension(f)))
            .OrderBy(l => l.Code == Base ? "" : l.Code, StringComparer.Ordinal).ToList();
    }

    public void Load(string appDir, string code)
    {
        var dir = LangDir(appDir);
        fallback = Read(Path.Combine(dir, $"{Base}.json"));
        strings = code == Base ? fallback : Read(Path.Combine(dir, $"{code}.json"));
        Code = code;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
    }

    static Dictionary<string, string> Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [] : []; }
        catch (Exception) { return []; }
    }
}
