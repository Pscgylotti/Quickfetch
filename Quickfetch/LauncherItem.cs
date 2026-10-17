using System;
using Quickfetch.Core;

namespace Quickfetch;

public interface ILauncherItem
{
    string Name { get; }
    string Path { get; }
}
public sealed record FileItem(FileEntry File) : ILauncherItem
{
    public string Name => File.Name;
    public string Path => File.Path;
    public string Location => File.Directory;
    public string SizeText => File.SizeText;
    public string DateText => File.DateText;
}
