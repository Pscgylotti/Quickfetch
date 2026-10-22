using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Quickfetch.Core;

namespace Quickfetch;

public partial class MainWindow
{
    private UrlHistory _urlHistory = null!;
    private bool _urlMode, _showingUrlHistory, _urlSelectionMoved;
    internal static bool IsUrlShortcut(Key key, ModifierKeys modifiers) => key == Key.W && modifiers == ModifierKeys.Alt;
    internal void EnterUrlMode()
    {
        ResetOpenAnimation(); _fileVersion++; _fileCancellation?.Cancel(); _fileTimer.Stop();
        _urlMode = _fileMode = true; FileSortHeader.Visibility = Visibility.Collapsed;
        ModeLabel.Text = Loc.T("网址 · HISTORY"); ModeButton.ToolTip = Loc.T("返回应用搜索");
        Placeholder.Text = Loc.T("输入网址或搜索网址历史"); ResultHint.Text = Loc.T("↑ ↓ 选择    Tab 补全    Enter 打开 / 下载");
        QuickKeyboard.SetFileMode(true); QuickKeyboard.SetUrlMode(true);
        SetTouchKeyboard(QuickKeyboard.Visibility == Visibility.Visible, physicalKeyboard: !_touchInput, animateModeBounds: true);
        ShowUrlSuggestions(); FocusSearch(); Query.SelectAll(); AnimateModeTransition();
    }
    private void ShowUrlSuggestions()
    {
        _showingUrlHistory = true; _urlSelectionMoved = false;
        CancelFilePreview(); FilePreviewPanel.Visibility = Visibility.Collapsed;
        FileSortHeader.Visibility = InstallEverythingButton.Visibility = MoreResults.Visibility = Visibility.Collapsed;
        Results.ItemTemplate = _appTemplate; QuickKeyboard.SetUrlMode(true);
        var items = _urlHistory.Search(Query.Text, DateTimeOffset.UtcNow).Select(v => new UrlHistoryItem(v)).ToArray();
        Results.ItemsSource = items; Results.SelectedIndex = items.Length > 0 ? 0 : -1;
        if (items.Length > 0) Results.ScrollIntoView(items[0]);
        EmptyMessage.Text = Query.Text.Length == 0 ? Loc.T("网址历史为空。输入网址后按 Enter 打开网页或下载文件。") : Loc.T("按 Enter：网页在浏览器中打开，文件加入后台下载队列。");
        EmptyMessage.Visibility = items.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        Status.Text = Loc.F($"{items.Length} 条网址历史 · 常用 / 最近使用");
        _urlSelectionMoved = false;
    }
    internal bool CompleteUrl()
    {
        if (!_showingUrlHistory || Results.SelectedItem is not UrlHistoryItem item) return false;
        Query.Text = item.Visit.Url; Query.Select(Query.Text.Length, 0); FocusSearch(); return true;
    }
    private string UrlToOpen() => Results.SelectedItem is UrlHistoryItem item && (_urlSelectionMoved || !UrlHistory.TryAddress(Query.Text, out _)) ? item.Visit.Url : Query.Text;
    private void ClearUrlHistory_Click(object sender, RoutedEventArgs e)
    {
        try { _urlHistory.Clear(); if (_showingUrlHistory) ShowUrlSuggestions(); SetMessage(Loc.T("已清空网址历史和网页检查缓存。")); }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { SetMessage(Loc.T("无法保存网址历史：") + ex.Message); }
    }
}
public sealed record UrlHistoryItem(UrlVisit Visit) : ILauncherItem
{
    public string Name => Visit.Name;
    public string Path => Visit.Url;
    public string Location => Visit.Url;
    public string Initial => "↗";
    public System.Windows.Media.ImageSource? Icon => null;
    public string MatchLabel => (Visit.IsFile ? Loc.T("下载") : Loc.T("网页")) + " · " + Visit.Usage.Count;
}
