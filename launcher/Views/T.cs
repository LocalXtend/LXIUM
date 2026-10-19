using System.Windows.Data;
using System.Windows.Markup;
using LXIUM.Models;

namespace LXIUM.Views;

/// <summary>XAML 문구: Text="{l:T lib.refresh}". 언어를 바꾸면 바로 다시 그려집니다.</summary>
public sealed class T(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
