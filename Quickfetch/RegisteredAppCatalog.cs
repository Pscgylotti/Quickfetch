using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Quickfetch.Core;

namespace Quickfetch;

// Enumerated on an STA, never while typing. Stable AUMIDs survive package upgrades.
internal sealed class RegisteredAppCatalog
{
    internal sealed record CachedApp(string Name, string Id, byte[]? Icon);
    internal sealed record Snapshot(ShortcutEntry[] Entries, IReadOnlyDictionary<string, ImageSource?> Icons,
        IReadOnlyDictionary<string, string> ShortcutIds);
    private readonly string _file;
    private readonly Dictionary<string, CachedApp> _cache = new(StringComparer.OrdinalIgnoreCase);
    private Snapshot? _snapshot;
    private DateTimeOffset _lastScan;
    internal bool NeedsRefresh => DateTimeOffset.UtcNow - _lastScan > TimeSpan.FromMinutes(5);
    internal RegisteredAppCatalog(string? file = null) => _file = file ?? Path.Combine(Settings.Storage, "registered-apps-v1.json");
    internal static bool IsAppId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Contains('!')
        && id.Length < 512 && !id.Any(c => char.IsControl(c) || c is '\\' or '/' or '"');
    internal static ShortcutEntry Entry(string name, string id) => new(name, "shell:AppsFolder\\" + id);
    internal static ShortcutEntry[] Merge(IEnumerable<ShortcutEntry> shortcuts, IEnumerable<ShortcutEntry> apps, IReadOnlyDictionary<string, string> ids)
    {
        var links = shortcuts.ToArray();
        var linkedIds = links.Select(e => ids.GetValueOrDefault(e.Path)).Where(id => id is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return links.Concat(apps.Where(e => !linkedIds.Contains(e.AppUserModelId)))
            .DistinctBy(e => e.Path, StringComparer.OrdinalIgnoreCase).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal Task<Snapshot> LoadAsync(IReadOnlyList<ShortcutEntry> links, bool force)
    {
        var completion = new TaskCompletionSource<Snapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Read(links, force)); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "Quickfetch registered apps" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private Snapshot Read(IReadOnlyList<ShortcutEntry> links, bool force)
    {
        if (_cache.Count == 0)
        {
            try
            {
                if (File.Exists(_file) && new FileInfo(_file).Length < 16 * 1024 * 1024)
                    foreach (var app in JsonSerializer.Deserialize<CachedApp[]>(File.ReadAllText(_file)) ?? [])
                        if (app is not null && IsAppId(app.Id) && !string.IsNullOrWhiteSpace(app.Name)) _cache[app.Id] = app;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        object? shell = null, folder = null, items = null;
        var shortcutIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            // Resolve shortcut identities without deduplicating by display name (names can collide).
            foreach (var group in links.GroupBy(e => Path.GetDirectoryName(e.Path)))
            {
                object? parent = null;
                try
                {
                    parent = ((dynamic)shell!).NameSpace(group.Key);
                    if (parent is null) continue;
                    foreach (var link in group)
                    {
                        object? item = null;
                        try
                        {
                            item = ((dynamic)parent).ParseName(Path.GetFileName(link.Path));
                            if (item is null) continue;
                            var id = ((dynamic)item).ExtendedProperty("System.AppUserModel.ID") as string;
                            if (IsAppId(id)) shortcutIds[link.Path] = id!;
                        }
                        catch (COMException) { }
                        finally { Release(item); }
                    }
                }
                catch (COMException) { }
                finally { Release(parent); }
            }
            if (_snapshot is not null && !force && !NeedsRefresh) return _snapshot with { ShortcutIds = shortcutIds };
            folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            if (folder is null) throw new IOException("Windows 应用目录不可用。");
            items = ((dynamic)folder).Items();
            var found = new List<CachedApp>();
            var icons = new Dictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < (int)((dynamic)items).Count; i++)
            {
                object? item = null;
                try
                {
                    item = ((dynamic)items).Item(i);
                    string id = ((dynamic)item!).Path;
                    string name = ((dynamic)item).Name;
                    if (!IsAppId(id) || !SearchMatcher.HasSearchableName(name)) continue;
                    var cached = _cache.GetValueOrDefault(id);
                    ImageSource? icon = !force && cached?.Name == name ? Decode(cached.Icon) : null;
                    icon ??= Extract(Entry(name, id).Path);
                    found.Add(new(name, id, Encode(icon)));
                    icons[Entry(name, id).Path] = icon;
                }
                catch (Exception ex) when (ex is COMException or ArgumentException) { }
                finally { Release(item); }
            }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(found.OrderBy(a => a.Id).ToArray());
            try
            {
                if (bytes.Length < 16 * 1024 * 1024 && (!File.Exists(_file) || !File.ReadAllBytes(_file).AsSpan().SequenceEqual(bytes)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                    File.WriteAllBytes(_file + ".tmp", bytes); File.Move(_file + ".tmp", _file, true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            _cache.Clear(); foreach (var app in found) _cache[app.Id] = app;
            _lastScan = DateTimeOffset.UtcNow;
            return _snapshot = new(found.Select(a => Entry(a.Name, a.Id)).ToArray(), icons, shortcutIds);
        }
        finally { Release(items); Release(folder); Release(shell); }
    }
    private static void Release(object? obj) { if (obj is not null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
    private static BitmapSource? Decode(byte[]? png)
    {
        if (png is not { Length: > 0 and < 100000 }) return null;
        try
        {
            using var stream = new MemoryStream(png);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or FormatException or InvalidOperationException) { return null; }
    }
    private static byte[]? Encode(ImageSource? image)
    {
        if (image is not BitmapSource bitmap) return null;
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    internal static ImageSource? Extract(string path, uint flags = 0x104, int width = 48, int height = 48)
    {
        object? factory = null; IntPtr bitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IImageFactory).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory));
            ((IImageFactory)factory).GetImage(new SizeInt { X = width, Y = height }, flags, out bitmap);
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze(); return image;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException) { return null; }
        finally { if (bitmap != IntPtr.Zero) DeleteObject(bitmap); Release(factory); }
    }
    internal static void Activate(string id)
    {
        if (!IsAppId(id)) throw new ArgumentException("无效的 Windows 应用标识。");
        var manager = (IActivationManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"))!)!;
        try { manager.ActivateApplication(id, null, 0, out _); }
        finally { Release(manager); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct SizeInt { public int X, Y; }
    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageFactory { void GetImage(SizeInt size, uint flags, out IntPtr bitmap); }
    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivationManager
    {
        void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.LPWStr)] string? arguments, uint options, out uint processId);
        void ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);
        void ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr items, out uint processId);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
