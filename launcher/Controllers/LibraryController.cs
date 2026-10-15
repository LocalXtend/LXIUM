using System.Diagnostics;
using LXIUM.Models;
using LXIUM.Views;

namespace LXIUM.Controllers;

/// <summary>화면 이벤트를 받아 모델을 다루고, 결과를 뷰에 넘깁니다.</summary>
public sealed class LibraryController(MainWindow view)
{
    static readonly string AppDir = AppContext.BaseDirectory;
    List<Game> games = [];
    Game? current;

    /// <summary>addons/의 애드온을 읽고 Steam 라이브러리에서 게임을 찾습니다.</summary>
    public async Task LoadAsync()
    {
        view.ShowBusy("게임을 찾는 중…");
        games = await Task.Run(() => Game.Build(Addon.LoadAll(Path.Combine(AppDir, "addons")), Steam.FindLibraries()));
        Back();
    }

    public void Back()
    {
        current = null;
        foreach (var g in games)
            g.Status = g.Root is null ? "게임 없음"
                : g.Addons.Any(a => Installer.Read(g.Root, a.Manifest.Id) is not null || Installer.FindUntracked(a, g.Root).Count > 0)
                    ? "모드 설치됨" : "설치 가능";
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
            if (current == game) view.ShowInfo(info, info is null ? "Steam 상점에서 이 게임을 찾지 못했습니다." : null);
        }
        catch (Exception e)
        {
            if (current == game) view.ShowInfo(null, $"Steam 상점 정보를 가져오지 못했습니다: {e.Message}");
        }
    }

    void Refresh()
    {
        var g = current!;
        view.ShowDetail(new GameDetail(g, g.Root is null ? [] : Loaders.Detect(g.Root),
            g.Addons.Select(a => g.Root is null ? new AddonRow(a, null, false)
                : new AddonRow(a, Installer.Read(g.Root, a.Manifest.Id), Installer.FindUntracked(a, g.Root).Count > 0)).ToList()));
    }

    /// <summary>
    /// addon.md 6장 설치 순서. 로더가 없고 애드온이 로더를 요구하면 사용자 확인 후 로더부터 설치합니다.
    /// </summary>
    public async Task InstallAsync(AddonRow row)
    {
        if (current?.Root is not string root) { view.Alert("게임 폴더를 찾지 못했습니다."); return; }
        var m = row.Addon.Manifest;
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
        if (loader?.Id == variant && min is not null && loader.Version is not null && Loaders.Compare(loader.Version, min) < 0
            && !view.Confirm($"{loader.Name} {loader.Version}은(는) 권장 최소 버전 {min}보다 낮습니다. 그래도 설치할까요?"))
        {
            Refresh();
            return;
        }

        try { Installer.Install(row.Addon, variant, root); }
        catch (Exception e) { view.Alert($"설치 실패: {e.Message}\n게임이 실행 중이면 끄고 다시 시도하세요."); }
        Refresh();
    }

    public void Uninstall(AddonRow row)
    {
        if (current?.Root is not string root) return;
        try { Installer.Uninstall(row.Addon, root); }
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
            foreach (var a in current.Addons)
                if (Installer.Read(root, a.Manifest.Id)?.Variant == loader.Id)
                    Installer.Uninstall(a, root);
            LoaderSetup.Uninstall(loader.Id, root);
        }
        catch (Exception e) { view.Alert($"로더 제거 실패: {e.Message}\n게임이 실행 중이면 끄고 다시 시도하세요."); }
        Refresh();
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
