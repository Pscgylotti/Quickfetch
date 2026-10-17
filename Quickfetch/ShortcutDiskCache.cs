using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Quickfetch;

public sealed record CachedShortcut(ShortcutMetadata Metadata, byte[]? Icon);
public sealed record ShortcutCacheDocument(int Version, string[] Roots, bool Recursive, CachedShortcut[] Items);

public sealed class ShortcutDiskCache
{
    private readonly string _file;
    private byte[]? _lastHash;
    public ShortcutDiskCache(string? file = null) => _file = file ?? Path.Combine(Settings.Storage, "shortcuts-v1.json");
    public ShortcutCacheDocument? Load(string[] roots, bool recursive)
    {
        try
        {
            var info = new FileInfo(_file);
            if (!info.Exists || info.Length > 32 * 1024 * 1024) return null;
            var bytes = File.ReadAllBytes(_file);
            var cache = JsonSerializer.Deserialize<ShortcutCacheDocument>(bytes);
            if (cache is not { Version: 1 } || cache.Roots is null || cache.Items is null || cache.Items.Any(item => item is null || item.Metadata is null || string.IsNullOrWhiteSpace(item.Metadata.Path) || string.IsNullOrWhiteSpace(item.Metadata.Name))
                || cache.Recursive != recursive || !cache.Roots.SequenceEqual(roots, StringComparer.OrdinalIgnoreCase) || cache.Items.Length > 10000) return null;
            _lastHash = System.Security.Cryptography.SHA256.HashData(bytes);
            return cache;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }
    public bool Save(string[] roots, bool recursive, IReadOnlyDictionary<string, ShortcutMetadata> items, ShortcutIconCache icons)
    {
        var records = items.Values.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(item => new CachedShortcut(item, icons.GetPng(item.Path))).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ShortcutCacheDocument(1, roots, recursive, records));
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        if (_lastHash is not null && hash.AsSpan().SequenceEqual(_lastHash)) return false;
        if (bytes.Length > 32 * 1024 * 1024) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temporary = _file + ".tmp";
        File.WriteAllBytes(temporary, bytes); File.Move(temporary, _file, true);
        _lastHash = hash;
        return true;
    }
}
