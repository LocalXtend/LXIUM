using System.Diagnostics;
using LXIUM.Models;
using LXIUM.Views;

namespace LXIUM.Controllers;

/// <summary>런처 설정 화면과 시작 처리: 언어, 업데이트 확인·적용, 자동 확인, 애드온 모음 목록</summary>
public sealed class SettingsController(MainWindow view, AppSettings settings, LibraryController library)
{
    static string AppDir => LibraryController.AppDir;
    LauncherRelease? pending;
    string status = "";

    /// <summary>시작: 언어가 정해지지 않았으면 언어 선택 화면부터, 아니면 바로 시작 처리</summary>
    public async Task StartAsync()
    {
        if (settings.Language is null || !Loc.Available(AppDir).Any(l => l.Code == settings.Language))
        {
            view.ShowLanguage(Loc.Available(AppDir));
            return;
        }
        await StartCoreAsync();
    }

    /// <summary>지난 업데이트 정리, 게임 목록, 자동 확인이 켜져 있으면 업데이트 확인</summary>
    async Task StartCoreAsync()
    {
        if (Environment.ProcessPath is string exe) Updater.CleanupOld(exe);
        var updates = await library.LoadAsync(fetchCatalogs: settings.AutoUpdate);
        if (!settings.AutoUpdate) return;
        await CheckLauncherAsync(quiet: true);
        status = Summary(updates);
    }

    /// <summary>언어 선택 화면에서 고른 언어를 바로 보여 줌 (아직 저장 안 함)</summary>
    public void PreviewLanguage(string code) => Loc.I.Load(AppDir, code);

    /// <summary>언어 선택 화면의 "다음": 저장하고 메인으로</summary>
    public async Task ChooseLanguageAsync(string code)
    {
        Loc.I.Load(AppDir, code);
        settings.Language = code;
        Save();
        await StartCoreAsync();
    }

    /// <summary>설정 화면에서 언어 바꾸기: 바로 적용</summary>
    public void SetLanguage(string code)
    {
        if (code == Loc.I.Code) return;
        Loc.I.Load(AppDir, code);
        settings.Language = code;
        Save();
        library.Relocalize();
        Open();
    }

    public void Open() => view.ShowSettings(settings, Updater.Current, status, library.CatalogErrors, Loc.Available(AppDir));

    public void SetAutoUpdate(bool on)
    {
        if (settings.AutoUpdate == on) return;
        settings.AutoUpdate = on;
        Save();
    }

    /// <summary>"지금 업데이트 확인": 런처 릴리스와 애드온 모음을 모두 확인합니다.</summary>
    public async Task CheckNowAsync()
    {
        view.SetSettingsStatus(Loc.T("settings.checking"));
        var updates = await library.LoadAsync(fetchCatalogs: true);
        var launcher = await CheckLauncherAsync(quiet: false);
        status = launcher + "\n" + Summary(updates);
        Open();
    }

    static string Summary(int addonUpdates) => addonUpdates > 0 ? Loc.T("settings.addonUpdates", addonUpdates) : Loc.T("settings.addonLatest");

    /// <summary>새 런처가 있으면 머리글에 업데이트 버튼을 띄웁니다. 결과 문장을 돌려줍니다.</summary>
    async Task<string> CheckLauncherAsync(bool quiet)
    {
        try
        {
            pending = await Updater.CheckAsync();
            view.ShowLauncherUpdate(pending?.Version);
            return pending is null ? Loc.T("settings.launcherLatest", Updater.Current) : Loc.T("settings.launcherNew", pending.Version);
        }
        catch (Exception e)
        {
            return quiet ? "" : Loc.T("settings.launcherFailed", e.Message);
        }
    }

    /// <summary>새 런처를 받아 바꾼 뒤 다시 실행합니다. settings.json과 addons/의 기존 파일은 남습니다.</summary>
    public async Task ApplyUpdateAsync()
    {
        if (pending is null || Environment.ProcessPath is not string exe) return;
        if (!view.Confirm(Loc.T("ask.updateLauncher", pending.Version))) return;
        using (var busy = view.StartBusy(Loc.T("busy.launcher", pending.Version)))
        {
            try { await Updater.ApplyAsync(pending, AppDir, exe, busy); }
            catch (Exception e)
            {
                view.Alert(Loc.T("err.updateFailed", e.Message));
                return;
            }
        }
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        view.Close();
    }

    public async Task AddCatalogAsync(string url)
    {
        url = url.Trim();
        if (!Catalogs.IsHttps(url)) { view.Alert(Loc.T("err.catalogUrl")); return; }
        if (settings.Catalogs.Contains(url)) return;
        if (!view.Confirm(Loc.T("ask.addCatalog"))) return;
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

    /// <summary>애드온 모음을 공식 모음 하나로 되돌림</summary>
    public async Task ResetCatalogsAsync()
    {
        if (!view.Confirm(Loc.T("ask.resetCatalogs"))) return;
        settings.ResetCatalogs();
        Save();
        await library.LoadAsync(fetchCatalogs: true);
        Open();
    }

    public void OpenLink(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    void Save()
    {
        try { settings.Save(AppDir); }
        catch (Exception e) { view.Alert(Loc.T("err.settingsSave", e.Message)); }
    }
}
