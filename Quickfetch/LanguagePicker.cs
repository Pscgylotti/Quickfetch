using System;
using System.Windows;
using System.Windows.Controls;

namespace Quickfetch;

public sealed class LanguagePicker : ComboBox
{
    private sealed record Choice(string Code, string Label) { public override string ToString() => Label; }
    public LanguagePicker()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Quickfetch;component/DownloadFields.xaml", UriKind.Relative) });
        SetResourceReference(StyleProperty, typeof(ComboBox));
        ItemsSource = new[] { new Choice("zh-CN", "简体中文"), new Choice("en-US", "English") }; SelectedValuePath = "Code";
        MinWidth = 160;
    }
}
