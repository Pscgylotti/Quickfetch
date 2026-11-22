using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;

namespace Quickfetch;

internal static class PackagePreview
{
    private const int MaxXmlBytes = 256 * 1024, MaxTextLength = 4096;

    internal static bool Supports(string extension) => extension is
        ".docx" or ".docm" or ".pptx" or ".pptm" or ".xlsx" or ".xlsm" or ".odt" or ".ods" or ".odp"
        or ".zip" or ".jar" or ".apk" or ".cbz";

    internal static string Read(string path, string extension, CancellationToken cancellation)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!HasSmallDirectory(stream)) return "";
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var part = extension switch
            {
                ".docx" or ".docm" => "word/document.xml",
                ".pptx" or ".pptm" => "ppt/slides/slide1.xml",
                ".xlsx" or ".xlsm" => "xl/workbook.xml",
                ".odt" or ".ods" or ".odp" => "content.xml",
                _ => null
            };
            if (part is null)
            {
                var listing = new StringBuilder(Loc.T("压缩包目录"));
                foreach (var entry in zip.Entries.Take(40))
                {
                    cancellation.ThrowIfCancellationRequested();
                    listing.AppendLine().Append(entry.FullName.AsSpan(0, Math.Min(entry.FullName.Length, 240)));
                }
                if (zip.Entries.Count > 40) listing.AppendLine().Append('…');
                return listing.ToString();
            }
            if (zip.GetEntry(part) is not { } document) return "";
            return ReadDocument(document, part, cancellation);
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or NotSupportedException or EndOfStreamException)
        {
            return "";
        }
    }

    // ZipArchive materializes its central directory. Bound that allocation independently
    // of the size of the payload. Oversized, multi-disk and ZIP64 packages use Shell only.
    private static bool HasSmallDirectory(FileStream stream)
    {
        const int recordSize = 22;
        var tail = new byte[Math.Min(stream.Length, recordSize + ushort.MaxValue)];
        var start = stream.Seek(-tail.Length, SeekOrigin.End);
        stream.ReadExactly(tail);
        for (var i = tail.Length - recordSize; i >= 0; i--)
        {
            var record = tail.AsSpan(i);
            if (BinaryPrimitives.ReadUInt32LittleEndian(record) != 0x06054b50
                || BinaryPrimitives.ReadUInt16LittleEndian(record[20..]) != record.Length - recordSize) continue;
            var count = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            return BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(record[8..]) == count && count <= 4096
                && size <= 512 * 1024 && (long)offset + size == start + i;
        }
        return false;
    }

    private static string ReadDocument(ZipArchiveEntry part, string name, CancellationToken cancellation)
    {
        using var source = part.Open();
        var bytes = new byte[Math.Min(part.Length, MaxXmlBytes)];
        for (var position = 0; position < bytes.Length;)
        {
            cancellation.ThrowIfCancellationRequested();
            var read = source.Read(bytes, position, Math.Min(8192, bytes.Length - position));
            if (read == 0) throw new EndOfStreamException();
            position += read;
        }
        using var memory = new MemoryStream(bytes);
        using var reader = XmlReader.Create(memory, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var text = new StringBuilder();
        var sheets = name == "xl/workbook.xml";
        var openDocument = name == "content.xml";
        var contentDepth = -1;
        var truncated = part.Length > bytes.Length;
        try
        {
            while (text.Length < MaxTextLength && reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (sheets && reader.LocalName == "sheet" && reader.GetAttribute("name") is { } sheet)
                    {
                        if (text.Length == 0) text.AppendLine(Loc.T("工作表"));
                        text.AppendLine(sheet);
                    }
                    else if (!reader.IsEmptyElement && (openDocument ? reader.LocalName is "p" or "h" : reader.LocalName == "t"))
                        contentDepth = reader.Depth;
                    else if (reader.LocalName is "br" or "line-break") text.AppendLine();
                    else if (reader.LocalName == "tab") text.Append('\t');
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Depth == contentDepth) contentDepth = -1;
                    if (reader.LocalName is "p" or "h" && text.Length > 0) text.AppendLine();
                }
                else if (contentDepth >= 0 && reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace)
                    text.Append(reader.Value.AsSpan(0, Math.Min(MaxTextLength - text.Length, reader.Value.Length)));
            }
        }
        catch (XmlException) when (truncated && text.Length > 0) { /* The bounded prefix ended inside XML. */ }
        truncated |= text.Length >= MaxTextLength;
        var excerpt = text.ToString(0, Math.Min(text.Length, MaxTextLength)).Trim();
        return truncated && excerpt.Length > 0 ? excerpt + "\n…" : excerpt;
    }
}
