namespace Quickfetch.Core;

public sealed record FileEntry(string Name, string Directory, string Path, bool IsFolder, long? Size, DateTimeOffset? Modified)
{
    public string SizeText => IsFolder ? "文件夹" : Size is null ? "—" : FormatSize(Size.Value);
    public string DateText => Modified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
    public static string FormatSize(long bytes)
    {
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var amount = (double)bytes; var unit = 0;
        while (amount >= 1024 && unit < units.Length - 1) { amount /= 1024; unit++; }
        return $"{amount:0.##} {units[unit]}";
    }
}

public sealed record FileSearchPage(IReadOnlyList<FileEntry> Entries, uint Total, uint Sort);
