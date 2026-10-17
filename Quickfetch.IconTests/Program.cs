using System.IO;
using System.Runtime.InteropServices;
using Quickfetch;
using Quickfetch.Core;

var passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }
var folder = Path.Combine(Path.GetTempPath(), "QuickfetchIconTests-" + Guid.NewGuid());
Directory.CreateDirectory(folder);
var path = Path.Combine(folder, "Custom Icon.lnk");
object? shell = null, shortcut = null;
try
{
    shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
    shortcut = ((dynamic)shell!).CreateShortcut(path);
    ((dynamic)shortcut).TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
    ((dynamic)shortcut).IconLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll") + ",3";
    ((dynamic)shortcut).Save();
    Marshal.FinalReleaseComObject(shortcut); shortcut = null;
    Marshal.FinalReleaseComObject(shell); shell = null;
    var cache = new ShortcutIconCache();
    var entries = new[] { new ShortcutEntry("Custom Icon", path) };
    var first = await cache.LoadAsync(entries);
    Check(first[path] is { IsFrozen: true }, "A custom .lnk icon must load and be frozen for sharing.");
    Check(cache.ExtractionCount == 1, "First load should extract exactly once.");
    var second = await cache.LoadAsync(entries);
    Check(ReferenceEquals(first[path], second[path]) && cache.ExtractionCount == 1, "Unchanged links must reuse the same in-memory bitmap.");
    File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
    var changed = await cache.LoadAsync(entries);
    Check(!ReferenceEquals(first[path], changed[path]) && cache.ExtractionCount == 2, "Modified links must invalidate cached icons.");
    await cache.LoadAsync(entries, force: true);
    Check(cache.ExtractionCount == 3, "Manual refresh must re-extract unchanged links.");
    Check((await cache.LoadAsync(Array.Empty<ShortcutEntry>())).Count == 0, "Removed links must disappear from the cache snapshot.");
    await cache.LoadAsync(entries);
    Check(cache.ExtractionCount == 4, "A removed and re-added link must not reuse a stale cached bitmap.");
    var index = new ShortcutIndex();
    index.Configure(new[] { folder }, true); index.FullScan();
    Check(index.Items.Count == 1 && index.FullScanCount == 1, "Initial reconciliation must enumerate the fixture once.");
    var disk = new ShortcutDiskCache(Path.Combine(folder, "cache.json"));
    Check(disk.Save(new[] { folder }, true, index.Items, cache), "The first cache must persist catalog metadata and icons.");
    Check(!disk.Save(new[] { folder }, true, index.Items, cache), "Identical state must not rewrite the cache file.");
    var document = disk.Load(new[] { folder }, true)!;
    var warm = new ShortcutIconCache(); warm.Restore(document.Items);
    var warmIcons = await warm.LoadAsync(entries, metadata: index.Items);
    Check(warm.ExtractionCount == 0 && warmIcons[path] is { IsFrozen: true }, "A warm restart must reuse packed icons without Shell extraction.");
    var added = Path.Combine(folder, "Added.lnk"); File.Copy(path, added);
    index.Apply(new[] { added });
    await warm.LoadAsync(index.Items.Values.Select(item => item.Entry).ToArray(), metadata: index.Items);
    Check(index.Items.Count == 2 && index.FullScanCount == 1 && warm.ExtractionCount == 1, "An added shortcut must update incrementally and extract only its own icon.");
    File.Delete(added); index.Apply(new[] { added });
    Check(index.Items.Count == 1 && index.FullScanCount == 1, "Deleting a shortcut must not scan the whole root.");
    var group = Path.Combine(folder, "Group"); Directory.CreateDirectory(group); File.Copy(path, Path.Combine(group, "Nested.lnk"));
    index.Apply(new[] { group });
    var moved = Path.Combine(folder, "Moved"); Directory.Move(group, moved); index.Apply(new[] { group, moved });
    Check(index.Items.Count == 2 && index.Items.Keys.Any(key => key.Contains("Moved")) && !index.Items.Keys.Any(key => key.Contains("Group")), "Folder moves must reconcile only their affected subtrees.");
    index.Configure(new[] { folder }, false); index.FullScan(); index.Apply(new[] { Path.Combine(moved, "Nested.lnk") });
    Check(index.Items.Count == 1, "Nonrecursive indexing must ignore nested change notifications.");
    var chinese = Path.Combine(folder, "微信.lnk"); File.Copy(path, chinese);
    index.Apply(new[] { chinese });
    Check(index.Items.ContainsKey(chinese) && SearchMatcher.Search(index.Items.Values.Select(item => item.Entry), "wx").Any(result => result.Path == chinese && result.Tier == 2), "Incremental indexing must retain Chinese-only shortcut names and expose pinyin initials.");
    Check(ShortcutCatalog.Read(folder, false).Any(entry => entry.Name == "微信"), "Legacy catalog reads must also retain Chinese-only names.");
    File.Delete(chinese); index.Apply(new[] { chinese });
    Check(disk.Load(new[] { Path.Combine(folder, "Other") }, true) is null, "Caches for another root must not be restored.");
    File.Delete(path);
    Check((await cache.LoadAsync(entries)).Count == 0, "A link removed during loading must be skipped.");
    var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
    var real = (Directory.Exists(startMenu) ? Directory.GetFiles(startMenu, "*.lnk", SearchOption.AllDirectories) : Array.Empty<string>())
        .Select(p => new ShortcutEntry(Path.GetFileNameWithoutExtension(p), p)).ToArray();
    var icons = await cache.LoadAsync(real);
    var loaded = icons.Values.Count(icon => icon is not null);
    Check(real.Length == 0 || loaded > 0, "A populated Start Menu must yield application icons.");
    Console.WriteLine($"PASS: {passed} icon/cache checks; {loaded}/{real.Length} real icons loaded.");
}
finally
{
    if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
    if (shell is not null) Marshal.FinalReleaseComObject(shell);
    Directory.Delete(folder, recursive: true);
}
