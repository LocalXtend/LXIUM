using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LXIUM.Models;

namespace LXIUM.Views.Pages;

public sealed record TagChoice(string Id, string Name, bool Checked);

/// <summary>모드 패널: 추가한 모드 / 모음에서 추가할 수 있는 모드. 검색어와 태그로 거릅니다.</summary>
public partial class ModsPage : UserControl
{
    List<AddonRow> rows = [];
    readonly HashSet<string> tags = [];

    public ModsPage() => InitializeComponent();

    MainWindow Main => (MainWindow)Window.GetWindow(this);

    /// <summary>같은 게임을 다시 그리면 검색어·태그·스크롤을 그대로 둡니다.</summary>
    public void Show(GameDetail detail, bool sameGame)
    {
        rows = detail.Addons;
        if (!sameGame)
        {
            Search.Text = "";
            tags.Clear();
            Scroll.ScrollToTop();
        }
        TagFilter.ItemsSource = Addon.KnownTags.Where(t => rows.Any(r => r.Package.Versions.Any(v => v.Manifest.Tags?.Contains(t) ?? false)))
            .Select(t => new TagChoice(t, Loc.T($"tag.{t}"), tags.Contains(t))).ToList();
        ConflictText.Text = detail.LoaderConflict ?? "";
        ConflictBox.Visibility = detail.HasLoaderConflict ? Visibility.Visible : Visibility.Collapsed;
        Apply();
    }

    void Apply()
    {
        SearchHint.Visibility = Search.Text == "" ? Visibility.Visible : Visibility.Collapsed;
        var shown = rows.Where(r => r.Matches(Search.Text, tags)).ToList();
        AddedList.ItemsSource = shown.Where(r => r.IsAdded).ToList();
        AvailableList.ItemsSource = shown.Where(r => r.IsAvailable).ToList();
        AddedEmpty.Visibility = AddedList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AvailableEmpty.Visibility = AvailableList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } chip)
        {
            if (chip.IsChecked == true) tags.Add(tag);
            else tags.Remove(tag);
        }
        if (IsLoaded) Apply();
    }

    static AddonRow Item(object sender) => (AddonRow)((FrameworkElement)sender).DataContext;

    async void Install_Click(object sender, RoutedEventArgs e) => await Main.Controller.InstallAsync(Item(sender));
    async void Uninstall_Click(object sender, RoutedEventArgs e) => await Main.Controller.UninstallAsync(Item(sender));
    void Config_Click(object sender, RoutedEventArgs e) => Main.Controller.OpenModConfig(Item(sender));
    async void GameSettings_Click(object sender, RoutedEventArgs e) => await Main.Controller.ShowGameAsync();
}
