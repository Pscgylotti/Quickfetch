using System;
using System.IO;
using System.Linq;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Interop;

namespace Quickfetch;

public partial class MainWindow
{
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
    private void CopySelectedFiles(bool cut)
    {
        var items = Results.SelectedItems.OfType<FileItem>().ToArray();
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
