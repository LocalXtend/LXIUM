using System.ComponentModel;

namespace LXIUM.Models;

/// <summary>애드온 한 버전. 받아 둔 zip(Local)이 있으면 그것을, 없으면 애드온 모음 항목(Remote)을 씁니다.</summary>
public sealed record AddonVersion(Manifest Manifest, Addon? Local, CatalogEntry? Remote)
{
    public string Version => Manifest.Version;
    public string Label => Local is not null ? $"v{Version}" : Loc.T("mod.versionRemote", Version, Remote!.Source);
}

/// <summary>id가 같은 애드온의 모든 버전 (최신순)</summary>
public sealed class AddonPackage(List<AddonVersion> versions)
{
    public List<AddonVersion> Versions { get; } = versions;
    public AddonVersion Latest => Versions[0];
    public string Id => Latest.Manifest.Id;
    /// <summary>받아 둔 zip 중 가장 새 것 (기록 없는 설치 감지·아이콘·.lxconfig에 씀)</summary>
    public Addon? AnyLocal => Versions.Select(v => v.Local).FirstOrDefault(a => a is not null);
    /// <summary>사용자가 모음에서 "추가"했거나 addons/에 넣은 애드온</summary>
    public bool IsAdded => AnyLocal is not null;
}

/// <summary>애드온이 있는 게임 하나. Steam 앱 ID(없으면 game.id)로 애드온을 묶습니다.</summary>
public sealed class Game(GameRef info, List<AddonPackage> packages, string? root)
{
    public GameRef Info { get; } = info;
    public List<AddonPackage> Packages { get; } = packages;
    /// <summary>게임 루트. Steam 라이브러리에서 찾지 못하면 null</summary>
    public string? Root { get; } = root;
    public string Name => Info.Name;
    public string Status { get; set; } = "";
    /// <summary>게임을 묶는 키. 애드온 이름을 바꾸며 game.id가 달라져도 같은 Steam 게임이면 한 곳에 모임</summary>
    public string Key => Info.SteamAppId?.ToString() ?? Info.Id;

    public string? CapsuleUrl => Info.SteamAppId is int id ? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/library_600x900.jpg" : null;

    /// <summary>
    /// 받아 둔 애드온과 모음 항목을 (id, version)으로 합칩니다(받아 둔 쪽 우선).
    /// 라이브러리에는 받아 둔 애드온이 있거나 Steam에 설치된 게임만 넣습니다.
    /// 다른 애드온이 replaces로 대신하는 애드온은 숨깁니다(설치돼 있으면 제거할 수 있게 남김).
    /// </summary>
    public static List<Game> Build(IEnumerable<Addon> local, IEnumerable<CatalogEntry> remote, IReadOnlyList<string> libraries)
    {
        var versions = local.Select(a => new AddonVersion(a.Manifest, a, null))
            .Concat(remote.Select(e => new AddonVersion(e.Manifest, null, e)))
            .GroupBy(v => (v.Manifest.Id, v.Manifest.Version))
            .Select(g => g.OrderBy(v => v.Local is null).First());
        var games = new List<Game>();
        foreach (var byGame in versions.GroupBy(v => v.Manifest.Game.SteamAppId?.ToString() ?? v.Manifest.Game.Id))
        {
            var packages = byGame.GroupBy(v => v.Manifest.Id)
                .Select(g => new AddonPackage(g.OrderByDescending(v => v.Version, Comparer<string>.Create(Versions.Compare)).ToList()))
                .OrderBy(p => p.Latest.Manifest.Name).ToList();
            var info = packages.Select(p => p.Latest.Manifest).OrderByDescending(m => m.Game.SteamAppId is not null).First().Game;
            var root = Steam.FindGame(libraries, info);
            var replaced = packages.SelectMany(p => p.Latest.Manifest.Replaces ?? []).ToHashSet();
            packages = packages.Where(p => !replaced.Contains(p.Id) || (root is not null && Installer.Read(root, p.Id) is not null)).ToList();
            if (root is not null || packages.Any(p => p.IsAdded)) games.Add(new Game(info, packages, root));
        }
        return games.OrderBy(g => g.Name).ToList();
    }
}

/// <summary>모드 패널의 애드온 한 줄. 고른 버전(Selected)이 바뀌면 표시를 모두 다시 계산합니다.</summary>
public sealed class AddonRow : INotifyPropertyChanged
{
    readonly List<LoaderInfo> loaders;
    readonly string iconCache;
    AddonVersion selected;

