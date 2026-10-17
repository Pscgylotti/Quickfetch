using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Quickfetch.Core;

namespace Quickfetch;

public static class ShortcutCatalog
{
    public static bool IsAllowed(ShortcutEntry entry, IEnumerable<string> roots, bool recursive) => roots.Any(root => IsAllowed(entry, root, recursive));
    public static IReadOnlyList<ShortcutEntry> Read(string folder, bool recursive)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("找不到快捷方式文件夹。请点击右上角设置重新选择。");
        var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System, MatchCasing = MatchCasing.CaseInsensitive };
        return Directory.EnumerateFiles(folder, "*.lnk", options)
            .Select(path => new ShortcutEntry(Path.GetFileNameWithoutExtension(path), Path.GetFullPath(path)))
            .Where(entry => SearchMatcher.Normalize(entry.Name).Length > 0)
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsAllowed(ShortcutEntry entry, string folder, bool recursive)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(entry.Path);
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Path.GetExtension(candidate).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return false;
        if (!recursive && !Path.GetDirectoryName(candidate)!.Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase)) return false;
        // Junctions/symlinks are never followed, including a shortcut swapped since enumeration.
        var info = new FileInfo(candidate);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        for (var parent = info.Directory; parent is not null && parent.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase); parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }
}
