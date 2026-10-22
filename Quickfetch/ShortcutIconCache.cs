using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Quickfetch.Core;

namespace Quickfetch;

public sealed class ShortcutIconCache
{
    private sealed record Cached(long Modified, long Length, ImageSource? Icon, byte[]? Png);
    private readonly Dictionary<string, Cached> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    internal int ExtractionCount { get; private set; }

    // Shell icon handlers require COM. Keep extraction off the UI thread in an STA.
    public Task<IReadOnlyDictionary<string, ImageSource?>> LoadAsync(IReadOnlyList<ShortcutEntry> entries, bool force = false, IReadOnlyDictionary<string, ShortcutMetadata>? metadata = null)
    {
        var completion = new TaskCompletionSource<IReadOnlyDictionary<string, ImageSource?>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var com = CoInitializeEx(IntPtr.Zero, 2);
            try
            {
                lock (_gate)
                {
                    var live = entries.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var path in _cache.Keys.Where(path => !live.Contains(path)).ToArray()) _cache.Remove(path);
                    var snapshot = new Dictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in entries)
                    {
                        try
                        {
                            long modified, length;
                            if (metadata is not null && metadata.TryGetValue(entry.Path, out var stamp)) { modified = stamp.Modified; length = stamp.Length; }
                            else
                            {
                                var file = new FileInfo(entry.Path);
                                if (!file.Exists) { _cache.Remove(entry.Path); continue; }
                                modified = file.LastWriteTimeUtc.Ticks; length = file.Length;
                            }
                            if (force || !_cache.TryGetValue(entry.Path, out var cached) || cached.Modified != modified || cached.Length != length)
                            {
                                var icon = Extract(entry.Path);
                                byte[]? png = null;
                                if (icon is BitmapSource bitmap)
                                {
                                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                                    using var stream = new MemoryStream(); encoder.Save(stream); png = stream.ToArray();
                                }
                                cached = new Cached(modified, length, icon, png);
                                _cache[entry.Path] = cached;
                            }
                            snapshot[entry.Path] = cached.Icon;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _cache.Remove(entry.Path); }
                    }
                    completion.SetResult(snapshot);
                }
            }
            catch (Exception ex) { completion.SetException(ex); }
            finally { if (com >= 0) CoUninitialize(); }
        }) { IsBackground = true, Name = "Quickfetch icon cache" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
    public void Restore(IEnumerable<CachedShortcut> items)
    {
        lock (_gate)
        foreach (var item in items)
        {
            try
            {
                BitmapImage? bitmap = null;
                if (item.Icon is { Length: > 0 and < 100000 })
                {
                    using var stream = new MemoryStream(item.Icon);
                    bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 48; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                }
                _cache[item.Metadata.Path] = new(item.Metadata.Modified, item.Metadata.Length, bitmap, item.Icon);
            }
            catch (Exception ex) when (ex is IOException or FormatException or ArgumentException or NotSupportedException or InvalidOperationException) { }
        }
    }
    public byte[]? GetPng(string path) { lock (_gate) return _cache.TryGetValue(path, out var icon) ? icon.Png : null; }

    private ImageSource? Extract(string path)
    {
        ExtractionCount++;
        var info = new ShellFileInfo();
        try
        {
            // Shell resolves custom IconLocation and targets into the system image list.
            // Read the base image without overlay flags, avoiding the .lnk arrow badge.
            var imageList = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x4000);
            if (imageList == IntPtr.Zero) return null;
            info.Icon = ImageList_GetIcon(imageList, info.IconIndex, 1); // ILD_TRANSPARENT; no INDEXTOOVERLAYMASK.
            if (info.Icon == IntPtr.Zero) return null;
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(48, 48));
            bitmap.Freeze(); // Safe cross-thread sharing; one bitmap per cached shortcut.
            return bitmap;
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException) { return null; }
        finally { if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("comctl32.dll")] private static extern IntPtr ImageList_GetIcon(IntPtr imageList, int index, uint flags);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}

public sealed record ShortcutItem(SearchResult Match, ImageSource? Icon) : ILauncherItem
{
    public string Name => Match.Name;
    public string Path => Match.Path;
    public string Initial => Match.Initial;
    public string Location => Match.Location;
    public string MatchLabel => Match.Entry.IsRegisteredApp && Match.Tier == 6 ? Loc.T("注册应用") : Match.MatchLabel;
}
