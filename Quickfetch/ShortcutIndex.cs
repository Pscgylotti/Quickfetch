using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Quickfetch.Core;

namespace Quickfetch;

public sealed record ShortcutMetadata(string Name, string Path, long Modified, long Length)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public ShortcutEntry Entry => new(Name, Path);
}

public sealed class ShortcutIndex
{
    private readonly Dictionary<string, ShortcutMetadata> _items = new(StringComparer.OrdinalIgnoreCase);
    private string[] _roots = Array.Empty<string>();
    private bool _recursive;
    public long FullScanCount { get; private set; }
    public long IncrementalScanCount { get; private set; }
    public IReadOnlyDictionary<string, ShortcutMetadata> Items => _items;
    public void Configure(string[] roots, bool recursive)
    {
        _roots = roots.Where(root => !string.IsNullOrWhiteSpace(root)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _recursive = recursive;
    }
    public void Restore(IEnumerable<ShortcutMetadata> items)
    {
        _items.Clear();
        foreach (var item in items)
            if (IsWithinRoots(item.Path) && Path.GetExtension(item.Path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) _items[item.Path] = item;
    }
    public void FullScan()
    {
        FullScanCount++;
        var snapshot = new Dictionary<string, ShortcutMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in _roots) foreach (var item in Enumerate(root)) snapshot[item.Path] = item;
        _items.Clear(); foreach (var (path, item) in snapshot) _items[path] = item;
    }
    public bool CouldAffect(string path) => Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
        || _items.Keys.Any(file => file.StartsWith(Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) || Directory.Exists(path);
    public void Apply(IEnumerable<string> changes)
    {
        IncrementalScanCount++;
        foreach (var path in changes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsWithinRoots(path) && !_roots.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            var prefix = Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;
            foreach (var key in _items.Keys.Where(key => key.Equals(path, StringComparison.OrdinalIgnoreCase) || key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray()) _items.Remove(key);
            if (Directory.Exists(path))
            {
                if ((!_recursive && !_roots.Contains(path, StringComparer.OrdinalIgnoreCase)) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var item in Enumerate(path)) _items[item.Path] = item;
            }
            else if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                try { var info = new FileInfo(path); if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0) AddInfo(info, _items); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }
    private bool IsWithinRoots(string path) => _roots.Any(root => path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        && (_recursive || string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase)));
    private IEnumerable<ShortcutMetadata> Enumerate(string folder)
    {
        if (!Directory.Exists(folder)) yield break;
        var options = new EnumerationOptions { RecurseSubdirectories = _recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System, MatchCasing = MatchCasing.CaseInsensitive };
        var found = new Dictionary<string, ShortcutMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in new DirectoryInfo(folder).EnumerateFiles("*.lnk", options)) AddInfo(info, found);
        foreach (var item in found.Values) yield return item;
    }
    private static void AddInfo(FileInfo info, Dictionary<string, ShortcutMetadata> result)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(info.Name);
            if (SearchMatcher.Normalize(name).Length > 0) result[info.FullName] = new(name, info.FullName, info.LastWriteTimeUtc.Ticks, info.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
