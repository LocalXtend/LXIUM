using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LXIUM.Views;

/// <summary>화면 전환 움직임. 모두 easeInOutCubic 0.3초 (design.md 9장)</summary>
public static class Motion
{
    public static readonly Duration Time = TimeSpan.FromSeconds(0.3);
    public static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

    public static DoubleAnimation To(double to) => new(to, Time) { EasingFunction = Ease };

    /// <summary>보이게 하고 투명 → 불투명, 아래 8px → 제자리</summary>
    public static void FadeIn(UIElement e)
    {
        e.Visibility = Visibility.Visible;
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Time) { EasingFunction = Ease });
        if (e.RenderTransform is not TranslateTransform t) e.RenderTransform = t = new TranslateTransform();
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, Time) { EasingFunction = Ease });
    }

    /// <summary>불투명 → 투명 뒤 숨김</summary>
    public static void FadeOut(UIElement e)
    {
        if (e.Visibility != Visibility.Visible) return;
        var a = To(0);
        a.Completed += (_, _) =>
        {
            // 그 사이 다시 FadeIn 했으면 숨기지 않음
            if (e.Opacity == 0) e.Visibility = Visibility.Collapsed;
        };
        e.BeginAnimation(UIElement.OpacityProperty, a);
    }
}
