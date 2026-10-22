using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Quickfetch.Core;

namespace Quickfetch;

public partial class CharacterMapWindow : Window
{
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly Action<string> _copy;
    private Task<CharacterCatalog>? _catalogTask;
    private bool _ready, _closed, _systemKeyboardOwned;
    private int _generation;
    private CharacterEntry[] _characterResults = [];
    public static readonly DependencyProperty CharacterColumnsProperty = DependencyProperty.Register(nameof(CharacterColumns), typeof(int), typeof(CharacterMapWindow), new PropertyMetadata(5));
    public int CharacterColumns { get => (int)GetValue(CharacterColumnsProperty); private set => SetValue(CharacterColumnsProperty, value); }
    internal System.Collections.Generic.IReadOnlyList<CharacterEntry> CharacterResults => _characterResults;
    internal string Draft => Composer.Text;
    internal string QueryText => SearchInput.Text;
    internal CharacterMapWindow(string draft = "", string query = "", Action<string>? copy = null, bool useBuiltInKeyboard = true)
    {
        InitializeComponent();
        _copy = copy ?? Clipboard.SetText;
        _useBuiltInKeyboard = useBuiltInKeyboard; _keyboardTarget = SearchInput;
        foreach (var input in new[] { SearchInput, Composer }) { input.IsInactiveSelectionHighlightEnabled = true; input.SelectionChanged += (_, _) => UpdateInputCaret(); input.SizeChanged += (_, _) => UpdateInputCaret(); }
        Width = Math.Min(920, SystemParameters.WorkArea.Width - 24);
        Height = Math.Min(740, SystemParameters.WorkArea.Height - 24);
        Category.ItemsSource = CharacterCatalog.Groups; Category.SelectedIndex = 0;
        Composer.Text = draft; Composer.CaretIndex = Composer.Text.Length; SearchInput.Text = query;
        _ready = true;
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await RefreshResults(); };
        Loaded += async (_, _) => { WindowBackdrop.Apply(new WindowInteropHelper(this).Handle); SearchInput.Focus(); if (TabletPosture.IsTablet) ShowInputKeyboard(SearchInput); await RefreshResults(); };
        Closed += (_, _) => { _closed = true; _generation++; _searchTimer.Stop(); _floatingKeyboard?.Close(); HideSystemKeyboard(); RemoveInputCaret(); };
    }
    protected override void OnSourceInitialized(EventArgs e) { base.OnSourceInitialized(e); WindowBackdrop.Apply(new WindowInteropHelper(this).Handle); }
    private void Search_Changed(object sender, TextChangedEventArgs e) => QueueSearch();
    private void Category_Changed(object sender, SelectionChangedEventArgs e) => QueueSearch();
    private void QueueSearch() { if (!_ready || _closed) return; _generation++; _searchTimer.Stop(); _searchTimer.Start(); }
    internal async Task RefreshResults()
    {
        if (_closed) return;
        _searchTimer.Stop();
        var generation = ++_generation; var query = SearchInput.Text; var group = Category.SelectedItem as string ?? "全部";
        try
        {
            var catalog = await (_catalogTask ??= Task.Run(() => CharacterCatalog.Instance));
            var results = await Task.Run(() => (Characters: catalog.Search(query, group), Styles: catalog.Convert(query)));
            if (_closed || generation != _generation) return;
            _characterResults = results.Characters.ToArray(); ReflowCharacters(); StyleResults.ItemsSource = results.Styles;
            EmptyHint.Text = results.Characters.Count == 0 ? Loc.T("没有匹配字符。可尝试英文名、中文关键词或 U+ 编码。") : "";
            StyleHint.Visibility = results.Styles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ResultCount.Text = Loc.F($"字符与 Emoji · {results.Characters.Count}") + (results.Characters.Count == 240 ? Loc.T("（前 240 项）") : "");
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { if (!_closed) EmptyHint.Text = Loc.T("字符库加载失败：") + ex.Message; }
    }
    private void Characters_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var columns = Math.Clamp((int)Math.Max(1, Characters.ActualWidth - 32) / 83, 2, 10);
        if (columns != CharacterColumns) { CharacterColumns = columns; ReflowCharacters(); }
    }
    private void ReflowCharacters() => Characters.ItemsSource = _characterResults.Chunk(CharacterColumns).ToArray();
    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button button) return;
        var text = button.DataContext switch { CharacterEntry entry => entry.Text, CharacterConversion style => style.Text, CharacterWord word => word.Text, _ => null };
        if (text is null) return;
        Insert(text); e.Handled = true;
    }
    internal void Insert(string text)
    {
        if (Composer.Text.Length - Composer.SelectionLength + text.Length > Composer.MaxLength) { Message.Text = Loc.T("文本已达到长度上限，请先复制或清空。"); return; }
        var start = Composer.SelectionStart;
        Composer.SelectedText = text;
        Composer.Select(start + text.Length, 0);
        Message.Text = Loc.T("已插入到文本光标位置 · 点击复制后可粘贴到其他应用");
    }
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyText();
    private void CopyText()
    {
        if (Composer.Text.Length == 0) { Message.Text = Loc.T("文本框为空。"); return; }
        try { _copy(Composer.Text); Message.Text = Loc.T("已复制到剪贴板"); }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { Message.Text = Loc.T("复制失败，请重试：") + ex.Message; }
    }
    private void Clear_Click(object sender, RoutedEventArgs e) { Composer.Clear(); Message.Text = Loc.T("已清空文本"); }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { if (_floatingKeyboard?.IsVisible == true) HideFloatingKeyboard(); else Close(); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { CopyText(); e.Handled = true; }
        else if (_floatingKeyboard?.IsVisible == true && e.Key is not (Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.System)) { HideFloatingKeyboard(); _keyboardTarget.Focus(); Keyboard.Focus(_keyboardTarget); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_Drag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is TextBlock) DragMove(); }
}
