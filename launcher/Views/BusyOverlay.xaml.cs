using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LXIUM.Models;

namespace LXIUM.Views;

/// <summary>진행 중 덮개. Show로 띄우고 Report(0~1)로 진행률, Hide로 닫습니다.</summary>
public partial class BusyOverlay : UserControl
{
    public BusyOverlay()
    {
        InitializeComponent();
        Spin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    public void Show(string message)
    {
        Message.Text = message;
        Detail.Text = Loc.T("busy.wait");
        Bar.Visibility = Visibility.Collapsed;
        Progress.Value = 0;
        if (Visibility != Visibility.Visible) Motion.FadeIn(this);
    }

    /// <summary>받기 진행률 0~1. 처음 값이 오면 막대를 보여 줌</summary>
    public void Report(double value)
    {
        Bar.Visibility = Visibility.Visible;
        Progress.Value = value;
        Detail.Text = $"{value:P0}";
    }

    public void Hide() => Motion.FadeOut(this);
}
