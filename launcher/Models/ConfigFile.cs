using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LXIUM.Models;

/// <summary>설정 항목 하나. Value를 바꾼 뒤 ConfigFile.Save로 저장합니다. Value가 바뀌면 화면의 다른 편집기(슬라이더·글)도 따라 바뀝니다.</summary>
public sealed class ConfigEntry : INotifyPropertyChanged
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    string value = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Key { get; init; }
    public string Value
    {
        get => value;
        set { this.value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
    }
    public string? Description { get; init; }
    public string? Type { get; init; }
    public string? Default { get; init; }
    /// <summary>BepInEx "Acceptable values" (열거형). 없으면 빈 배열</summary>
    public string[] Choices { get; init; } = [];
    /// <summary>여러 값을 쉼표로 함께 고르는 플래그 열거형 (BepInEx "Multiple values can be set")</summary>
    public bool Flags { get; init; }
    /// <summary>BepInEx "Acceptable value range" 등 값 범위 안내</summary>
    public string? Range { get; init; }

    internal int Line { get; init; }
    internal string Prefix { get; init; } = "";
    internal bool Quoted { get; init; }
    internal string Original { get; set; } = "";

    /// <summary>편집기 종류는 처음 읽은 값으로 정합니다 (입력 중에 바뀌지 않게)</summary>
    public bool IsBool => Type is "Boolean" || Original is "true" or "false";
    public bool HasChoices => !IsBool && !Flags && Choices.Length > 0;
    public bool IsSlider => !IsBool && !HasChoices && !Flags && Min is not null && Max is not null && Min < Max;
    public bool IsText => !IsBool && !HasChoices && !IsSlider;
    public bool BoolValue { get => Value == "true"; set => Value = value ? "true" : "false"; }

    /// <summary>Range "From a to b"의 a·b. 없으면 null</summary>
    public double? Min => RangeNumber(1);
    public double? Max => RangeNumber(2);
    bool IsInteger => Type is "Int32" or "Int64" or "Int16" or "Byte" or "SByte" or "UInt16" or "UInt32";
    public double TickFrequency => IsInteger ? 1 : 0;
    public bool Snap => IsInteger;

    /// <summary>슬라이더 값. 정수 형식이면 반올림해 씁니다.</summary>
    public double NumberValue
    {
        get => double.TryParse(Value, NumberStyles.Float, Inv, out var v) ? v : Min ?? 0;
        set => Value = IsInteger ? Math.Round(value).ToString(Inv) : Math.Round(value, 2).ToString(Inv);
    }

    double? RangeNumber(int group) =>
        Range is not null && Regex.Match(Range, @"From\s+(-?[\d.]+)\s+to\s+(-?[\d.]+)") is { Success: true } m
        && double.TryParse(m.Groups[group].Value, NumberStyles.Float, Inv, out var v) ? v : null;
    public string Hint => string.Join(" · ", new[]
    {
        Type, Range, Flags ? "쉼표로 여러 개: " + string.Join(", ", Choices) : null, Default is null ? null : $"기본값 {Default}",
    }.Where(s => !string.IsNullOrEmpty(s)));
}

public sealed record ConfigSection(string Name, List<ConfigEntry> Entries);

/// <summary>
/// BepInEx .cfg와 MelonLoader .cfg(TOML)의 공통 형태 "[섹션]" + "키 = 값"을 줄 단위로 읽고,
/// 저장할 때는 값이 바뀐 줄만 고쳐 주석·순서·다른 줄을 그대로 둡니다.
/// 여러 줄에 걸친 TOML 값(배열·문자열)은 편집 대상에서 뺍니다.
/// </summary>
public sealed class ConfigFile
{
    static readonly Regex EntryLine = new(@"^(\s*([^=#\[\s][^=]*?)\s*=\s*)(.*?)\s*$");

    public string Path { get; }
    public List<ConfigSection> Sections { get; } = [];
    readonly string[] lines;
    readonly string newline;

