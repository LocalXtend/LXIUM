using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using LXIUM.Controllers;
using LXIUM.Models;

namespace LXIUM.Views;

/// <summary>화면 표시만 담당. 사용자 동작은 LibraryController(라이브러리·게임)와 SettingsController(런처 설정)로 넘깁니다.</summary>
public partial class MainWindow : Window
{
    readonly LibraryController controller;
    readonly SettingsController settingsController;
    bool showingSettings;

    public MainWindow()
    {
        InitializeComponent();
        var settings = AppSettings.Load(LibraryController.AppDir);
        controller = new LibraryController(this, settings);
        settingsController = new SettingsController(this, settings, controller);
        Loaded += async (_, _) => await settingsController.StartAsync();
    }

    public LoaderInfo? SelectedLoader => LoaderList.SelectedItem as LoaderInfo;

    /// <summary>라이브러리·상세·설정 중 하나만 보이게 하고 머리글 버튼을 맞춥니다.</summary>
    void ShowPage(FrameworkElement page, string subtitle)
    {
        LibraryView.Visibility = page == LibraryView ? Visibility.Visible : Visibility.Collapsed;
        DetailView.Visibility = page == DetailView ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = page == SettingsView ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = page == LibraryView ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.Visibility = page == LibraryView ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = page == SettingsView ? Visibility.Collapsed : Visibility.Visible;
        showingSettings = page == SettingsView;
        Subtitle.Text = subtitle;
    }

    public void ShowBusy(string message)
    {
        ShowPage(LibraryView, "게임 모드 런처");
        GameList.ItemsSource = null;
        EmptyText.Text = message;
        EmptyText.Visibility = Visibility.Visible;
    }

