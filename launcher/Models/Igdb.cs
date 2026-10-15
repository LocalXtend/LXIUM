using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LXIUM.Models;

public sealed record GameInfo(string Name, string? Summary, string? CoverUrl, string? Released,
    string[] Genres, string[] Developers, int? Rating);

/// <summary>런처 폴더의 settings.json</summary>
public sealed record Settings(string? IgdbClientId, string? IgdbClientSecret)
{
    public static Settings Load(string dir)
    {
        var path = Path.Combine(dir, "settings.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Addon.Json) ?? new(null, null)
            : new(null, null);
    }
}

/// <summary>IGDB API v4. Twitch 앱의 Client ID·Secret으로 토큰을 받아 게임 정보를 찾습니다.</summary>
public static class Igdb
{
    static readonly HttpClient Http = new();
    static string? token;

    const string Fields = "fields name,summary,first_release_date,total_rating,cover.image_id,genres.name," +
                          "involved_companies.developer,involved_companies.company.name;";

    /// <summary>Steam 앱 ID로 먼저 찾고, 없으면 이름으로 검색합니다.</summary>
    public static async Task<GameInfo?> FindAsync(GameRef game, Settings s)
    {
        if (string.IsNullOrWhiteSpace(s.IgdbClientId) || string.IsNullOrWhiteSpace(s.IgdbClientSecret))
            throw new InvalidOperationException("런처 폴더의 settings.json에 igdbClientId·igdbClientSecret을 넣으면 IGDB 정보를 보여 줍니다.");
        token ??= await TokenAsync(s);
        JsonElement? hit = null;
        if (game.SteamAppId is int id)
            hit = await QueryAsync(s, $"{Fields} where external_games.uid = \"{id}\" & external_games.external_game_source = 1; limit 1;");
        hit ??= await QueryAsync(s, $"{Fields} search \"{game.Name.Replace("\"", "")}\"; limit 1;");
        return hit is JsonElement g ? Parse(g) : null;
    }

    static async Task<string> TokenAsync(Settings s)
    {
        using var res = await Http.PostAsync("https://id.twitch.tv/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = s.IgdbClientId!, ["client_secret"] = s.IgdbClientSecret!, ["grant_type"] = "client_credentials",
        }));
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"IGDB 인증 실패 ({(int)res.StatusCode}). settings.json의 키를 확인하세요.");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>첫 결과를 돌려줍니다. 결과가 없거나 요청이 거부되면 null.</summary>
    static async Task<JsonElement?> QueryAsync(Settings s, string body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games")
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain"),
        };
        req.Headers.Add("Client-ID", s.IgdbClientId);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0].Clone() : null;
    }

    public static GameInfo Parse(JsonElement g)
    {
        string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) ? v.GetString() : null;
        IEnumerable<JsonElement> Arr(JsonElement e, string n) =>
            e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

        var released = g.TryGetProperty("first_release_date", out var d)
            ? DateTimeOffset.FromUnixTimeSeconds(d.GetInt64()).ToString("yyyy-MM-dd") : null;
        var cover = g.TryGetProperty("cover", out var c) && Str(c, "image_id") is string img
            ? $"https://images.igdb.com/igdb/image/upload/t_cover_big/{img}.jpg" : null;
        var devs = Arr(g, "involved_companies")
            .Where(ic => ic.TryGetProperty("developer", out var dev) && dev.GetBoolean())
            .Select(ic => ic.TryGetProperty("company", out var co) ? Str(co, "name") : null)
            .OfType<string>().ToArray();
        int? rating = g.TryGetProperty("total_rating", out var r) ? (int)Math.Round(r.GetDouble()) : null;
        return new GameInfo(Str(g, "name") ?? "", Str(g, "summary"), cover, released,
            Arr(g, "genres").Select(x => Str(x, "name")).OfType<string>().ToArray(), devs, rating);
    }
}
