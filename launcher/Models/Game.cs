namespace LXIUM.Models;

/// <summary>애드온이 있는 게임 하나. game.id로 애드온을 묶습니다.</summary>
public sealed class Game(GameRef info, List<Addon> addons, string? root)
{
    public GameRef Info { get; } = info;
    public List<Addon> Addons { get; } = addons;
    /// <summary>게임 루트. Steam 라이브러리에서 찾지 못하면 null</summary>
    public string? Root { get; } = root;
    public string Name => Info.Name;
    public string Status { get; set; } = "";

    public string? CapsuleUrl => Info.SteamAppId is int id ? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/library_600x900.jpg" : null;
    public string? HeaderUrl => Info.SteamAppId is int id ? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/header.jpg" : null;

    public static List<Game> Build(IEnumerable<Addon> addons, IReadOnlyList<string> libraries) =>
        addons.GroupBy(a => a.Manifest.Game.Id)
            .Select(g => new Game(g.First().Manifest.Game, g.ToList(), Steam.FindGame(libraries, g.First().Manifest.Game)))
            .OrderBy(g => g.Name).ToList();
}

/// <summary>상세 화면의 애드온 한 줄</summary>
public sealed record AddonRow(Addon Addon, InstallRecord? Installed)
{
    Manifest M => Addon.Manifest;
    public string Title => M.Name;
    public string Detail => $"v{M.Version} · " + string.Join(", ", M.Variants.Keys.Select(k => Loaders.Names.GetValueOrDefault(k, k)));
    public string? Description => M.Description;
    public bool IsInstalled => Installed is not null;
    public string Status => Installed is null ? "미설치"
        : Installed.Version == M.Version ? $"설치됨 · {Loaders.Names.GetValueOrDefault(Installed.Variant, Installed.Variant)}"
        : $"v{Installed.Version} 설치됨 · 업데이트 가능";
    public string InstallLabel => Installed is null ? "설치" : Installed.Version == M.Version ? "재설치" : "업데이트";
}

public sealed record GameDetail(Game Game, List<LoaderInfo> Loaders, List<AddonRow> Addons)
{
    public string RootText => Game.Root ?? "Steam 라이브러리에서 게임을 찾지 못했습니다. 게임을 설치한 뒤 새로 고침하세요.";
    public bool HasRoot => Game.Root is not null;
    public bool CanPlay => HasRoot && Game.Info.SteamAppId is not null;
}
