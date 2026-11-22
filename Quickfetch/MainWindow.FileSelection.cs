using System;
using System.IO;
using System.Collections.Immutable;
using System.Globalization;
using System.Windows.Data;
using System.Linq;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;

namespace Quickfetch;

public sealed class FileCheckedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values[0] is string path && values[1] is ImmutableHashSet<string> paths && paths.Contains(path);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public partial class MainWindow
{
    public static readonly DependencyProperty TouchFileControlsProperty = DependencyProperty.Register(
        nameof(TouchFileControls), typeof(Visibility), typeof(MainWindow), new PropertyMetadata(Visibility.Collapsed));
    public Visibility TouchFileControls
    {
        get => (Visibility)GetValue(TouchFileControlsProperty);
        private set => SetValue(TouchFileControlsProperty, value);
    }

    public static readonly DependencyProperty CheckedFilePathsProperty = DependencyProperty.Register(
        nameof(CheckedFilePaths), typeof(ImmutableHashSet<string>), typeof(MainWindow),
        new PropertyMetadata(ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase)));
    public ImmutableHashSet<string> CheckedFilePaths
    {
        get => (ImmutableHashSet<string>)GetValue(CheckedFilePathsProperty);
        private set => SetValue(CheckedFilePathsProperty, value);
    }
    private string? _fileCheckQuery;

    private void FileSelectionCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: FileItem item }) ToggleFileSelection(item);
        e.Handled = true;
    }

    private void ToggleFileSelection(FileItem item)
    {
        _doubleTap.Reset();
        if (Results.SelectionMode != SelectionMode.Extended || !Results.Items.Contains(item)) return;
        CheckedFilePaths = CheckedFilePaths.Contains(item.Path) ? CheckedFilePaths.Remove(item.Path) : CheckedFilePaths.Add(item.Path);
        UpdateCheckedSummary();
    }

    private void UpdateCheckedSummary()
    {
        var items = FileClipboardTargets(checkedOnly: true);
        CheckedSummary.Text = items.Length == 0 ? "" : Loc.F($"已选中 {items.Length} 项") + " · " + SelectionSize(items);
    }

    internal FileItem[] FileClipboardTargets(bool checkedOnly)
    {
        if (!CheckedFilePaths.IsEmpty) return Results.Items.OfType<FileItem>().Where(item => CheckedFilePaths.Contains(item.Path)).ToArray();
        return checkedOnly ? [] : Results.SelectedItems.OfType<FileItem>().ToArray();
    }

    private void FileSelectionAction_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button { Tag: FileMenuAction action }) RunSelectionAction(action, checkedOnly: true);
    }

    private void RunSelectionAction(FileMenuAction action, bool checkedOnly = false)
    {
        if (!_fileMode || _urlMode || _showingUrlHistory) return;
        var items = FileClipboardTargets(checkedOnly);
        if (items.Length == 0) return;
        try
        {
            if (action == FileMenuAction.CopyPath)
            {
                Clipboard.SetText(string.Join(Environment.NewLine, items.Select(item => "\"" + item.Path + "\"")));
                SetMessage(Loc.T("文件路径已复制。"));
            }
            else if (action is FileMenuAction.Copy or FileMenuAction.Cut)
            {
                CopySelectedFiles(items, action == FileMenuAction.Cut);
                SetMessage(Loc.T(action == FileMenuAction.Cut ? "文件已剪切，可在资源管理器中粘贴。" : "文件已复制，可在资源管理器中粘贴。"));
            }
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { SetMessage(Loc.T("无法完成文件操作：") + ex.Message); }
    }

    private bool _extendingFileSelection;
    private int _fileRangeAnchor = -1, _fileRangeCursor;
    private void ExtendFileSelection(int delta)
    {
        if (Results.Items.Count == 0) return;
        if (_fileRangeAnchor < 0) _fileRangeAnchor = _fileRangeCursor = Math.Max(0, Results.SelectedIndex);
        _fileRangeCursor = Math.Clamp(_fileRangeCursor + delta, 0, Results.Items.Count - 1);
        _extendingFileSelection = true;
        try
        {
            Results.SelectedItems.Clear();
            for (var i = Math.Min(_fileRangeAnchor, _fileRangeCursor); i <= Math.Max(_fileRangeAnchor, _fileRangeCursor); i++)
                Results.SelectedItems.Add(Results.Items[i]);
        }
        finally { _extendingFileSelection = false; QueueFilePreview(); }
        Results.ScrollIntoView(Results.Items[_fileRangeCursor]);
    }
    private void CopySelectedFiles(FileItem[] items, bool cut)
    {
        if (items.Length == 1) { ShellContextMenu.InvokeVerb(new WindowInteropHelper(this).Handle, items[0].Path, cut ? "cut" : "copy"); return; }
        if (items.Length == 0) return;
        if (items.Any(item => !File.Exists(item.Path) && !Directory.Exists(item.Path))) throw new IOException(Loc.T("文件已移动或删除，请刷新搜索。"));
        var data = new DataObject();
        var paths = new StringCollection(); paths.AddRange(items.Select(item => item.Path).ToArray());
        data.SetFileDropList(paths);
        // Standard Windows CF_HDROP + Preferred DropEffect supports cross-folder selections.
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 1)));
        Clipboard.SetDataObject(data, true);
    }
}
