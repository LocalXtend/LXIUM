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
                : g.Addons.Any(a => Installer.Read(g.Root, a.Manifest.Id) is not null) ? "모드 설치됨" : "설치 가능";
        view.ShowLibrary(games);
    }

    public async Task OpenAsync(Game game)
    {
        current = game;
        Refresh();
        try
        {
            var info = await Igdb.FindAsync(game.Info, Settings.Load(AppDir));
            if (current == game) view.ShowInfo(info, info is null ? "IGDB에서 이 게임을 찾지 못했습니다." : null);
        }
        catch (Exception e)
        {
            if (current == game) view.ShowInfo(null, e.Message);
        }
    }

    void Refresh()
    {
        var g = current!;
        view.ShowDetail(new GameDetail(g, g.Root is null ? [] : Loaders.Detect(g.Root),
            g.Addons.Select(a => new AddonRow(a, g.Root is null ? null : Installer.Read(g.Root, a.Manifest.Id))).ToList()));
    }

    public void Install(AddonRow row)
    {
        if (current?.Root is not string root) { view.Alert("게임 폴더를 찾지 못했습니다."); return; }
        var loader = view.SelectedLoader;
        var (variant, error) = Installer.PickVariant(row.Addon.Manifest, loader?.Id);
        if (variant is null) { view.Alert(error!); return; }

        var min = row.Addon.Manifest.Variants[variant].MinLoaderVersion;
        if (loader?.Id == variant && min is not null && loader.Version is not null && Loaders.Compare(loader.Version, min) < 0
            && !view.Confirm($"{loader.Name} {loader.Version}은(는) 권장 최소 버전 {min}보다 낮습니다. 그래도 설치할까요?"))
            return;

        try { Installer.Install(row.Addon, variant, root); }
        catch (Exception e) { view.Alert($"설치 실패: {e.Message}\n게임이 실행 중이면 끄고 다시 시도하세요."); }
        Refresh();
    }

    public void Uninstall(AddonRow row)
    {
        if (current?.Root is not string root) return;
        try { Installer.Uninstall(root, row.Addon.Manifest.Id); }
        catch (Exception e) { view.Alert($"제거 실패: {e.Message}\n게임이 실행 중이면 끄고 다시 시도하세요."); }
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
