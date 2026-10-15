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
/// <param name="Untracked">설치 기록 없이 게임 폴더에 이 애드온 파일이 있음 (수동 복사 등)</param>
public sealed record AddonRow(Addon Addon, InstallRecord? Installed, bool Untracked)
{
    Manifest M => Addon.Manifest;
    public string Title => M.Name;
    public string Detail => $"v{M.Version} · " + string.Join(", ", M.Variants.Keys.Select(k => Loaders.Names.GetValueOrDefault(k, k)));
    public string? Description => M.Description;
    public bool IsInstalled => Installed is not null || Untracked;
    public string Status => Installed is not null
        ? Installed.Version == M.Version ? $"설치됨 · {Loaders.Names.GetValueOrDefault(Installed.Variant, Installed.Variant)}"
            : $"v{Installed.Version} 설치됨 · 업데이트 가능"
        : Untracked ? "기록 없이 설치됨" : "미설치";
    public string InstallLabel => Installed is null && !Untracked ? "설치" : Installed?.Version is string v && v != M.Version ? "업데이트" : "재설치";
}

public sealed record LoaderChoice(string Id, string Name)
{
    public string Label => $"{Name} 설치";
}

public sealed record GameDetail(Game Game, List<LoaderInfo> Loaders, List<AddonRow> Addons)
{
    /// <summary>로더가 없을 때 설치를 제안할 로더: 애드온 변형 중 런처가 설치할 수 있는 것</summary>
    public List<LoaderChoice> InstallableLoaders => Loaders.Count > 0 || Game.Root is null ? []
        : Addons.SelectMany(a => a.Addon.Manifest.Variants.Keys).Distinct().Where(LoaderSetup.Supports)
            .Select(id => new LoaderChoice(id, Models.Loaders.Names[id])).ToList();

    public string RootText => Game.Root ?? "Steam 라이브러리에서 게임을 찾지 못했습니다. 게임을 설치한 뒤 새로 고침하세요.";
    public bool HasRoot => Game.Root is not null;
    public bool CanPlay => HasRoot && Game.Info.SteamAppId is not null;
}
