using System.Windows;
using System.Windows.Media.Imaging;
using LXIUM.Controllers;
using LXIUM.Models;

namespace LXIUM.Views;

/// <summary>화면 표시만 담당. 사용자 동작은 모두 LibraryController로 넘깁니다.</summary>
public partial class MainWindow : Window
{
    readonly LibraryController controller;

    public MainWindow()
    {
        InitializeComponent();
        controller = new LibraryController(this);
        Loaded += async (_, _) => await controller.LoadAsync();
    }

    public LoaderInfo? SelectedLoader => LoaderList.SelectedItem as LoaderInfo;

    public void ShowBusy(string message)
    {
        GameList.ItemsSource = null;
        EmptyText.Text = message;
        EmptyText.Visibility = Visibility.Visible;
    }

    public void ShowLibrary(IReadOnlyList<Game> games)
    {
        Subtitle.Text = "게임 최적화 모드 런처";
        BackButton.Visibility = Visibility.Collapsed;
        RefreshButton.Visibility = Visibility.Visible;
        DetailView.Visibility = Visibility.Collapsed;
        LibraryView.Visibility = Visibility.Visible;
        GameList.ItemsSource = null;
        GameList.ItemsSource = games;
        EmptyText.Text = "addons 폴더에 애드온이 없습니다.";
        EmptyText.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowDetail(GameDetail detail)
    {
        var sameGame = (DetailView.DataContext as GameDetail)?.Game == detail.Game;
        var keep = SelectedLoader?.Id;
        DetailView.DataContext = detail;
        LoaderList.SelectedItem = detail.Loaders.FirstOrDefault(l => l.Id == keep) ?? detail.Loaders.FirstOrDefault();
        LoaderHint.Text = detail.Game.Root is null ? "게임 폴더를 찾으면 로더를 감지합니다."
            : detail.Loaders.Count switch
            {
                0 => "감지된 로더가 없습니다. 모드에 맞는 로더를 먼저 설치하세요.",
                1 => "게임 폴더에서 감지한 로더입니다.",
                _ => "로더가 여러 개 있습니다. 모드를 설치할 로더를 고르세요.",
            };
        Subtitle.Text = detail.Game.Name;
        BackButton.Visibility = Visibility.Visible;
        RefreshButton.Visibility = Visibility.Collapsed;
        LibraryView.Visibility = Visibility.Collapsed;
        DetailView.Visibility = Visibility.Visible;
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

    public void Alert(string message) => MessageBox.Show(this, message, "LXIUM");

    public bool Confirm(string message) =>
        MessageBox.Show(this, message, "LXIUM", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    static T Item<T>(object sender) => (T)((FrameworkElement)sender).DataContext;

    async void Game_Click(object sender, RoutedEventArgs e) => await controller.OpenAsync(Item<Game>(sender));
    void Back_Click(object sender, RoutedEventArgs e) => controller.Back();
    async void Refresh_Click(object sender, RoutedEventArgs e) => await controller.LoadAsync();
    void Install_Click(object sender, RoutedEventArgs e) => controller.Install(Item<AddonRow>(sender));
    void Uninstall_Click(object sender, RoutedEventArgs e) => controller.Uninstall(Item<AddonRow>(sender));
    void Play_Click(object sender, RoutedEventArgs e) => controller.Play();
    void Folder_Click(object sender, RoutedEventArgs e) => controller.OpenFolder();
}