    ConfigFile(string path, string text)
    {
        Path = path;
        newline = text.Contains("\r\n") ? "\r\n" : "\n";
        lines = text.Split(newline);

        var section = new ConfigSection("", []);
        var desc = new List<string>();
        string? type = null, def = null, range = null;
        string[] choices = [];
        var flags = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[') && t.EndsWith(']') && !t.Contains('='))
            {
                if (section.Entries.Count > 0) Sections.Add(section);
                section = new ConfigSection(t[1..^1].Trim().Trim('"'), []);
                desc.Clear(); type = def = range = null; choices = []; flags = false;
            }
            else if (t.StartsWith('#'))
            {
                var c = t.TrimStart('#').Trim();
                if (c.StartsWith("Setting type:")) type = c["Setting type:".Length..].Trim();
                else if (c.StartsWith("Default value:")) def = c["Default value:".Length..].Trim();
                else if (c.StartsWith("Acceptable values:")) choices = c["Acceptable values:".Length..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                else if (c.StartsWith("Multiple values can be set")) flags = true;
                else if (c.StartsWith("Acceptable value range:")) range = c["Acceptable value range:".Length..].Trim();
                else if (c.Length > 0 && !c.StartsWith("Settings file was created") && !c.StartsWith("Plugin GUID:")) desc.Add(c);
            }
            else if (EntryLine.Match(lines[i]) is { Success: true } m)
            {
                var raw = m.Groups[3].Value;
                var multiline = raw.StartsWith("\"\"\"") || raw.StartsWith("'''") || (raw.StartsWith('[') && !raw.EndsWith(']'));
                if (!multiline)
                {
                    var quoted = raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"';
                    var value = quoted ? raw[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\") : raw;
                    section.Entries.Add(new ConfigEntry
                    {
                        Key = m.Groups[2].Value, Value = value, Original = value, Line = i, Prefix = m.Groups[1].Value, Quoted = quoted,
                        Description = desc.Count > 0 ? string.Join("\n", desc) : null, Type = type, Default = def, Choices = choices, Range = range, Flags = flags,
                    });
                }
                desc.Clear(); type = def = range = null; choices = []; flags = false;
            }
            else if (t.Length == 0)
            {
                // 빈 줄 뒤의 주석은 다음 항목의 설명이 아님 (TOML 머리 주석 등). BepInEx는 설명과 키 사이에 빈 줄이 없음
                if (type is null) desc.Clear();
            }
        }
        if (section.Entries.Count > 0) Sections.Add(section);
    }

    public static ConfigFile Load(string path) => new(path, File.ReadAllText(path));

    /// <summary>애드온 설정 파일 (addon.md 5장): BepInEx = BepInEx/config/&lt;id&gt;.cfg, MelonLoader = UserData/&lt;id&gt;.cfg</summary>
    public static string ModConfigPath(string root, string id, string variant) =>
        System.IO.Path.Combine(root, variant == "melonloader" ? $"UserData/{id}.cfg" : $"BepInEx/config/{id}.cfg");

    /// <summary>로더 자체 설정 파일. 런처가 모르는 로더면 null</summary>
    public static string? LoaderConfigPath(string root, string loader) => loader switch
    {
        "bepinex5" or "bepinex6" => System.IO.Path.Combine(root, "BepInEx/config/BepInEx.cfg"),
        "melonloader" => System.IO.Path.Combine(root, "UserData/Loader.cfg"),
        _ => null,
    };

    /// <summary>BepInEx 형식 정보로 확인되는 잘못된 값 ("키: 값" 목록)</summary>
    public List<string> Invalid() => Sections.SelectMany(s => s.Entries).Where(e => !IsValid(e)).Select(e => $"{e.Key}: {e.Value}").ToList();

    static bool IsValid(ConfigEntry e)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (e.IsBool) return e.Value is "true" or "false";
        if (e.Choices.Length > 0 && !e.Value.Split(',', StringSplitOptions.TrimEntries).All(e.Choices.Contains)) return false;
        return e.Type switch
        {
            "Int32" or "Int64" or "Int16" or "Byte" => long.TryParse(e.Value, System.Globalization.NumberStyles.Integer, inv, out _),
            "Single" or "Double" or "Decimal" => double.TryParse(e.Value, System.Globalization.NumberStyles.Float, inv, out _),
            _ => true,
        };
    }

    public bool IsChanged => Sections.SelectMany(s => s.Entries).Any(e => e.Value != e.Original);

    /// <summary>바뀐 값의 줄만 다시 씁니다. 따옴표로 감싸져 있던 값은 따옴표·이스케이프를 유지합니다.</summary>
    public void Save()
    {
        var changed = Sections.SelectMany(s => s.Entries).Where(e => e.Value != e.Original).ToList();
        foreach (var e in changed)
            lines[e.Line] = e.Prefix + (e.Quoted ? "\"" + e.Value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : e.Value);
        File.WriteAllText(Path, string.Join(newline, lines), new UTF8Encoding(false));
        foreach (var e in changed) e.Original = e.Value;
    }
}
