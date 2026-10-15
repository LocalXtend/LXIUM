using System.Diagnostics;
using LXIUM.Models;
using LXIUM.Views;

namespace LXIUM.Controllers;

/// <summary>라이브러리·게임 상세 화면의 이벤트를 받아 모델을 다루고, 결과를 뷰에 넘깁니다.</summary>
public sealed class LibraryController(MainWindow view, AppSettings settings)
{
    public static readonly string AppDir = AppContext.BaseDirectory;
    static string AddonsDir => Path.Combine(AppDir, "addons");
    List<Game> games = [];
    List<CatalogEntry> remote = [];
    List<string> libraries = [];
    Game? current;

    /// <summary>마지막으로 애드온 모음을 받을 때 생긴 문제 (설정 화면에 보여 줌)</summary>
    public List<string> CatalogErrors { get; private set; } = [];

    /// <summary>
    /// Steam 라이브러리를 찾고 addons/의 애드온을 읽습니다. fetchCatalogs면 애드온 모음도 새로 받습니다.
    /// 설치된 애드온 중 새 버전이 있는 것의 수를 돌려줍니다.
    /// </summary>
    public async Task<int> LoadAsync(bool fetchCatalogs)
    {
        view.ShowBusy(fetchCatalogs ? "게임과 애드온 모음을 확인하는 중…" : "게임을 찾는 중…");
        if (fetchCatalogs)
            (remote, CatalogErrors) = await Catalogs.FetchAsync(settings.Catalogs);
        libraries = await Task.Run(Steam.FindLibraries);
        await Task.Run(Rebuild);
        Back();
        return games.Where(g => g.Root is not null)
            .Sum(g => g.Packages.Count(p => Installer.Read(g.Root!, p.Id) is { } r && Versions.Compare(p.Latest.Version, r.Version) > 0));
    }

    /// <summary>받아 둔 애드온이 바뀐 뒤(받기 등) 목록을 다시 만들고, 보던 게임을 새 객체로 바꿉니다.</summary>
    void Rebuild()
    {
        games = Game.Build(Addon.LoadAll(AddonsDir), remote, libraries);
        if (current is not null) current = games.FirstOrDefault(g => g.Info.Id == current.Info.Id);
    }

    public void Back()
    {
        current = null;
        foreach (var g in games)
        {
            if (g.Root is not string root) { g.Status = "게임 없음"; continue; }
            var records = g.Packages.Select(p => (p, r: Installer.Read(root, p.Id))).ToList();
            g.Status = records.Any(x => x.r is not null && Versions.Compare(x.p.Latest.Version, x.r.Version) > 0) ? "업데이트 있음"
                : records.Any(x => x.r is not null) || g.Packages.Any(p => p.AnyLocal is Addon a && Installer.FindUntracked(a, root).Count > 0) ? "모드 설치됨"
                : "설치 가능";
        }
        view.ShowLibrary(games);
    }

    public async Task OpenAsync(Game game)
    {
        current = game;
        Refresh();
        if (game.Info.SteamAppId is not int appId)
        {
            view.ShowInfo(null, "lxium.json에 steamAppId가 없어 게임 정보를 가져올 수 없습니다.");
            return;
        }
        try
        {
            var info = await SteamStore.FindAsync(appId);
            if (current?.Info.Id == game.Info.Id) view.ShowInfo(info, info is null ? "Steam 상점에서 이 게임을 찾지 못했습니다." : null);
        }
        catch (Exception e)
        {
            if (current?.Info.Id == game.Info.Id) view.ShowInfo(null, $"Steam 상점 정보를 가져오지 못했습니다: {e.Message}");
        }
    }

    void Refresh()
    {
        if (current is not Game g) { Back(); return; }
        var loaders = g.Root is null ? [] : Loaders.Detect(g.Root);
        view.ShowDetail(new GameDetail(g, loaders, g.Packages.Select(p => g.Root is null ? new AddonRow(p, null, false, loaders)
            : new AddonRow(p, Installer.Read(g.Root, p.Id), p.AnyLocal is Addon a && Installer.FindUntracked(a, g.Root).Count > 0, loaders)).ToList()));
    }

