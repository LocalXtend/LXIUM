using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LXIUM.Models;

/// <summary>.lxconfig의 설정 하나</summary>
public sealed class LxSetting(string section, string key, string type, string @default)
{
    public string Section { get; } = section;
    public string Key { get; } = key;
    /// <summary>Bool, Int, Float, String, Enum</summary>
    public string Type { get; } = type;
    public string Default { get; } = @default;
    public double? Min { get; init; }
    public double? Max { get; init; }
    public string[] Values { get; init; } = [];
    /// <summary>언어 코드 → 설명</summary>
    public Dictionary<string, string> Descriptions { get; } = [];
    /// <summary>언어 코드 → 표시 이름</summary>
    public Dictionary<string, string> Names { get; } = [];
}

/// <summary>
/// 모드 설정 설명 파일 &lt;id&gt;.lxconfig (docs/ai/lxconfig.md). 설정 파일(.cfg)에는 주석을 두지 않고
/// 형식·기본값·언어별 설명을 여기에 둡니다. 런처는 이것으로 게임을 실행하기 전에도 설정 파일을 만들고 편집합니다.
/// <code>
/// Section FrameRate
/// Set TargetFps Int 0 Range -1 240
/// Lang TargetFps ko "fps 제한을 바꿉니다.\n(-1은 제한 없음)"
/// Name TargetFps ko "목표 fps"
/// </code>
/// </summary>
public sealed class LxConfig
{
    static readonly Regex Token = new(@"""((?:[^""\\]|\\.)*)""|(\S+)");

    public List<LxSetting> Settings { get; } = [];
    /// <summary>섹션 이름 → (언어 → 표시 이름)</summary>
    public Dictionary<string, Dictionary<string, string>> SectionNames { get; } = [];
    /// <summary>읽다가 무시한 줄 ("줄 번호: 이유")</summary>
    public List<string> Errors { get; } = [];

    public static LxConfig Parse(string text)
    {
        var cfg = new LxConfig();
        var section = "";
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var t = Token.Matches(line).Select(m => m.Groups[1].Success ? Unescape(m.Groups[1].Value) : m.Groups[2].Value).ToList();
            try
            {
                switch (t[0])
                {
                    case "Section" when t.Count >= 2:
                        section = t[1];
                        break;
                    case "Set" when t.Count >= 4:
                        cfg.Settings.Add(ParseSet(section, t));
                        break;
                    case "Lang" when t.Count >= 4:
                        cfg.Find(t[1]).Descriptions[t[2]] = t[3];
                        break;
                    case "Name" when t.Count >= 4:
                        if (cfg.Settings.Any(s => s.Key == t[1] || $"{s.Section}.{s.Key}" == t[1])) cfg.Find(t[1]).Names[t[2]] = t[3];
                        else (cfg.SectionNames.TryGetValue(t[1], out var n) ? n : cfg.SectionNames[t[1]] = [])[t[2]] = t[3];
                        break;
                    default:
                        throw new FormatException(t[0]);
                }
            }
            catch (Exception e)
            {
                cfg.Errors.Add($"{i + 1}: {e.Message}");
            }
        }
        return cfg;
    }

    static LxSetting ParseSet(string section, List<string> t)
    {
        if (section == "") throw new FormatException("Section 없음");
        var type = t[2];
        if (type is not ("Bool" or "Int" or "Float" or "String" or "Enum")) throw new FormatException($"형식 {type}");
        double? min = null, max = null;
        string[] values = [];
        for (var k = 4; k < t.Count; k++)
        {
            if (t[k] == "Range" && k + 2 < t.Count)
            {
                min = double.Parse(t[k + 1], CultureInfo.InvariantCulture);
                max = double.Parse(t[k + 2], CultureInfo.InvariantCulture);
                k += 2;
            }
            else if (t[k] == "Values" && k + 1 < t.Count)
                values = t[++k].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        if (type == "Enum" && values.Length == 0) throw new FormatException("Enum에 Values 없음");
        return new LxSetting(section, t[1], type, t[3]) { Min = min, Max = max, Values = values };
    }

    /// <summary>"Key" 또는 "Section.Key"로 찾음</summary>
    public LxSetting Find(string key) =>
        Settings.LastOrDefault(s => $"{s.Section}.{s.Key}" == key) ?? Settings.LastOrDefault(s => s.Key == key)
        ?? throw new FormatException($"Set 없는 키 {key}");

    public LxSetting? Get(string section, string key) => Settings.LastOrDefault(s => s.Section == section && s.Key == key);

    /// <summary>지금 언어 → en → ko → 아무거나</summary>
    public static string? Pick(Dictionary<string, string> byLang, string lang) =>
        byLang.GetValueOrDefault(lang) ?? byLang.GetValueOrDefault("en") ?? byLang.GetValueOrDefault(Loc.Base) ?? byLang.Values.FirstOrDefault();

    /// <summary>기본값으로 새 설정 파일 내용을 만듭니다. toml이면 문자열·선택지 값을 따옴표로 감쌈 (MelonLoader)</summary>
    public string CreateFile(bool toml)
    {
        var sb = new StringBuilder();
        foreach (var group in Settings.GroupBy(s => s.Section))
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append('[').Append(group.Key).Append("]\n");
            foreach (var s in group)
                sb.Append(s.Key).Append(" = ").Append(Format(s, s.Default, toml)).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>파일에 쓸 값 표현. BepInEx는 따옴표 없이, TOML은 String·Enum을 따옴표로</summary>
    public static string Format(LxSetting s, string value, bool toml) =>
        toml && s.Type is "String" or "Enum" ? "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : value;

    static string Unescape(string s) => Regex.Replace(s, @"\\(.)", m => m.Groups[1].Value == "n" ? "\n" : m.Groups[1].Value);
}
