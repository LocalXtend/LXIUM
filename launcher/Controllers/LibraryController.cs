using System.Diagnostics;
using LXIUM.Models;
using LXIUM.Views;

namespace LXIUM.Controllers;

/// <summary>라이브러리·게임 설정·모드 패널의 이벤트를 받아 모델을 다루고, 결과를 뷰에 넘깁니다.</summary>
public sealed class LibraryController(MainWindow view, AppSettings settings)
{
    public static readonly string AppDir = AppContext.BaseDirectory;
    static string AddonsDir => Path.Combine(AppDir, "addons");
    public static string CacheDir => Path.Combine(AppDir, "cache");
    static string IconCache => Path.Combine(CacheDir, "icons");

    enum Page { Game, Mods }

    List<Game> games = [];
    List<CatalogEntry> remote = [];
    List<string> libraries = [];
    Game? current;
    Page page;

    /// <summary>마지막으로 애드온 모음을 받을 때 생긴 문제 (설정 화면에 보여 줌)</summary>
    public List<string> CatalogErrors { get; private set; } = [];

    /// <summary>
    /// Steam 라이브러리를 찾고 addons/의 애드온을 읽습니다. fetchCatalogs면 애드온 모음도 새로 받습니다.
    /// 설치된 애드온 중 새 버전이 있는 것의 수를 돌려줍니다.
    /// </summary>
    public async Task<int> LoadAsync(bool fetchCatalogs)
    {
        view.ShowLibraryMessage(Loc.T(fetchCatalogs ? "lib.loadingCatalogs" : "lib.loading"));
        if (fetchCatalogs)
            (remote, CatalogErrors) = await Catalogs.FetchAsync(settings.Catalogs, CacheDir);
        libraries = await Task.Run(Steam.FindLibraries);
        await Task.Run(Rebuild);
        Back();
        return games.Where(g => g.Root is not null)
            .Sum(g => g.Packages.Count(p => Installer.Read(g.Root!, p.Id) is { } r && Versions.Compare(p.Latest.Version, r.Version) > 0));
    }

    /// <summary>받아 둔 애드온이 바뀐 뒤(추가·업데이트) 목록을 다시 만들고, 보던 게임을 새 객체로 바꿉니다.</summary>
    void Rebuild()
    {
        games = Game.Build(Addon.LoadAll(AddonsDir, CacheDir), remote, libraries);
        if (current is not null) current = games.FirstOrDefault(g => g.Key == current.Key);
    }

    public void Back()
    {
        current = null;
        foreach (var g in games)
        {
            if (g.Root is not string root) { g.Status = Loc.T("lib.noGame"); continue; }
            var records = g.Packages.Select(p => (p, r: Installer.Read(root, p.Id))).ToList();
            g.Status = records.Any(x => x.r is not null && Versions.Compare(x.p.Latest.Version, x.r.Version) > 0) ? Loc.T("lib.updateAvailable")
                : records.Any(x => x.r is not null) || g.Packages.Any(p => p.AnyLocal is Addon a && Installer.FindUntracked(a, root).Count > 0) ? Loc.T("lib.installed")
                : Loc.T("lib.available");
        }
        view.ShowLibrary(games);
    }

    /// <summary>게임 카드 "설정" 칸: 게임 설정 패널</summary>
    public async Task OpenGameAsync(Game game)
    {
        current = game;
        page = Page.Game;
        Refresh(sameGame: false);
        await LoadStoreInfoAsync(game);
    }

    /// <summary>게임 카드 "모드" 칸: 모드 패널</summary>
    public Task OpenModsAsync(Game game)
    {
        current = game;
        page = Page.Mods;
        Refresh(sameGame: false);
        return Task.CompletedTask;
    }

    public async Task ShowGameAsync()
    {
        if (current is not Game g) return;
        page = Page.Game;
        Refresh(sameGame: false);
        await LoadStoreInfoAsync(g);
    }

    public Task ShowModsAsync()
    {
        if (current is null) return Task.CompletedTask;
        page = Page.Mods;
        Refresh(sameGame: false);
        return Task.CompletedTask;
    }

