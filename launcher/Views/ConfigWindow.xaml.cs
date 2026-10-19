using System.Windows;
using LXIUM.Models;

namespace LXIUM.Views;

/// <summary>설정 파일 편집 창. 값은 ConfigEntry에 바로 바인딩되고, 저장 여부만 DialogResult로 돌려줍니다.</summary>
public partial class ConfigWindow : Window
{
    public ConfigWindow(ConfigFile config, string heading)
    {
        InitializeComponent();
        Title = heading;
        Heading.Text = heading;
        FilePath.Text = config.Path;
        NewNotice.Visibility = config.IsNew ? Visibility.Visible : Visibility.Collapsed;
        SectionList.ItemsSource = config.Sections;
    }

    void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
