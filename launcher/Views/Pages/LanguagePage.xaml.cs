using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LXIUM.Models;

namespace LXIUM.Views.Pages;

/// <summary>처음 실행할 때 언어 고르기. 고르면 그 언어로 바로 바뀌고 "다음"이 켜집니다.</summary>
public partial class LanguagePage : UserControl
{
    bool open;
    LanguageInfo? chosen;

    public LanguagePage() => InitializeComponent();

    MainWindow Main => (MainWindow)Window.GetWindow(this);

    public void Show(List<LanguageInfo> languages)
    {
        Languages.ItemsSource = languages;
        Visibility = Visibility.Visible;
        Opacity = 1;
    }

    void Toggle_Click(object sender, RoutedEventArgs e) => SetOpen(!open);

    /// <summary>목록 펼치기·접기: 투명도와 세로 크기, 화살표 회전 (0.3초)</summary>
    void SetOpen(bool value)
    {
        open = value;
        var scale = (ScaleTransform)DropList.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.To(value ? 1 : 0.92));
        ((RotateTransform)Chevron.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, Motion.To(value ? 180 : 0));
        if (value)
        {
            DropList.Visibility = Visibility.Visible;
            DropList.BeginAnimation(OpacityProperty, Motion.To(1));
        }
        else
        {
            var fade = Motion.To(0);
            fade.Completed += (_, _) => { if (!open) DropList.Visibility = Visibility.Collapsed; };
            DropList.BeginAnimation(OpacityProperty, fade);
        }
    }

    void Pick_Click(object sender, RoutedEventArgs e)
    {
        chosen = (LanguageInfo)((FrameworkElement)sender).DataContext;
        Main.SettingsController.PreviewLanguage(chosen.Code);
        Chosen.Text = chosen.Name;
        Chosen.Foreground = (Brush)FindResource("Ink");
        Chosen.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Time) { EasingFunction = Motion.Ease });
        NextButton.IsEnabled = true;
        NextButton.BeginAnimation(OpacityProperty, Motion.To(1));
        SetOpen(false);
    }

    async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (chosen is null) return;
        Motion.FadeOut(this);
        await Main.SettingsController.ChooseLanguageAsync(chosen.Code);
    }
}
