using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Quickfetch;

public partial class MainWindow
{
    private sealed record Tile(string Id, string Label, string Glyph, string? SettingsPage = null, TogglePlugin? Plugin = null);
    private readonly HashSet<string> _switching = new();
    private readonly Dictionary<string, Func<Task>> _refreshTiles = new();
    private UniformGrid? _tileGrid;
    private readonly Dictionary<string, int> _pendingLevels = new();
    private readonly Dictionary<string, Task> _levelWrites = new();
    private CancellationTokenSource? _quickPage;
    private TextBlock? _quickHint;
    private List<Tile> _tiles = [];
    private int _externalQuickActions;
    private SystemQuickFlyout? _quickFlyout;
    private CancellationTokenSource? _flyoutSession;
    internal Action<string> LaunchQuickProtocol { get; set; } = QuickSystemControls.OpenSettings;

    private static string Q(string zh, string en) => Loc.IsEnglish ? en : zh;

    private void QuickToggles_Click(object sender, RoutedEventArgs e)
    {
        if (TogglePanel.IsVisible) { CloseQuickToggles(); FocusSearch(); return; }
        CloseExtensions();
        TogglePanel.Visibility = Visibility.Visible;
        PositionQuickToggles();
        SetToggled(QuickTogglesButton, true);
        _tiles = [
            new("wifi", "Wi-Fi", "\uE701", "network-wifi"),
            new("bluetooth", Q("蓝牙","Bluetooth"), "\uE702", "bluetooth"),
            new("power", Q("电源模式","Power mode"), "\uE945", "powersleep"),
            new("rotation", Q("旋转锁定","Rotation lock"), "\uE755", "display"),
            new("flight", Q("飞行模式","Airplane mode"), "\uE709", "network-airplanemode"),
            new("hotspot", Q("移动热点","Mobile hotspot"), "\uEC44", "network-mobilehotspot") ];
        var plugins = TogglePlugin.Discover(Path.Combine(AppContext.BaseDirectory, "plugins", "toggles"), Path.Combine(Settings.Storage, "plugins", "toggles"));
        _tiles.AddRange(plugins.Select(p => new Tile("plugin." + p.Id, Loc.IsEnglish ? p.Title : p.TitleZh ?? p.Title, p.Icon, Plugin: p)));
        DrawQuickHome();
        ToggleBody.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void PositionQuickToggles()
    {
        TogglePanel.Width = Math.Min(436, Math.Max(1, ExtensionsLayer.ActualWidth));
        if (_tileGrid != null) _tileGrid.Columns = TogglePanel.Width >= 366 ? 3 : 2;
        TogglePanel.MaxHeight = Math.Max(1, ExtensionsLayer.ActualHeight);
        var edge = QuickTogglesButton.TranslatePoint(new Point(QuickTogglesButton.ActualWidth, 0), ExtensionsLayer).X;
        Canvas.SetLeft(TogglePanel, Math.Clamp(edge - TogglePanel.Width, 0, Math.Max(0, ExtensionsLayer.ActualWidth - TogglePanel.Width)));
    }

    private void CloseQuickToggles()
    {
        if (TogglePanel.Visibility != Visibility.Visible) return;
        EndQuickPage();
        TogglePanel.Visibility = Visibility.Collapsed;
        SetToggled(QuickTogglesButton, false);
    }

    private void EndQuickPage()
    {
        _quickPage?.Cancel(); _quickPage?.Dispose(); _quickPage = null;
        _quickFlyout = null;
        _refreshTiles.Clear(); _tileGrid = null;
        ToggleBody.Children.Clear(); _quickHint = null;
    }

    private CancellationToken BeginQuickPage()
    {
        EndQuickPage();
        _quickPage = new();
        ((ScrollViewer)TogglePanel.Child).ScrollToTop();
        return _quickPage.Token;
    }

    private static TextBlock QuickText(string text, double size = 12, bool muted = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Muted" : "Ink");
        return block;
    }

    private static TextBlock QuickGlyph(string code, double size)
    {
        var glyph = new TextBlock { Text = code, FontSize = size };
        glyph.SetResourceReference(StyleProperty, "HeaderGlyph");
        return glyph;
    }

    private static Button QuickButton(object content, string name, Action clicked)
    {
        var button = new Button { Content = content, MinHeight = 38, ToolTip = name };
        button.SetResourceReference(StyleProperty, "QuickAction");
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => clicked();
        return button;
    }

    private void QuickHeading(string title, bool back)
    {
        var row = new DockPanel { MinHeight = 28, Margin = new Thickness(3, 0, 3, 6) };
        if (back)
        {
            var btn = QuickButton(QuickGlyph("\uE72B", 15), Q("返回","Back"), DrawQuickHome);
            btn.Width = 38; btn.Margin = new Thickness(0, 0, 7, 0); row.Children.Add(btn);
        }
        var heading = QuickText(title, 16); heading.FontWeight = FontWeights.SemiBold; row.Children.Add(heading);
        ToggleBody.Children.Add(row);
    }

    private void DrawQuickHome()
    {
        var token = BeginQuickPage();
        QuickHeading(Q("快捷开关","Quick settings"), false);
        _tileGrid = new UniformGrid { Columns = TogglePanel.Width >= 366 ? 3 : 2 };
        ToggleBody.Children.Add(_tileGrid);
        foreach (var tile in _tiles) AddQuickTile(tile.Plugin == null ? _tileGrid : ToggleBody, tile, token);

        var levels = new StackPanel();
        var well = new Border { Margin = new Thickness(3, 7, 3, 0), Padding = new Thickness(9, 1, 4, 1),
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = levels };
        well.SetResourceReference(Border.BackgroundProperty, "Hover");
        well.SetResourceReference(Border.BorderBrushProperty, "Line");
        ToggleBody.Children.Add(well);
        AddLevel(levels, "brightness", Q("亮度","Brightness"), "\uE706", token);
        var line = new Border { Height = 1, Margin = new Thickness(0, 0, 4, 0) };
        line.SetResourceReference(Border.BackgroundProperty, "Line"); levels.Children.Add(line);
        AddLevel(levels, "volume", Q("音量","Volume"), "\uE767", token);
        AddQuickHint();
    }

    private void AddQuickTile(Panel parent, Tile tile, CancellationToken token)
    {
        var face = new Grid();
        var pluginRow = tile.Plugin != null;
        var card = new Border { Height = pluginRow ? 44 : 61, Margin = new Thickness(3), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), Child = face };
        card.SetResourceReference(Border.BackgroundProperty, "Hover");
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        parent.Children.Add(card);
        var primary = new Button { Content = tile, HorizontalContentAlignment = HorizontalAlignment.Stretch, IsEnabled = false, ToolTip = tile.Label };
        primary.SetResourceReference(StyleProperty, "QuickAction");
        primary.SetResourceReference(ContentControl.ContentTemplateProperty, pluginRow ? "QuickPluginFace" : "QuickTileFace");
        AutomationProperties.SetName(primary, tile.Label);
        face.Children.Add(primary);
        if (tile.SettingsPage != null || tile.Plugin is { ExecutablePicker: true } || tile.Plugin?.Actions.Length > 0)
        {
            var more = QuickButton(QuickGlyph("\uE76C", 11), tile.Label + " · " + Q("选项","Options"), () => OpenQuickOptions(tile, face));
            more.Width = 40; more.Height = 38;
            more.HorizontalAlignment = HorizontalAlignment.Right;
            more.VerticalAlignment = pluginRow ? VerticalAlignment.Center : VerticalAlignment.Top;
            if (pluginRow)
            {
                face.ColumnDefinitions.Add(new ColumnDefinition());
                face.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                Grid.SetColumn(more, 1);
            }
            face.Children.Add(more);
        }
        BindQuickTile(tile, card, primary, token);
    }

