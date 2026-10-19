using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LXIUM.Models;

namespace LXIUM.Views.Pages;

/// <summary>게임 설정 패널: 게임 정보·폴더·실행, 모드 로더 설치·업데이트·제거·설정</summary>
public partial class GamePage : UserControl
{
    public GamePage() => InitializeComponent();

    MainWindow Main => (MainWindow)Window.GetWindow(this);

    public LoaderInfo? SelectedLoader => LoaderList.SelectedItem as LoaderInfo;

    /// <summary>같은 게임을 다시 그리면 표지·스크롤·고른 로더를 그대로 둡니다.</summary>
    public void Show(GameDetail detail, bool sameGame)
    {
        var keep = SelectedLoader?.Id;
        DataContext = detail;
        LoaderList.SelectedItem = detail.Loaders.FirstOrDefault(l => l.Id == keep) ?? detail.Loaders.FirstOrDefault();
        LoaderHint.Text = detail.Game.Root is null ? Loc.T("loader.hintNoGame")
            : detail.Loaders.Count switch
            {
                0 => Loc.T("loader.hintNone"),
                1 => Loc.T("loader.hintOne"),
                _ => Loc.T("loader.hintMany"),
            };
        LoaderButtons.Visibility = detail.Loaders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (sameGame) return;
        Scroll.ScrollToTop();
        ShowInfo(null, Loc.T("game.loadingInfo"));
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

    static T Item<T>(object sender) => (T)((FrameworkElement)sender).DataContext;

    void Play_Click(object sender, RoutedEventArgs e) => Main.Controller.Play(null);
    void Folder_Click(object sender, RoutedEventArgs e) => Main.Controller.OpenFolder();
    async void InstallLoader_Click(object sender, RoutedEventArgs e) => await Main.Controller.InstallLoaderAsync(Item<LoaderChoice>(sender));
    async void RemoveLoader_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLoader is LoaderInfo loader) await Main.Controller.UninstallLoaderAsync(loader);
    }
    async void LoaderUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLoader is LoaderInfo loader) await Main.Controller.UpdateLoaderAsync(loader);
    }
    void LoaderConfig_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLoader is LoaderInfo loader) Main.Controller.OpenLoaderConfig(loader);
    }
    async void OpenMods_Click(object sender, RoutedEventArgs e) => await Main.Controller.ShowModsAsync();
}
