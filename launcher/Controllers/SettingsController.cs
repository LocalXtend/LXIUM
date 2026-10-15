using System.Diagnostics;
using LXIUM.Models;
using LXIUM.Views;

namespace LXIUM.Controllers;

/// <summary>런처 설정 화면: 업데이트 확인·적용, 자동 확인, 애드온 모음 목록</summary>
public sealed class SettingsController(MainWindow view, AppSettings settings, LibraryController library)
{
    static string AppDir => LibraryController.AppDir;
    LauncherRelease? pending;
    string status = "";

    /// <summary>시작할 때: 지난 업데이트 정리, 게임 목록, 자동 확인이 켜져 있으면 업데이트 확인</summary>
    public async Task StartAsync()
    {
        if (Environment.ProcessPath is string exe) Updater.CleanupOld(exe);
        var updates = await library.LoadAsync(fetchCatalogs: settings.AutoUpdate);
        if (!settings.AutoUpdate) return;
        await CheckLauncherAsync(quiet: true);
        status = Summary(updates);
    }

    public void Open() => view.ShowSettings(settings, Updater.Current, status, library.CatalogErrors);

    public void SetAutoUpdate(bool on)
    {
        if (settings.AutoUpdate == on) return;
        settings.AutoUpdate = on;
        Save();
    }

    /// <summary>"지금 업데이트 확인": 런처 릴리스와 애드온 모음을 모두 확인합니다.</summary>
    public async Task CheckNowAsync()
    {
        view.SetSettingsStatus("확인하는 중…");
        var updates = await library.LoadAsync(fetchCatalogs: true);
        var launcher = await CheckLauncherAsync(quiet: false);
        status = launcher + "\n" + Summary(updates);
        Open();
    }

    static string Summary(int addonUpdates) => addonUpdates > 0 ? $"설치한 애드온 중 {addonUpdates}개에 새 버전이 있습니다." : "설치한 애드온은 모두 최신입니다.";

    /// <summary>새 런처가 있으면 머리글에 업데이트 버튼을 띄웁니다. 결과 문장을 돌려줍니다.</summary>
    async Task<string> CheckLauncherAsync(bool quiet)
    {
        try
        {
            pending = await Updater.CheckAsync();
            view.ShowLauncherUpdate(pending?.Version);
            return pending is null ? $"LXIUM {Updater.Current}은(는) 최신입니다." : $"LXIUM {pending.Version}이(가) 나왔습니다. 머리글의 업데이트 버튼을 누르세요.";
        }
        catch (Exception e)
        {
            return quiet ? "" : $"런처 업데이트를 확인하지 못했습니다: {e.Message}";
        }
    }

    /// <summary>새 런처를 받아 바꾼 뒤 다시 실행합니다. settings.json과 addons/의 기존 파일은 남습니다.</summary>
    public async Task ApplyUpdateAsync()
    {
        if (pending is null || Environment.ProcessPath is not string exe) return;
        if (!view.Confirm($"LXIUM {pending.Version}으로 업데이트하고 다시 시작할까요?\n런처 설정과 받아 둔 애드온은 그대로 남습니다.")) return;
        view.SetBusy($"LXIUM {pending.Version} 받는 중…");
        try
        {
            await Updater.ApplyAsync(pending, AppDir, exe);
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            view.Close();
        }
        catch (Exception e)
        {
            view.SetBusy(null);
            view.Alert($"업데이트 실패: {e.Message}");
        }
    }

    public async Task AddCatalogAsync(string url)
    {
        url = url.Trim();
        if (!Catalogs.IsHttps(url)) { view.Alert("https:// 로 시작하는 애드온 모음 주소를 넣으세요."); return; }
        if (settings.Catalogs.Contains(url)) return;
        if (!view.Confirm("애드온은 게임 안에서 실행되는 프로그램입니다. 믿을 수 있는 사람이 만든 모음만 추가하세요.\n추가할까요?")) return;
        settings.Catalogs.Add(url);
        Save();
        await library.LoadAsync(fetchCatalogs: true);
        Open();
    }

    public async Task RemoveCatalogAsync(string url)
    {
        settings.Catalogs.Remove(url);
        Save();
        await library.LoadAsync(fetchCatalogs: true);
        Open();
    }

    public void OpenLink(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    void Save()
    {
        try { settings.Save(AppDir); }
        catch (Exception e) { view.Alert($"설정을 저장하지 못했습니다: {e.Message}"); }
    }
}
