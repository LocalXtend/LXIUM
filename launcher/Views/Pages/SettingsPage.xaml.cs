using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using LXIUM.Models;

namespace LXIUM.Views.Pages;

/// <summary>런처 설정: 업데이트, 언어, 정보, 애드온 모음</summary>
public partial class SettingsPage : UserControl
{
    /// <summary>화면을 채우는 동안 생기는 Checked·SelectionChanged는 사용자 동작이 아니므로 무시</summary>
    bool filling;

    public SettingsPage() => InitializeComponent();

    MainWindow Main => (MainWindow)Window.GetWindow(this);

    public void Show(AppSettings settings, string version, string status, List<string> catalogErrors, List<LanguageInfo> languages)
    {
        filling = true;
        VersionText.Text = Loc.T("settings.version", version);
        AutoUpdateSwitch.IsChecked = settings.AutoUpdate;
        UpdateStatus.Text = status.Trim();
        LanguageBox.ItemsSource = languages;
        LanguageBox.SelectedItem = languages.FirstOrDefault(l => l.Code == Loc.I.Code);
        CatalogList.ItemsSource = null;
        CatalogList.ItemsSource = settings.Catalogs;
        CatalogStatus.Text = catalogErrors.Count == 0 ? "" : Loc.T("settings.catalogErrors") + "\n" + string.Join("\n", catalogErrors);
        filling = false;
    }

    public void SetStatus(string status) => UpdateStatus.Text = status;

    void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (!filling) Main.SettingsController.SetAutoUpdate(AutoUpdateSwitch.IsChecked == true);
    }

    void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!filling && LanguageBox.SelectedItem is LanguageInfo lang) Main.SettingsController.SetLanguage(lang.Code);
    }

    async void CheckNow_Click(object sender, RoutedEventArgs e) => await Main.SettingsController.CheckNowAsync();

    async void AddCatalog_Click(object sender, RoutedEventArgs e)
    {
        await Main.SettingsController.AddCatalogAsync(CatalogInput.Text);
        CatalogInput.Text = "";
    }

    async void RemoveCatalog_Click(object sender, RoutedEventArgs e) =>
        await Main.SettingsController.RemoveCatalogAsync((string)((FrameworkElement)sender).DataContext);

    async void ResetCatalogs_Click(object sender, RoutedEventArgs e) => await Main.SettingsController.ResetCatalogsAsync();

    void Link_Navigate(object sender, RequestNavigateEventArgs e) => Main.SettingsController.OpenLink(e.Uri.AbsoluteUri);
}