    /// <param name="untracked">설치 기록 없이 게임 폴더에 이 애드온 파일이 있음 (수동 복사 등)</param>
    public AddonRow(AddonPackage package, InstallRecord? installed, bool untracked, List<LoaderInfo> loaders, string iconCache)
    {
        Package = package;
        Installed = installed;
        Untracked = untracked;
        this.loaders = loaders;
        this.iconCache = iconCache;
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
    public string? Description => M.Description;
    public string Version => $"v{M.Version}";
    public string[] LoaderNames => M.Variants.Keys.Select(Loaders.Name).ToArray();
    public string[] TagNames => (M.Tags ?? []).Select(t => Loc.T($"tag.{t}")).ToArray();
    /// <summary>추가한 모드: zip을 받아 두었거나 이미 설치돼 있음 (설치된 모드는 zip이 없어도 "추가한 모드"에 둠)</summary>
    public bool IsAdded => Package.IsAdded || IsInstalled;
    public bool IsInstalled => Installed is not null || Untracked;
    public bool IsAvailable => !IsAdded;

    /// <summary>아이콘: 받아 둔 zip의 addon.png, 없으면 모음의 아이콘 주소</summary>
    public string? Icon => (Selected.Local ?? Package.AnyLocal)?.ExtractIcon(iconCache) ?? Package.Versions.Select(v => v.Remote?.IconUrl).FirstOrDefault(u => u is not null);

    /// <summary>설치된 버전보다 새 버전이 있으면 그 버전</summary>
    public string? NewerVersion => Installed is not null && Models.Versions.Compare(Package.Latest.Version, Installed.Version) > 0 ? Package.Latest.Version : null;

    public string Status
    {
        get
        {
            if (Installed is null) return Untracked ? Loc.T("mod.untracked") : IsAdded ? Loc.T("mod.notInstalled") : Loc.T("mod.notAdded");
            var s = Loc.T("mod.installed", Installed.Version, Loaders.Name(Installed.Variant));
            return NewerVersion is string v ? s + " · " + Loc.T("mod.newer", v) : s;
        }
    }

    /// <summary>주 버튼 글. 추가 안 됨 = 추가(받기), 그 밖 = 설치·업데이트·이 버전으로·재설치</summary>
    public string InstallLabel
    {
        get
        {
            if (!IsAdded) return Loc.T("mod.add");
            if (Installed is null) return Untracked ? Loc.T("mod.reinstall") : Loc.T("mod.install");
            var c = Models.Versions.Compare(Selected.Version, Installed.Version);
            return c > 0 ? Loc.T("mod.update") : c < 0 ? Loc.T("mod.downgrade") : Loc.T("mod.reinstall");
        }
    }

    /// <summary>고른 버전이 지금 게임의 로더와 맞지 않으면 그 이유. 맞으면 null</summary>
    public string? Compat
    {
        get
        {
            var hasNone = M.Variants.ContainsKey("none");
            var names = string.Join(", ", M.Variants.Keys.Where(k => k != "none").Select(Loaders.Name));
            if (loaders.Count == 0) return hasNone ? null : Loc.T("compat.needLoader", names);
            var match = loaders.FirstOrDefault(l => M.Variants.ContainsKey(l.Id));
            if (match is null) return hasNone ? null : Loc.T("compat.mismatch", string.Join(", ", loaders.Select(l => l.Name)), names);
            var min = M.Variants[match.Id].MinLoaderVersion;
            return min is not null && match.Version is not null && Models.Versions.Compare(match.Version, min) < 0
                ? Loc.T("compat.oldLoader", match.Name, min, match.Version) : null;
        }
    }

    public bool HasCompat => Compat is not null;

    /// <summary>검색어(이름·설명·id)와 태그(하나라도 맞으면) 거르기</summary>
    public bool Matches(string search, IReadOnlyCollection<string> tags)
    {
        var m = Package.Latest.Manifest;
        var text = search.Trim();
        var textOk = text == "" || m.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                     || m.Id.Contains(text, StringComparison.OrdinalIgnoreCase)
                     || (m.Description?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
        return textOk && (tags.Count == 0 || (m.Tags ?? []).Any(tags.Contains));
    }
}

public sealed record LoaderChoice(string Id, string Name)
{
    public string Label => Loc.T("loader.install", Name);
}

public sealed record GameDetail(Game Game, List<LoaderInfo> Loaders, List<AddonRow> Addons)
{
    /// <summary>로더가 없을 때 설치를 제안할 로더: 애드온 변형 중 런처가 설치할 수 있는 것</summary>
    public List<LoaderChoice> InstallableLoaders => Loaders.Count > 0 || Game.Root is null ? []
        : Addons.SelectMany(a => a.Selected.Manifest.Variants.Keys).Distinct().Where(LoaderSetup.Supports)
            .Select(id => new LoaderChoice(id, Models.Loaders.Name(id))).ToList();

    /// <summary>함께 깔려 부딪치는 로더가 있으면 경고 문구</summary>
    public string? LoaderConflict => Models.Loaders.Conflict(Loaders);
    public bool HasLoaderConflict => LoaderConflict is not null;

    public string RootText => Game.Root ?? Loc.T("game.notFound");
    public bool HasRoot => Game.Root is not null;
    public bool CanPlay => HasRoot && Game.Info.SteamAppId is not null;
    public int InstalledCount => Addons.Count(a => a.IsInstalled);
    public string ModSummary => Loc.T("game.modSummary", InstalledCount, Addons.Count);
}
