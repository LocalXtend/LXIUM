using System.Net;
using System.Text.Json;

namespace LXIUM.Models;

public sealed record GameInfo(string Name, string? Summary, string? Released,
    string[] Genres, string[] Developers, string[] Publishers);

/// <summary>Steam 상점 appdetails API. 키 없이 앱 ID로 게임 정보를 가져옵니다(한국어 우선).</summary>
public static class SteamStore
{
    static readonly HttpClient Http = new();

    /// <summary>앱 ID가 없거나 상점에 없는 게임이면 null</summary>
    public static async Task<GameInfo?> FindAsync(int appId)
    {
        var json = await Http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=koreana");
        using var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement.GetProperty(appId.ToString()));
    }

    /// <summary>appdetails 응답에서 앱 하나({"success":..,"data":{..}})를 해석합니다.</summary>
    public static GameInfo? Parse(JsonElement app)
    {
        if (!app.TryGetProperty("success", out var ok) || !ok.GetBoolean() || !app.TryGetProperty("data", out var d)) return null;
        string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        string[] Strs(string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.GetString()).OfType<string>().ToArray() : [];

        var genres = d.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array
            ? g.EnumerateArray().Select(x => Str(x, "description")).OfType<string>().ToArray() : [];
        var released = d.TryGetProperty("release_date", out var r) ? Str(r, "date") : null;
        var summary = Str(d, "short_description");
        return new GameInfo(Str(d, "name") ?? "", summary is null ? null : WebUtility.HtmlDecode(summary),
            string.IsNullOrWhiteSpace(released) ? null : released, genres, Strs("developers"), Strs("publishers"));
    }
}
