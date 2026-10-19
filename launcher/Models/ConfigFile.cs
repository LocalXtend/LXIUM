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
    public string Section { get; init; } = "";
    /// <summary>화면에 보일 이름 (.lxconfig Name, 없으면 Key)</summary>
    public string DisplayName { get; internal set; } = "";
    public string Value
    {
        get => value;
        set { this.value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
    }
    public string? Description { get; internal set; }
    public string? Type { get; internal set; }
    public string? Default { get; internal set; }
    /// <summary>BepInEx "Acceptable values" (열거형). 없으면 빈 배열</summary>
    public string[] Choices { get; internal set; } = [];
    /// <summary>여러 값을 쉼표로 함께 고르는 플래그 열거형 (BepInEx "Multiple values can be set")</summary>
    public bool Flags { get; internal set; }
    /// <summary>BepInEx "Acceptable value range" 등 값 범위 안내 ("From a to b")</summary>
    public string? Range { get; internal set; }

    /// <summary>파일의 줄 번호. -1이면 .lxconfig에만 있어 저장할 때 새로 넣는 항목</summary>
    internal int Line { get; set; }
    internal string Prefix { get; init; } = "";
    internal bool Quoted { get; set; }
    internal string Original { get; set; } = "";

    /// <summary>편집기 종류는 처음 읽은 값으로 정합니다 (입력 중에 바뀌지 않게)</summary>
    public bool IsBool => Type is "Boolean" || (Type is null && Original is "true" or "false");
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
        Type, Range, Flags ? Loc.T("cfg.flags", string.Join(", ", Choices)) : null, Default is null ? null : Loc.T("cfg.default", Default),
    }.Where(s => !string.IsNullOrEmpty(s)));
}

