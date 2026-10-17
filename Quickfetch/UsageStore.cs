using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Quickfetch.Core;

namespace Quickfetch;

public sealed class UsageStore
{
    private readonly string _file;
    public Dictionary<string, AppUsage> History { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public UsageStore(string? file = null)
    {
        _file = file ?? Path.Combine(Settings.Storage, "usage.json");
        try { History = new(JsonSerializer.Deserialize<Dictionary<string, AppUsage>>(File.ReadAllText(_file)) ?? new(), StringComparer.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }
    public void Record(string path)
    {
        History[path] = UsageRanking.Record(History.GetValueOrDefault(path), DateTimeOffset.UtcNow);
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temporary = _file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(History));
        File.Move(temporary, _file, true);
    }
}