    async Task LoadStoreInfoAsync(Game game)
    {
        if (game.Info.SteamAppId is not int appId)
        {
            view.ShowGameInfo(null, Loc.T("game.noAppId"));
            return;
        }
        try
        {
            var info = await SteamStore.FindAsync(appId);
            if (current?.Key == game.Key) view.ShowGameInfo(info, info is null ? Loc.T("game.notInStore") : null);
        }
        catch (Exception e)
        {
            if (current?.Key == game.Key) view.ShowGameInfo(null, Loc.T("game.storeFailed", e.Message));
        }
    }

    /// <summary>지금 페이지를 다시 그립니다. sameGame이면 스크롤·검색어 등을 그대로 둠</summary>
    void Refresh(bool sameGame = true)
    {
        if (current is not Game g) { Back(); return; }
        var loaders = g.Root is null ? [] : Loaders.Detect(g.Root);
        var detail = new GameDetail(g, loaders, g.Packages.Select(p => g.Root is null ? new AddonRow(p, null, false, loaders, IconCache)
            : new AddonRow(p, Installer.Read(g.Root, p.Id), p.AnyLocal is Addon a && Installer.FindUntracked(a, g.Root).Count > 0, loaders, IconCache)).ToList());
        if (page == Page.Mods) view.ShowMods(detail, sameGame);
        else view.ShowGame(detail, sameGame);
    }

    /// <summary>
    /// 모드 패널의 주 버튼. 추가하지 않은 모드면 고른 버전을 받아 "추가"만 하고,
    /// 추가한 모드면 고른 버전을 설치(필요하면 받기·로더 설치·먼저 필요한 모드 설치)합니다.
    /// </summary>
    public async Task InstallAsync(AddonRow row)
    {
        if (!row.IsAdded)
        {
            await DownloadAsync(row.Selected);
            Rebuild();
            Refresh();
            return;
        }
        await InstallVersionAsync(row.Selected);
        Rebuild();
        Refresh();
    }

    /// <summary>모음 항목을 받아 addons/에 둡니다. 실패하면 null</summary>
    async Task<Addon?> DownloadAsync(AddonVersion v)
    {
        if (v.Local is not null) return v.Local;
        using var busy = view.StartBusy(Loc.T("busy.download", v.Manifest.Name, v.Version));
        try { return await Catalogs.DownloadAsync(v.Remote!, AddonsDir, busy); }
        catch (Exception e) { view.Alert(Loc.T("err.downloadFailed", e.Message)); return null; }
    }

    /// <summary>addon.md 6장 설치 순서 + requires(먼저 필요한 모드). 설치하면 true</summary>
    async Task<bool> InstallVersionAsync(AddonVersion v)
    {
        if (current?.Root is not string root) { view.Alert(Loc.T("game.notFound")); return false; }
        var m = v.Manifest;

        foreach (var req in m.Requires ?? [])
        {
            if (Installer.Read(root, req) is not null) continue;
            var pkg = current.Packages.FirstOrDefault(p => p.Id == req);
            if (pkg is null) { view.Alert(Loc.T("err.requiresMissing", m.Name, req)); return false; }
            if (!view.Confirm(Loc.T("ask.requires", m.Name, pkg.Latest.Manifest.Name))) return false;
            if (!await InstallVersionAsync(pkg.Latest)) return false;
            Rebuild();
        }

        var loader = view.SelectedLoader ?? Loaders.Detect(root).FirstOrDefault(l => m.Variants.ContainsKey(l.Id)) ?? Loaders.Detect(root).FirstOrDefault();
        if (loader is null && !m.Variants.ContainsKey("none"))
        {
            var need = m.Variants.Keys.FirstOrDefault(LoaderSetup.Supports);
            if (need is null) { view.Alert(Installer.PickVariant(m, null).Error!); return false; }
            if (!view.Confirm(Loc.T("ask.installLoader", m.Name, Loaders.Name(need)))) return false;
            if (!await InstallLoaderCoreAsync(need, root)) return false;
            loader = Loaders.Detect(root).FirstOrDefault(l => l.Id == need);
        }

        var (variant, error) = Installer.PickVariant(m, loader?.Id);
        if (variant is null) { view.Alert(error!); return false; }

        var min = m.Variants[variant].MinLoaderVersion;
        if (loader?.Id == variant && min is not null && loader.Version is not null && Versions.Compare(loader.Version, min) < 0
            && !view.Confirm(Loc.T("ask.oldLoader", loader.Name, loader.Version, min)))
            return false;

        var addon = await DownloadAsync(v);
        if (addon is null) return false;

        using var busy = view.StartBusy(Loc.T("busy.install", m.Name, m.Version));
        try
        {
            await Task.Run(() => Installer.Install(addon, variant, root));
            return true;
        }
        catch (Exception e)
        {
            view.Alert(Loc.T("err.installFailed", e.Message));
            return false;
        }
    }