    private void AddQuickHint()
    {
        _quickHint = QuickText("", 11, true); _quickHint.Visibility = Visibility.Collapsed;
        _quickHint.Margin = new Thickness(3, 5, 2, 0); ToggleBody.Children.Add(_quickHint);
    }

    private void QuickError(string message)
    {
        if (_disposed) return;
        if (_quickHint != null) { _quickHint.Text = message; _quickHint.Visibility = Visibility.Visible; }
        else SetMessage(message);
    }

    private async Task<ToggleReading> ReadTile(Tile tile, CancellationToken token)
    {
        if (tile.Plugin is not { } plugin) return await QuickSystemControls.Read(tile.Id);
        var exe = PluginExe(plugin.Id);
        var reading = await plugin.Run("status", exe, token, _settings.Folder);
        // Remember an executable discovered by the plugin so its tile can also cold-start it.
        if (!token.IsCancellationRequested && plugin.ExecutablePicker && string.IsNullOrEmpty(exe) &&
            reading.Executable is { } path && Path.IsPathFullyQualified(path) && File.Exists(path))
            SavePluginExe(plugin.Id, path);
        return reading;
    }

    private string? PluginExe(string id) => _settings.TogglePluginPaths?.GetValueOrDefault(id);
    private void SavePluginExe(string id, string path)
    {
        (_settings.TogglePluginPaths ??= new())[id] = path; _saveSettings(_settings);
    }

