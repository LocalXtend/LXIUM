using System.Windows;
using System.Windows.Input;
using LXIUM.Controllers;
using LXIUM.Models;

namespace LXIUM.Views;

/// <summary>오래 걸리는 작업 동안 덮개를 띄웁니다. Report(0~1)로 진행률, Dispose로 닫음</summary>
public sealed class BusyScope(BusyOverlay overlay) : IProgress<double>, IDisposable
{
    public void Report(double value) => overlay.Dispatcher.Invoke(() => overlay.Report(value));
    public void Dispose() => overlay.Hide();
}

/// <summary>
/// 화면 표시만 담당. 페이지(라이브러리·게임 설정·모드·런처 설정)와 덮개(진행·언어)를 바꿔 보여 주고,
/// 사용자 동작은 LibraryController(게임·모드)와 SettingsController(런처)로 넘깁니다.
/// </summary>
public partial class MainWindow : Window
{
    public LibraryController Controller { get; }
    public SettingsController SettingsController { get; }

    public MainWindow()
    {
        var settings = AppSettings.Load(LibraryController.AppDir);
        Loc.I.Load(LibraryController.AppDir, settings.Language ?? "en");
        InitializeComponent();
        Controller = new LibraryController(this, settings);
        SettingsController = new SettingsController(this, settings, Controller);
        Loaded += async (_, _) => await SettingsController.StartAsync();
    }

    public LoaderInfo? SelectedLoader => GameView.SelectedLoader;

    /// <summary>페이지 하나만 보이게 하고(0.3초 나타남) 머리글 버튼을 맞춥니다.</summary>
    void ShowPage(FrameworkElement page, string subtitle)
    {
        Library.CloseOverlay();
        foreach (FrameworkElement p in Pages.Children)
            if (p != page) p.Visibility = Visibility.Collapsed;
        if (page.Visibility != Visibility.Visible) Motion.FadeIn(page);
        BackButton.Visibility = page == Library ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.Visibility = page == Library ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = page == Settings ? Visibility.Collapsed : Visibility.Visible;
        Subtitle.Text = subtitle;
    }

    public void ShowLanguage(List<LanguageInfo> languages) => LanguageView.Show(languages);

    public void ShowLibraryMessage(string message)
    {
        ShowPage(Library, Loc.T("app.subtitle"));
        Library.ShowMessage(message);
    }

    public void ShowLibrary(IReadOnlyList<Game> games)
    {
        ShowPage(Library, Loc.T("app.subtitle"));
        Library.ShowGames(games, Loc.T("lib.empty"));
    }

    public void ShowGame(GameDetail detail, bool sameGame)
    {
        GameView.Show(detail, sameGame);
        ShowPage(GameView, Loc.T("page.game", detail.Game.Name));
    }

    public void ShowGameInfo(GameInfo? info, string? message) => GameView.ShowInfo(info, message);

    public void ShowMods(GameDetail detail, bool sameGame)
    {
        Mods.Show(detail, sameGame);
        ShowPage(Mods, Loc.T("page.mods", detail.Game.Name));
    }

    public void ShowSettings(AppSettings settings, string version, string status, List<string> catalogErrors, List<LanguageInfo> languages)
    {
        Settings.Show(settings, version, status, catalogErrors, languages);
        ShowPage(Settings, Loc.T("nav.settings"));
    }

    public void SetSettingsStatus(string status) => Settings.SetStatus(status);

    /// <summary>새 런처 버전이 있으면 머리글에 업데이트 버튼을 보여 줍니다. null이면 숨김</summary>
    public void ShowLauncherUpdate(string? version)
    {
        UpdateButton.Content = Loc.T("update.button", version);
        UpdateButton.Visibility = version is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>진행 덮개를 띄웁니다. using으로 감싸 끝나면 닫힘</summary>
    public BusyScope StartBusy(string message)
    {
        Busy.Show(message);
        return new BusyScope(Busy);
    }

    public bool EditConfig(ConfigFile config, string heading) =>
        new ConfigWindow(config, heading) { Owner = this }.ShowDialog() == true;

    public void Alert(string message) => MessageBox.Show(this, message, "LXIUM");

    public bool Confirm(string message) =>
        MessageBox.Show(this, message, "LXIUM", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    /// <summary>게임 카드 밖을 누르면 카드 위 세 칸을 닫음</summary>
    void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) => Library.CloseIfOutside(e.OriginalSource as DependencyObject);

    void Back_Click(object sender, RoutedEventArgs e) => Controller.Back();
    async void Refresh_Click(object sender, RoutedEventArgs e) => await Controller.LoadAsync(fetchCatalogs: true);
    void Settings_Click(object sender, RoutedEventArgs e) => SettingsController.Open();
    async void ApplyUpdate_Click(object sender, RoutedEventArgs e) => await SettingsController.ApplyUpdateAsync();
}
