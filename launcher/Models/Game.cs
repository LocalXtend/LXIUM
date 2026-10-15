using System.ComponentModel;

namespace LXIUM.Models;

/// <summary>애드온 한 버전. 받아 둔 zip(Local)이 있으면 그것을, 없으면 애드온 모음 항목(Remote)을 씁니다.</summary>
public sealed record AddonVersion(Manifest Manifest, Addon? Local, CatalogEntry? Remote)
{
    public string Version => Manifest.Version;
    public string Label => Local is not null ? $"v{Version}" : $"v{Version} · 받기 ({Remote!.Source})";
}

/// <summary>id가 같은 애드온의 모든 버전 (최신순)</summary>
public sealed class AddonPackage(List<AddonVersion> versions)
{
    public List<AddonVersion> Versions { get; } = versions;
    public AddonVersion Latest => Versions[0];
    public string Id => Latest.Manifest.Id;
    /// <summary>받아 둔 zip 중 하나 (기록 없는 설치 감지에 씀)</summary>
    public Addon? AnyLocal => Versions.Select(v => v.Local).FirstOrDefault(a => a is not null);
}

/// <summary>애드온이 있는 게임 하나. game.id로 애드온을 묶습니다.</summary>
public sealed class Game(GameRef info, List<AddonPackage> packages, string? root)
{
    public GameRef Info { get; } = info;
    public List<AddonPackage> Packages { get; } = packages;
    /// <summary>게임 루트. Steam 라이브러리에서 찾지 못하면 null</summary>
    public string? Root { get; } = root;
    public string Name => Info.Name;
    public string Status { get; set; } = "";

    public string? CapsuleUrl => Info.SteamAppId is int id ? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/library_600x900.jpg" : null;

    /// <summary>
    /// 받아 둔 애드온과 모음 항목을 (id, version)으로 합칩니다(받아 둔 쪽 우선).
    /// 라이브러리에는 받아 둔 애드온이 있거나 Steam에 설치된 게임만 넣습니다.
    /// </summary>
    public static List<Game> Build(IEnumerable<Addon> local, IEnumerable<CatalogEntry> remote, IReadOnlyList<string> libraries)
    {
        var versions = local.Select(a => new AddonVersion(a.Manifest, a, null))
            .Concat(remote.Select(e => new AddonVersion(e.Manifest, null, e)))
            .GroupBy(v => (v.Manifest.Id, v.Manifest.Version))
            .Select(g => g.OrderBy(v => v.Local is null).First());
        var games = new List<Game>();
        foreach (var byGame in versions.GroupBy(v => v.Manifest.Game.Id))
        {
            var packages = byGame.GroupBy(v => v.Manifest.Id)
                .Select(g => new AddonPackage(g.OrderByDescending(v => v.Version, Comparer<string>.Create(Versions.Compare)).ToList()))
                .OrderBy(p => p.Latest.Manifest.Name).ToList();
            var info = packages.Select(p => p.Latest.Manifest).OrderByDescending(m => m.Game.SteamAppId is not null).First().Game;
            var root = Steam.FindGame(libraries, info);
            if (root is not null || packages.Any(p => p.AnyLocal is not null)) games.Add(new Game(info, packages, root));
        }
        return games.OrderBy(g => g.Name).ToList();
    }
}

/// <summary>상세 화면의 애드온 한 줄. 고른 버전(Selected)이 바뀌면 표시를 모두 다시 계산합니다.</summary>
public sealed class AddonRow : INotifyPropertyChanged
{
    readonly List<LoaderInfo> loaders;
    AddonVersion selected;

    /// <param name="untracked">설치 기록 없이 게임 폴더에 이 애드온 파일이 있음 (수동 복사 등)</param>
    public AddonRow(AddonPackage package, InstallRecord? installed, bool untracked, List<LoaderInfo> loaders)
    {
        Package = package;
        Installed = installed;
        Untracked = untracked;
        this.loaders = loaders;
        selected = package.Latest;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AddonPackage Package { get; }
    public InstallRecord? Installed { get; }
    public bool Untracked { get; }
    public List<AddonVersion> Versions => Package.Versions;
    public bool HasManyVersions => Versions.Count > 1;

    public AddonVersion Selected
    {
        get => selected;
        set { selected = value ?? Package.Latest; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
    }

    Manifest M => Selected.Manifest;
    public string Title => M.Name;
    public string Detail => $"v{M.Version} · " + string.Join(", ", M.Variants.Keys.Select(k => Loaders.Names.GetValueOrDefault(k, k)));
    public string? Description => M.Description;
    public bool IsInstalled => Installed is not null || Untracked;

    public string Status
    {
        get
        {
            if (Installed is null) return Untracked ? "기록 없이 설치됨" : "미설치";
            var s = $"v{Installed.Version} 설치됨 · {Loaders.Names.GetValueOrDefault(Installed.Variant, Installed.Variant)}";
            return Models.Versions.Compare(Package.Latest.Version, Installed.Version) > 0 ? $"{s} · v{Package.Latest.Version} 있음" : s;
        }
    }

    public string InstallLabel
    {
        get
        {
            var get = Selected.Local is null ? "받아서 " : "";
            if (Installed is null) return Untracked ? get + "다시 설치" : get + "설치";
            var c = Models.Versions.Compare(Selected.Version, Installed.Version);
            return c > 0 ? get + "업데이트" : c < 0 ? get + "이 버전으로" : get + "재설치";
        }
    }

    /// <summary>고른 버전이 지금 게임의 로더와 맞지 않으면 그 이유. 맞으면 null</summary>
    public string? Compat
    {
        get
        {
            var hasNone = M.Variants.ContainsKey("none");
            var names = string.Join(", ", M.Variants.Keys.Where(k => k != "none").Select(k => Loaders.Names.GetValueOrDefault(k, k)));
            if (loaders.Count == 0) return hasNone ? null : $"로더 필요: {names} (설치할 때 함께 설치)";
            var match = loaders.FirstOrDefault(l => M.Variants.ContainsKey(l.Id));
            if (match is null) return hasNone ? null : $"지금 로더({string.Join(", ", loaders.Select(l => l.Name))})와 맞지 않음. 필요: {names}";
            var min = M.Variants[match.Id].MinLoaderVersion;
            return min is not null && match.Version is not null && Models.Versions.Compare(match.Version, min) < 0
                ? $"{match.Name} {min} 이상 권장 (지금 {match.Version})" : null;
        }
    }

    public bool HasCompat => Compat is not null;
}

public sealed record LoaderChoice(string Id, string Name)
{
    public string Label => $"{Name} 설치";
}

public sealed record GameDetail(Game Game, List<LoaderInfo> Loaders, List<AddonRow> Addons)
{
    /// <summary>로더가 없을 때 설치를 제안할 로더: 애드온 변형 중 런처가 설치할 수 있는 것</summary>
    public List<LoaderChoice> InstallableLoaders => Loaders.Count > 0 || Game.Root is null ? []
        : Addons.SelectMany(a => a.Selected.Manifest.Variants.Keys).Distinct().Where(LoaderSetup.Supports)
            .Select(id => new LoaderChoice(id, Models.Loaders.Names[id])).ToList();

    public string RootText => Game.Root ?? "Steam 라이브러리에서 게임을 찾지 못했습니다. 게임을 설치한 뒤 새로 고침하세요.";
    public bool HasRoot => Game.Root is not null;
    public bool CanPlay => HasRoot && Game.Info.SteamAppId is not null;
}