    private void BindQuickTile(Tile tile, Border card, Button button, CancellationToken token)
    {
        ToggleReading reading = new(null, false);
        int revision = 0;
        async Task Refresh()
        {
            var request = ++revision;
            ToggleReading next;
            try { next = await ReadTile(tile, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { next = new(null, false, ex.Message); }
            if (token.IsCancellationRequested || request != revision) return;
            reading = next;
            var state = !reading.Available ? Q("不可用","Unavailable") : reading.On switch
                { true => Q("已开启","On"), false => Q("已关闭","Off"), null => Q("状态未知","Unknown state") };
            button.ToolTip = tile.Label + " · " + state + (string.IsNullOrEmpty(reading.Note) || reading.Note == state ? "" : "\n" + reading.Note);
            AutomationProperties.SetItemStatus(button, state);
            var active = reading.Available && reading.On == true;
            card.SetResourceReference(Border.BackgroundProperty, active ? "Selection" : "Hover");
            card.SetResourceReference(Border.BorderBrushProperty, active ? "Selection" : "Line");
            button.SetResourceReference(ForegroundProperty, active ? "Accent" : "Muted");
            button.IsEnabled = reading.Available && !_switching.Contains(tile.Id);
        }
        _refreshTiles[tile.Id] = Refresh;
        _ = Refresh();
        button.Click += async (_, _) =>
        {
            if (!_switching.Add(tile.Id)) return;
            button.IsEnabled = false;
            string? error = null;
            try
            {
                _externalQuickActions++;
                var desired = reading.On.HasValue ? !reading.On.Value : (bool?)null;
                if (tile.Plugin is { } p) await p.Run(desired == false ? "off" : "on", PluginExe(p.Id), CancellationToken.None, _settings.Folder);
                else await QuickSystemControls.Set(tile.Id, desired, CancellationToken.None);
            }
            catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { error = tile.Label + " · " + ex.Message; }
            finally { _externalQuickActions--; _switching.Remove(tile.Id); }
            RefreshRelatedTiles(tile.Id);
            if (error != null) QuickError(error);
        };
    }

    private void RefreshRelatedTiles(string id)
    {
        static bool Radio(string name) => name is "wifi" or "bluetooth" or "flight" or "hotspot";
        foreach (var (key, refresh) in _refreshTiles)
            if (key == id || Radio(id) && Radio(key) || id == "bluetooth" && key == "volume") _ = refresh();
    }

    private void AddLevel(Panel parent, string id, string title, string glyph, CancellationToken token)
    {
        var row = new DockPanel { Height = 44 };
        var icon = QuickGlyph(glyph, 16); icon.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        icon.Width = 25; row.Children.Add(icon);
        var label = QuickText(title, 12); label.Width = 70; label.Margin = new Thickness(4, 0, 4, 0); row.Children.Add(label);

        var end = new Grid { Width = 40 };
        DockPanel.SetDock(end, Dock.Right); row.Children.Add(end);
        if (id == "volume")
            end.Children.Add(QuickButton(QuickGlyph("\uE76C", 12), Q("声音输出","Sound output"),
                () => ShowSystemFlyout("volume", CenterOnScreen(end))));
        var value = QuickText("—", 11, true); value.Width = 39; value.TextAlignment = TextAlignment.Center;
        DockPanel.SetDock(value, Dock.Right); row.Children.Add(value);
        var slider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true,
            IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center, IsEnabled = false, ToolTip = title };
        slider.SetResourceReference(StyleProperty, "QuickLevel");
        AutomationProperties.SetName(slider, title); row.Children.Add(slider); parent.Children.Add(row);
        bool readingLevel = false;
        int levelRevision = 0;
        slider.ValueChanged += (_, _) =>
        {
            if (readingLevel) return;
            levelRevision++;
            value.Text = (int)slider.Value + "%"; _pendingLevels[id] = (int)slider.Value;
            if (!_levelWrites.ContainsKey(id)) _levelWrites[id] = WriteLevels(id);
        };
        void MoveTouch(TouchEventArgs e)
        {
            if (slider.Template.FindName("PART_Track", slider) is Track track)
                slider.Value = Math.Round(track.ValueFromPoint(e.GetTouchPoint(track).Position));
            e.Handled = true;
        }
        slider.PreviewTouchDown += (_, e) => { if (!slider.AreAnyTouchesCaptured && e.TouchDevice.Capture(slider)) MoveTouch(e); };
        slider.PreviewTouchMove += (_, e) => { if (e.TouchDevice.Captured == slider) MoveTouch(e); };
        slider.PreviewTouchUp += (_, e) => { if (e.TouchDevice.Captured == slider) { MoveTouch(e); e.TouchDevice.Capture(null); } };
        if (id == "volume") _refreshTiles[id] = LoadLevel;
        _ = LoadLevel();

        async Task LoadLevel()
        {
            try
            {
                int revision = ++levelRevision;
                if (_levelWrites.TryGetValue(id, out var pending)) await pending;
                var level = await Task.Run<int?>(() => id == "brightness" ? QuickSystemControls.Brightness() : QuickSystemControls.Volume());
                if (token.IsCancellationRequested || revision != levelRevision) return;
                if (level == null) { slider.IsEnabled = false; value.Text = "—"; return; }
                readingLevel = true;
                slider.Value = level.Value; slider.IsEnabled = true; value.Text = level + "%";
                readingLevel = false;
            }
            catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex))
            {
                if (!token.IsCancellationRequested) { slider.IsEnabled = false; value.Text = "—"; slider.ToolTip = title + " · " + ex.Message; }
            }
        }
    }

    private async Task WriteLevels(string id)
    {
        try
        {
            do
            {
                await Task.Delay(65);
                if (!_pendingLevels.Remove(id, out var next)) break;
                await Task.Run(() => id == "brightness" ? QuickSystemControls.Brightness(next) : QuickSystemControls.Volume(next));
            } while (_pendingLevels.ContainsKey(id));
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { QuickError(ex.Message); }
        finally { _levelWrites.Remove(id); }
    }

    private void DetailButton(string title, Action action, string? glyph = null, bool selected = false)
    {
        var content = new DockPanel { Margin = new Thickness(12, 6, 10, 6) };
        if (glyph != null)
        {
            var icon = QuickGlyph(glyph, 14); icon.Width = 24;
            DockPanel.SetDock(icon, Dock.Right); content.Children.Add(icon);
        }
        content.Children.Add(QuickText(title));
        var button = QuickButton(content, title, action);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.MinHeight = 42; button.Margin = new Thickness(3, 2, 3, 2);
        button.SetResourceReference(BackgroundProperty, selected ? "Selection" : "Hover");
        if (selected)
        {
            button.SetResourceReference(ForegroundProperty, "Accent");
            AutomationProperties.SetItemStatus(button, Q("已选中", "Selected"));
        }
        ToggleBody.Children.Add(button);
    }

    private static Point CenterOnScreen(FrameworkElement element) =>
        element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));

    private void OpenQuickOptions(Tile tile, FrameworkElement face)
    {
        if (tile.Id is "wifi" or "bluetooth") ShowSystemFlyout(tile.Id, CenterOnScreen(face));
        else DrawQuickDetail(tile);
    }

    private async void ShowSystemFlyout(string id, Point trigger)
    {
        if (_quickPage is null || _quickFlyout?.Opening == true) return;
        _flyoutSession?.Cancel();
        using var session = CancellationTokenSource.CreateLinkedTokenSource(_quickPage.Token);
        _flyoutSession = session;
        var token = session.Token;
        _activationRetry.Stop();
        _restoringLauncherFocus = false;
        var corner = TogglePanel.PointToScreen(new Point(TogglePanel.ActualWidth, 0));
        var flyout = _quickFlyout = new SystemQuickFlyout();
        try
        {
            static System.Drawing.Point Pixel(Point p) => new((int)Math.Round(p.X), (int)Math.Round(p.Y));
            await flyout.Show(id, Pixel(corner), Pixel(trigger), VisualTreeHelper.GetDpi(TogglePanel).DpiScaleX, LaunchQuickProtocol, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { if (!token.IsCancellationRequested) QuickError(ex.Message); }
        finally
        {
            if (_flyoutSession == session) _flyoutSession = null;
            if (_quickFlyout == flyout)
            {
                _quickFlyout = null;
                if (!token.IsCancellationRequested) RefreshRelatedTiles(id);
            }
        }
    }

    private async void DrawQuickDetail(Tile tile)
    {
        var token = BeginQuickPage();
        QuickHeading(tile.Label, true);
        var description = QuickText("", 12, true); description.Margin = new Thickness(5, 0, 5, 6); ToggleBody.Children.Add(description);
        if (tile.SettingsPage is { } page)
        {
            if (tile.Id == "power")
            {
                try
                {
                    var (pluggedIn, current) = QuickPowerMode.Current();
                    description.Text = pluggedIn ? Q("已接通电源", "Plugged in") : Q("正在使用电池", "On battery");
                    foreach (var mode in QuickPowerMode.Choices)
                        DetailButton(mode.Name, () =>
                        {
                            try { mode.Apply(pluggedIn); DrawQuickDetail(tile); }
                            catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { QuickError(ex.Message); }
                        }, mode == current ? "\uE73E" : null, mode == current);
                }
                catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { description.Text = ex.Message; }
            }
            DetailButton(Q("打开系统设置","Open Windows settings"), () => OpenSystemPage("ms-settings:" + page), "\uE8A7");
        }
        if (tile.Plugin is { } plugin)
        {
            if (plugin.ExecutablePicker) DetailButton(Q("选择程序位置…","Choose executable…"), () =>
            {
                _choosingFolder = true;
                try
                {
                    var picker = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true, FileName = PluginExe(plugin.Id) ?? "" };
                    if (picker.ShowDialog(this) == true) { SavePluginExe(plugin.Id, picker.FileName); DrawQuickDetail(tile); }
                }
                catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { QuickError(ex.Message); }
                finally { _choosingFolder = false; }
            }, "\uE8B7");
            foreach (var a in plugin.Actions)
                DetailButton(Loc.IsEnglish ? a.Title : a.TitleZh ?? a.Title, async () =>
                {
                    if (!_switching.Add(tile.Id)) return;
                    try
                    {
                        _externalQuickActions++;
                        var reply = await plugin.Run("action." + a.Id, PluginExe(plugin.Id), CancellationToken.None, _settings.Folder);
                        if (!token.IsCancellationRequested) description.Text = reply.Note ?? "";
                    }
                    catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { QuickError(ex.Message); }
                    finally { _externalQuickActions--; _switching.Remove(tile.Id); RefreshRelatedTiles(tile.Id); }
                });
        }
        AddQuickHint();
        if (tile.Id == "power") return;
        try
        {
            var info = tile.Plugin == null ? await QuickSystemControls.Details(tile.Id) : (await ReadTile(tile, token)).Note;
            if (!token.IsCancellationRequested) description.Text = info ?? "";
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { if (!token.IsCancellationRequested) description.Text = ex.Message; }
    }

    private void OpenSystemPage(string uri)
    {
        try { QuickSystemControls.OpenSettings(uri); }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { QuickError(ex.Message); }
    }
}