    /// <summary>
    /// addon.md 6장 설치 순서. 고른 버전을 설치하고, 받아 두지 않은 버전이면 애드온 모음에서 먼저 받습니다.
    /// 로더가 없고 애드온이 로더를 요구하면 사용자 확인 후 로더부터 설치합니다.
    /// </summary>
    public async Task InstallAsync(AddonRow row)
    {
        if (current?.Root is not string root) { view.Alert("게임 폴더를 찾지 못했습니다."); return; }
        var m = row.Selected.Manifest;
        var loader = view.SelectedLoader;
        if (loader is null && !m.Variants.ContainsKey("none"))
        {
            var need = m.Variants.Keys.FirstOrDefault(LoaderSetup.Supports);
            if (need is null) { view.Alert(Installer.PickVariant(m, null).Error!); return; }
            if (!view.Confirm($"이 모드는 {Loaders.Names[need]}가 필요합니다. {Loaders.Names[need]}를 설치한 뒤 모드를 설치할까요?")) return;
            if (!await InstallLoaderCoreAsync(need, root)) return;
            loader = Loaders.Detect(root).FirstOrDefault(l => l.Id == need);
        }

        var (variant, error) = Installer.PickVariant(m, loader?.Id);
        if (variant is null) { view.Alert(error!); Refresh(); return; }

        var min = m.Variants[variant].MinLoaderVersion;
        if (loader?.Id == variant && min is not null && loader.Version is not null && Versions.Compare(loader.Version, min) < 0
            && !view.Confirm($"{loader.Name} {loader.Version}은(는) 권장 최소 버전 {min}보다 낮습니다. 그래도 설치할까요?"))
        {
            Refresh();
            return;
        }

        var addon = row.Selected.Local;
        if (addon is null)
        {
            view.SetBusy($"{m.Name} v{m.Version} 받는 중…");
            try { addon = await Catalogs.DownloadAsync(row.Selected.Remote!, AddonsDir); }
            catch (Exception e) { view.Alert($"받기 실패: {e.Message}"); }
            finally { view.SetBusy(null); }
            if (addon is null) return;
        }

        try { Installer.Install(addon, variant, root); }
        catch (Exception e) { view.Alert($"설치 실패: {e.Message}\n게임이 실행 중이면 끄고 다시 시도하세요."); }
        Rebuild();
        Refresh();
    }

    public void Uninstall(AddonRow row)
    {
        if (current?.Root is not string root) return;
        var local = row.Package.AnyLocal;
        try { Installer.Uninstall(root, row.Package.Id, local is null ? null : () => Installer.FindUntracked(local, root)); }
        catch (Exception e) { view.Alert($"제거 실패: {e.Message}\n게임이 실행 중이면 끄고 다시 시도하세요."); }
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
        view.SetBusy($"{Loaders.Names[loader]} 받는 중…");
        try
        {
            var version = await LoaderSetup.InstallAsync(loader, root, current!.Info.Exe);
            view.SetBusy(null);
            view.Alert($"{Loaders.Names[loader]} {version}을(를) 설치했습니다.\n게임을 한 번 실행하면 로더가 처음 준비를 마칩니다.");
            return true;
        }
        catch (Exception e)
        {
            view.SetBusy(null);
            view.Alert($"{Loaders.Names[loader]} 설치 실패: {e.Message}");
            return false;
        }
    }

    /// <summary>이 로더로 설치한 LXIUM 애드온을 먼저 제거(백업 복원)한 뒤 로더 파일을 지웁니다.</summary>
    public void UninstallLoader(LoaderInfo loader)
    {
        if (current?.Root is not string root) return;
        if (!LoaderSetup.Supports(loader.Id)) { view.Alert($"{loader.Name}는 런처가 제거할 수 없습니다."); return; }
        var items = string.Join(", ", LoaderSetup.Footprint[loader.Id]);
        if (!view.Confirm($"{loader.Label}를 제거합니다.\n지울 항목: {items}\n\n폴더 안의 다른 모드와 설정도 함께 지워집니다. 계속할까요?"))
            return;
        try
        {
            foreach (var p in current.Packages)
                if (Installer.Read(root, p.Id)?.Variant == loader.Id)
                    Installer.Uninstall(root, p.Id);
            LoaderSetup.Uninstall(loader.Id, root);
        }
        catch (Exception e) { view.Alert($"로더 제거 실패: {e.Message}\n게임이 실행 중이면 끄고 다시 시도하세요."); }
        Refresh();
    }

    public void OpenModConfig(AddonRow row)
    {
        if (current?.Root is not string root) return;
        var variant = row.Installed?.Variant ?? view.SelectedLoader?.Id ?? "";
        EditConfig(ConfigFile.ModConfigPath(root, row.Package.Id, variant), $"{row.Title} 설정");
    }

    public void OpenLoaderConfig(LoaderInfo loader)
    {
        if (current?.Root is not string root) return;
        EditConfig(ConfigFile.LoaderConfigPath(root, loader.Id), $"{loader.Name} 설정");
    }

    /// <summary>설정 창을 열고, 저장을 누르면 값을 확인한 뒤 저장합니다. 잘못된 값이 있으면 알리고 창을 다시 엽니다.</summary>
    void EditConfig(string? path, string heading)
    {
        if (path is null || !File.Exists(path))
        {
            view.Alert("설정 파일이 아직 없습니다. 게임을 한 번 실행하면 만들어집니다.");
            return;
        }
        ConfigFile config;
        try { config = ConfigFile.Load(path); }
        catch (Exception e) { view.Alert($"설정 파일을 읽지 못했습니다: {e.Message}"); return; }

        while (view.EditConfig(config, heading))
        {
            if (config.Invalid() is { Count: > 0 } bad)
            {
                view.Alert("형식에 맞지 않는 값이 있습니다.\n" + string.Join("\n", bad));
                continue;
            }
            try { config.Save(); return; }
            catch (Exception e) { view.Alert($"저장 실패: {e.Message}"); }
        }
    }

    public void Play()
    {
        if (current?.Info.SteamAppId is int id)
            Process.Start(new ProcessStartInfo($"steam://rungameid/{id}") { UseShellExecute = true });
    }

    public void OpenFolder()
    {
        if (current?.Root is string root)
            Process.Start(new ProcessStartInfo(root) { UseShellExecute = true });
    }
}