    public async Task UninstallAsync(AddonRow row)
    {
        if (current?.Root is not string root) return;
        var local = row.Package.AnyLocal;
        using (view.StartBusy(Loc.T("busy.uninstall", row.Title)))
        {
            try { await Task.Run(() => Installer.Uninstall(root, row.Package.Id, local is null ? null : () => Installer.FindUntracked(local, root))); }
            catch (Exception e) { view.Alert(Loc.T("err.uninstallFailed", e.Message)); }
        }
        Refresh();
    }

    public async Task InstallLoaderAsync(LoaderChoice choice)
    {
        if (current?.Root is not string root) return;
        await InstallLoaderCoreAsync(choice.Id, root);
        Refresh();
    }

    async Task<bool> InstallLoaderCoreAsync(string loader, string root)
    {
        string version;
        using (var busy = view.StartBusy(Loc.T("busy.loader", Loaders.Name(loader))))
        {
            try { version = await LoaderSetup.InstallAsync(loader, root, current!.Info.Exe, busy); }
            catch (Exception e)
            {
                view.Alert(Loc.T("err.loaderFailed", Loaders.Name(loader), e.Message));
                return false;
            }
        }
        view.Alert(Loc.T("done.loader", Loaders.Name(loader), version));
        return true;
    }

    /// <summary>로더 업데이트: 이 게임에 맞는 최신 판이 지금보다 새것이면 확인 후 덮어 설치(설정·모드는 남음)</summary>
    public async Task UpdateLoaderAsync(LoaderInfo loader)
    {
        if (current?.Root is not string root || !LoaderSetup.Supports(loader.Id)) return;
        string latest;
        using (view.StartBusy(Loc.T("busy.checkLoader", loader.Name)))
        {
            try { latest = await LoaderSetup.LatestAsync(loader.Id, root, current.Info.Exe); }
            catch (Exception e) { view.Alert(Loc.T("err.loaderFailed", loader.Name, e.Message)); return; }
        }
        if (loader.Version is not null && Versions.Compare(latest, loader.Version) <= 0)
        {
            view.Alert(Loc.T("done.loaderLatest", loader.Label));
            return;
        }
        if (!view.Confirm(Loc.T("ask.updateLoader", loader.Name, loader.Version ?? "?", latest))) return;
        await InstallLoaderCoreAsync(loader.Id, root);
        Refresh();
    }

    /// <summary>이 로더로 설치한 LXIUM 애드온을 먼저 제거(백업 복원)한 뒤 로더 파일을 지웁니다.</summary>
    public async Task UninstallLoaderAsync(LoaderInfo loader)
    {
        if (current?.Root is not string root) return;
        if (!LoaderSetup.Supports(loader.Id)) { view.Alert(Loc.T("err.loaderUnsupported", loader.Name)); return; }
        if (!view.Confirm(Loc.T("ask.removeLoader", loader.Label, string.Join(", ", LoaderSetup.Footprint[loader.Id])))) return;
        var packages = current.Packages.Select(p => p.Id).ToList();
        using (view.StartBusy(Loc.T("busy.removeLoader", loader.Name)))
        {
            try
            {
                await Task.Run(() =>
                {
                    foreach (var id in packages)
                        if (Installer.Read(root, id)?.Variant == loader.Id)
                            Installer.Uninstall(root, id);
                    LoaderSetup.Uninstall(loader.Id, root);
                });
            }
            catch (Exception e) { view.Alert(Loc.T("err.uninstallFailed", e.Message)); }
        }
        Refresh();
    }

