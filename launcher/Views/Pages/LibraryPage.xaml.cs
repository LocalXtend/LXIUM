using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LXIUM.Models;

namespace LXIUM.Views.Pages;

/// <summary>게임 카드 목록. 표지를 누르면 세 칸(설정·모드·플레이)이 뜹니다.</summary>
public partial class LibraryPage : UserControl
{
    /// <summary>지금 세 칸이 떠 있는 카드의 덮개</summary>
    FrameworkElement? openOverlay;

    public LibraryPage() => InitializeComponent();

    MainWindow Main => (MainWindow)Window.GetWindow(this);

    public void ShowGames(IReadOnlyList<Game> games, string emptyText)
    {
        CloseOverlay();
        GameList.ItemsSource = null;
        GameList.ItemsSource = games;
        EmptyText.Text = emptyText;
        EmptyText.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowMessage(string message)
    {
        CloseOverlay();
        GameList.ItemsSource = null;
        EmptyText.Text = message;
        EmptyText.Visibility = Visibility.Visible;
    }

    void Cover_Click(object sender, MouseButtonEventArgs e)
    {
        var overlay = (FrameworkElement)((Grid)sender).FindName("Overlay");
        if (overlay is null || overlay == openOverlay) return;
        CloseOverlay();
        openOverlay = overlay;
        Motion.FadeIn(overlay);
    }

    void Card_MouseLeave(object sender, MouseEventArgs e)
    {
        if (openOverlay is not null && IsInside(openOverlay, (DependencyObject)sender)) CloseOverlay();
    }

    /// <summary>표지 밖을 누르면 세 칸을 닫음 (MainWindow가 누를 때마다 부름)</summary>
    public void CloseIfOutside(DependencyObject? clicked)
    {
        if (openOverlay is null) return;
        var cover = VisualTreeHelper.GetParent(openOverlay);
        if (clicked is null || cover is null || !IsInside(clicked, cover)) CloseOverlay();
    }

    public void CloseOverlay()
    {
        if (openOverlay is not null) Motion.FadeOut(openOverlay);
        openOverlay = null;
    }

    static bool IsInside(DependencyObject child, DependencyObject parent)
    {
        for (var p = child; p is not null; p = p is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (p == parent) return true;
        return false;
    }

    static Game Item(object sender) => (Game)((FrameworkElement)sender).DataContext;

    async void ZoneSettings_Click(object sender, RoutedEventArgs e)
    {
        CloseOverlay();
        await Main.Controller.OpenGameAsync(Item(sender));
    }

    async void ZoneMods_Click(object sender, RoutedEventArgs e)
    {
        CloseOverlay();
        await Main.Controller.OpenModsAsync(Item(sender));
    }

    void ZonePlay_Click(object sender, RoutedEventArgs e)
    {
        CloseOverlay();
        Main.Controller.Play(Item(sender));
    }
}