    public void ShowLibrary(IReadOnlyList<Game> games)
    {
        ShowPage(LibraryView, "게임 모드 런처");
        GameList.ItemsSource = null;
        GameList.ItemsSource = games;
        EmptyText.Text = "설치된 게임 중 애드온이 있는 게임이 없습니다. 설정에서 애드온 모음을 추가하거나 addons 폴더에 애드온 zip을 넣으세요.";
        EmptyText.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowDetail(GameDetail detail)
    {
        var sameGame = DetailView.IsVisible && (DetailView.DataContext as GameDetail)?.Game.Info.Id == detail.Game.Info.Id;
        var keep = SelectedLoader?.Id;
        DetailView.DataContext = detail;
        LoaderList.SelectedItem = detail.Loaders.FirstOrDefault(l => l.Id == keep) ?? detail.Loaders.FirstOrDefault();
        LoaderHint.Text = detail.Game.Root is null ? "게임 폴더를 찾으면 로더를 감지합니다."
            : detail.Loaders.Count switch
            {
                0 => "감지된 로더가 없습니다. 아래 버튼으로 모드에 맞는 로더를 설치할 수 있습니다.",
                1 => "게임 폴더에서 감지한 로더입니다.",
                _ => "로더가 여러 개 있습니다. 모드를 설치할 로더를 고르세요.",
            };
        LoaderButtons.Visibility = detail.Loaders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowPage(DetailView, detail.Game.Name);
        if (sameGame) return;

        DetailView.ScrollToTop();
        ShowInfo(null, "Steam 상점에서 정보를 가져오는 중…");
        Cover.Source = detail.Game.CapsuleUrl is string url ? new BitmapImage(new Uri(url)) : null;
    }

    public void ShowInfo(GameInfo? info, string? message)
    {
        static string Join(string[]? items) => items is { Length: > 0 } ? string.Join(", ", items) : "—";
        InfoName.Text = info?.Name ?? "";
        Summary.Text = info?.Summary ?? message ?? "";
        Genres.ItemsSource = info?.Genres;
        Released.Text = info?.Released ?? "—";
        Developers.Text = Join(info?.Developers);
        Publishers.Text = Join(info?.Publishers);
    }

    public void ShowSettings(AppSettings settings, string version, string status, List<string> catalogErrors)
    {
        ShowPage(SettingsView, "설정");
        VersionText.Text = $"지금 버전: LXIUM {version}";
        AutoUpdateSwitch.IsChecked = settings.AutoUpdate;
        UpdateStatus.Text = status.Trim();
        CatalogList.ItemsSource = null;
        CatalogList.ItemsSource = settings.Catalogs;
        CatalogStatus.Text = catalogErrors.Count == 0 ? "" : "받지 못한 모음·항목:\n" + string.Join("\n", catalogErrors);
    }

    public void SetSettingsStatus(string status) => UpdateStatus.Text = status;

    /// <summary>새 런처 버전이 있으면 머리글에 업데이트 버튼을 보여 줍니다. null이면 숨김</summary>
    public void ShowLauncherUpdate(string? version)
    {
        UpdateButton.Content = $"LXIUM {version} 업데이트";
        UpdateButton.Visibility = version is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>오래 걸리는 작업 중에는 화면을 잠그고 머리글에 진행 상황을 보여 줍니다. null이면 해제</summary>
    public void SetBusy(string? message)
    {
        DetailView.IsEnabled = SettingsView.IsEnabled = BackButton.IsEnabled = UpdateButton.IsEnabled = message is null;
        Subtitle.Text = message ?? (showingSettings ? "설정" : (DetailView.DataContext as GameDetail)?.Game.Name ?? "");
    }

    public bool EditConfig(ConfigFile config, string heading) =>
        new ConfigWindow(config, heading) { Owner = this }.ShowDialog() == true;

    public void Alert(string message) => MessageBox.Show(this, message, "LXIUM");

    public bool Confirm(string message) =>
        MessageBox.Show(this, message, "LXIUM", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    static T Item<T>(object sender) => (T)((FrameworkElement)sender).DataContext;

    async void Game_Click(object sender, RoutedEventArgs e) => await controller.OpenAsync(Item<Game>(sender));
    void Back_Click(object sender, RoutedEventArgs e) => controller.Back();
    async void Refresh_Click(object sender, RoutedEventArgs e) => await controller.LoadAsync(fetchCatalogs: true);
    void Settings_Click(object sender, RoutedEventArgs e) => settingsController.Open();
    async void ApplyUpdate_Click(object sender, RoutedEventArgs e) => await settingsController.ApplyUpdateAsync();
    async void Install_Click(object sender, RoutedEventArgs e) => await controller.InstallAsync(Item<AddonRow>(sender));
    void Uninstall_Click(object sender, RoutedEventArgs e) => controller.Uninstall(Item<AddonRow>(sender));
    async void InstallLoader_Click(object sender, RoutedEventArgs e) => await controller.InstallLoaderAsync(Item<LoaderChoice>(sender));
    void RemoveLoader_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLoader is LoaderInfo loader) controller.UninstallLoader(loader);
    }
    void ModConfig_Click(object sender, RoutedEventArgs e) => controller.OpenModConfig(Item<AddonRow>(sender));
    void LoaderConfig_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLoader is LoaderInfo loader) controller.OpenLoaderConfig(loader);
    }
    void Play_Click(object sender, RoutedEventArgs e) => controller.Play();
    void Folder_Click(object sender, RoutedEventArgs e) => controller.OpenFolder();

    void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (SettingsView.IsVisible) settingsController.SetAutoUpdate(AutoUpdateSwitch.IsChecked == true);
    }
    async void CheckNow_Click(object sender, RoutedEventArgs e) => await settingsController.CheckNowAsync();
    async void AddCatalog_Click(object sender, RoutedEventArgs e)
    {
        await settingsController.AddCatalogAsync(CatalogInput.Text);
        CatalogInput.Text = "";
    }
    async void RemoveCatalog_Click(object sender, RoutedEventArgs e) => await settingsController.RemoveCatalogAsync(Item<string>(sender));
    void Link_Navigate(object sender, RequestNavigateEventArgs e) => settingsController.OpenLink(e.Uri.AbsoluteUri);
}