public sealed record ConfigSection(string Name, List<ConfigEntry> Entries)
{
    public string DisplayName { get; internal set; } = Name;
}

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
    /// <summary>파일이 아직 없어 .lxconfig 기본값으로 만든 것 (저장하면 파일이 생김)</summary>
    public bool IsNew { get; }
    readonly List<string> lines;
    readonly string newline;
    readonly bool toml;

    ConfigFile(string path, string text, bool isNew, bool toml)
    {
        Path = path;
        IsNew = isNew;
        this.toml = toml;
        newline = text.Contains("\r\n") ? "\r\n" : "\n";
        lines = [.. text.Split(newline)];

        var section = new ConfigSection("", []);
        var desc = new List<string>();
        string? type = null, def = null, range = null;
        string[] choices = [];
        var flags = false;
        for (var i = 0; i < lines.Count; i++)
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
                        Key = m.Groups[2].Value, Section = section.Name, DisplayName = m.Groups[2].Value, Value = value, Original = value, Line = i, Prefix = m.Groups[1].Value, Quoted = quoted,
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

    public static ConfigFile Load(string path) => new(path, File.ReadAllText(path), false, IsToml(path));

    /// <summary>MelonLoader(UserData) 설정은 TOML: 문자열을 따옴표로 씀</summary>
    static bool IsToml(string path) => path.Replace('\\', '/').Contains("/UserData/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 설정 파일을 엽니다. lx가 있으면 형식·기본값·설명(lang 언어)을 .lxconfig에서 가져오고,
    /// 파일이 없으면 .lxconfig 기본값으로 새로 만들 준비를 합니다(게임을 실행하지 않아도 편집 가능). 둘 다 없으면 null
    /// </summary>
    public static ConfigFile? Open(string path, LxConfig? lx, string lang)
    {
        ConfigFile cfg;
        if (File.Exists(path)) cfg = Load(path);
        else if (lx is { Settings.Count: > 0 }) cfg = new ConfigFile(path, lx.CreateFile(IsToml(path)), true, IsToml(path));
        else return null;
        if (lx is not null) cfg.Apply(lx, lang);
        return cfg;
    }

    /// <summary>.lxconfig 정보를 항목에 입히고, 파일에 없는 설정은 기본값 항목으로 더합니다.</summary>
    void Apply(LxConfig lx, string lang)
    {
        var inv = CultureInfo.InvariantCulture;
        foreach (var s in lx.Settings)
        {
            var section = Sections.FirstOrDefault(x => x.Name == s.Section);
            if (section is null) Sections.Add(section = new ConfigSection(s.Section, []));
            var e = section.Entries.FirstOrDefault(x => x.Key == s.Key);
            if (e is null)
            {
                e = new ConfigEntry { Key = s.Key, Section = s.Section, Line = -1, Original = "\0", Quoted = toml && s.Type is "String" or "Enum" };
                e.Value = s.Default;
                section.Entries.Add(e);
            }
            e.Type = s.Type switch { "Bool" => "Boolean", "Int" => "Int32", "Float" => "Single", _ => s.Type };
            e.Default = s.Default;
            e.Choices = s.Values;
            e.Flags = false;
            e.Range = s.Min is double min && s.Max is double max ? $"From {min.ToString(inv)} to {max.ToString(inv)}" : null;
            e.Description = LxConfig.Pick(s.Descriptions, lang) ?? e.Description;
            e.DisplayName = LxConfig.Pick(s.Names, lang) ?? s.Key;
        }
        foreach (var section in Sections)
            if (lx.SectionNames.TryGetValue(section.Name, out var names)) section.DisplayName = LxConfig.Pick(names, lang) ?? section.Name;
    }

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
    public List<string> Invalid() => Sections.SelectMany(s => s.Entries).Where(e => !IsValid(e)).Select(e => $"{e.DisplayName}: {e.Value}").ToList();

    static bool IsValid(ConfigEntry e)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (e.IsBool) return e.Value is "true" or "false";
        if (e.Choices.Length > 0 && !e.Value.Split(',', StringSplitOptions.TrimEntries).All(e.Choices.Contains)) return false;
        return e.Type switch
        {
            "Int32" or "Int64" or "Int16" or "Byte" => long.TryParse(e.Value, System.Globalization.NumberStyles.Integer, inv, out var n) && InRange(e, n),
            "Single" or "Double" or "Decimal" => double.TryParse(e.Value, System.Globalization.NumberStyles.Float, inv, out var d) && InRange(e, d),
            _ => true,
        };
    }

    static bool InRange(ConfigEntry e, double v) => (e.Min is not double min || v >= min) && (e.Max is not double max || v <= max);

    public bool IsChanged => IsNew || Sections.SelectMany(s => s.Entries).Any(e => e.Value != e.Original);

    /// <summary>
    /// 바뀐 값의 줄만 다시 씁니다. 따옴표로 감싸져 있던 값은 따옴표·이스케이프를 유지합니다.
    /// .lxconfig에만 있던 항목은 그 섹션의 마지막 항목 뒤(섹션이 없으면 파일 끝)에 넣습니다.
    /// </summary>
    public void Save()
    {
        var changed = Sections.SelectMany(s => s.Entries).Where(e => e.Value != e.Original).ToList();
        foreach (var e in changed.Where(e => e.Line >= 0))
            lines[e.Line] = e.Prefix + Format(e);
        foreach (var e in changed.Where(e => e.Line < 0))
        {
            var last = Sections.First(s => s.Name == e.Section).Entries.Where(x => x.Line >= 0).Select(x => x.Line).DefaultIfEmpty(-1).Max();
            if (last < 0)
            {
                // 새 섹션은 파일 끝에. 파일이 줄바꿈으로 끝났으면 그 줄바꿈은 맨 끝에 그대로 둠
                var end = lines.Count > 0 && lines[^1] == "" ? lines.Count - 1 : lines.Count;
                List<string> block = end > 0 && lines[end - 1] != "" ? ["", $"[{e.Section}]"] : [$"[{e.Section}]"];
                lines.InsertRange(end, block);
                last = end + block.Count - 1;
            }
            var at = last + 1;
            lines.Insert(at, $"{e.Key} = {Format(e)}");
            foreach (var other in Sections.SelectMany(s => s.Entries).Where(x => x.Line >= at)) other.Line++;
            e.Line = at;
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, string.Join(newline, lines), new UTF8Encoding(false));
        foreach (var e in changed) e.Original = e.Value;
    }

    static string Format(ConfigEntry e) => e.Quoted ? "\"" + e.Value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : e.Value;
}