    /// <summary>모드 설정: .lxconfig가 있으면 게임을 실행하지 않았어도 기본값으로 만들어 편집</summary>
    public void OpenModConfig(AddonRow row)
    {
        if (current?.Root is not string root) return;
        var variant = row.Installed?.Variant ?? view.SelectedLoader?.Id ?? "";
        var zip = row.Package.Versions.FirstOrDefault(v => v.Version == row.Installed?.Version)?.Local ?? row.Package.AnyLocal;
        var lx = zip?.ReadLxConfig() is string text ? LxConfig.Parse(text) : null;
        EditConfig(ConfigFile.ModConfigPath(root, row.Package.Id, variant), lx, Loc.T("config.modTitle", row.Title));
    }

    public void OpenLoaderConfig(LoaderInfo loader)
    {
        if (current?.Root is not string root) return;
        EditConfig(ConfigFile.LoaderConfigPath(root, loader.Id), null, Loc.T("config.modTitle", loader.Name));
    }

    /// <summary>설정 창을 열고, 저장을 누르면 값을 확인한 뒤 저장합니다. 잘못된 값이 있으면 알리고 창을 다시 엽니다.</summary>
    void EditConfig(string? path, LxConfig? lx, string heading)
    {
        if (path is null) return;
        ConfigFile? config;
        try { config = ConfigFile.Open(path, lx, Loc.I.Code); }
        catch (Exception e) { view.Alert(Loc.T("err.configRead", e.Message)); return; }
        if (config is null) { view.Alert(Loc.T("err.configMissing")); return; }

        while (view.EditConfig(config, heading))
        {
            if (config.Invalid() is { Count: > 0 } bad)
            {
                view.Alert(Loc.T("err.configInvalid") + "\n" + string.Join("\n", bad));
                continue;
            }
            try { config.Save(); return; }
            catch (Exception e) { view.Alert(Loc.T("err.configSave", e.Message)); }
        }
    }

    /// <summary>게임 실행 (카드의 "플레이" 칸이면 그 게임, 아니면 보던 게임)</summary>
    public void Play(Game? game)
    {
        if ((game ?? current)?.Info.SteamAppId is int id)
            Process.Start(new ProcessStartInfo($"steam://rungameid/{id}") { UseShellExecute = true });
        else view.Alert(Loc.T("game.noAppId"));
    }

    /// <summary>
    /// 모드 패널 "파일에서 추가": 고른 파일(LXIUM zip, ManosabaLoader zip, .msmod)을 addons/에 복사합니다.
    /// 알아보지 못한 파일은 복사하지 않고 알립니다. 다른 게임의 모드여도 그 게임 쪽에 나타납니다.
    /// </summary>
    public async Task AddFilesAsync(IReadOnlyList<string> files)
    {
        var rejected = new List<string>();
        using (view.StartBusy(Loc.T("busy.addFiles")))
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(AddonsDir);
                foreach (var file in files)
                {
                    if (Addon.TryRead(file, CacheDir) is null) { rejected.Add(Path.GetFileName(file)); continue; }
                    var dest = Path.Combine(AddonsDir, Path.GetFileName(file));
                    if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) File.Copy(file, dest, true);
                }
                Rebuild();
            });
        }
        if (rejected.Count > 0) view.Alert(Loc.T("err.unrecognized") + "\n" + string.Join("\n", rejected));
        Refresh();
    }

    public void OpenFolder()
    {
        if (current?.Root is string root)
            Process.Start(new ProcessStartInfo(root) { UseShellExecute = true });
    }

    /// <summary>언어를 바꾼 뒤 모델이 만든 문구(상태 등)를 다시 그림</summary>
    public void Relocalize()
    {
        if (current is null) Back();
        else Refresh();
    }
}
