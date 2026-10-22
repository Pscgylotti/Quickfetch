using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using Quickfetch.Core;

namespace Quickfetch;

public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Current { get; } = new();
    public static bool IsEnglish => TextLocalization.IsEnglish;
    private static readonly Lazy<Dictionary<string, string>> Keys = new(() => TextLocalization.All.Keys.ToDictionary(Key, source => source));
    private static readonly Lazy<Dictionary<string, string>> Reverse = new(() => TextLocalization.All.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.First().Key));
    public string this[string key] => T(Keys.Value.GetValueOrDefault(key, key));
    public static string T(string source) => TextLocalization.T(source);
    public static string F(FormattableString value) => TextLocalization.F(value);
    public static string Key(string source) => "T" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12].ToLowerInvariant();
    internal static void SetLanguage(string? language)
    {
        TextLocalization.Language = language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en-US" : "zh-CN";
        Current.PropertyChanged?.Invoke(Current, new PropertyChangedEventArgs("Item[]"));
    }
    internal static string Existing(string text) => T(Reverse.Value.GetValueOrDefault(text, text));
    internal static void RefreshTree(DependencyObject root)
    {
        if (root is TextBlock text && !BindingOperations.IsDataBound(text, TextBlock.TextProperty) && text.Name is not ("FolderLabel" or "PreviewTitle" or "PreviewText")) text.Text = Existing(text.Text);
        if (root is ContentControl content && content.Content is string value && !BindingOperations.IsDataBound(content, ContentControl.ContentProperty)) content.Content = Existing(value);
        if (root is FrameworkElement element && element.ToolTip is string tip && !BindingOperations.IsDataBound(element, FrameworkElement.ToolTipProperty)) element.ToolTip = Existing(tip);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) RefreshTree(VisualTreeHelper.GetChild(root, i));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding("[" + key + "]") { Source = Loc.Current, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
public sealed class LocalizedTextConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, CultureInfo culture) => value is string source ? Loc.T(source) : value;
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public sealed class CharacterNameConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, CultureInfo culture) => value is CharacterEntry entry ? Loc.IsEnglish ? entry.EnglishName : entry.Name : value;
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
