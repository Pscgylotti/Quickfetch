using System.Buffers.Binary;
using System.Text;

namespace Quickfetch.Core;

// Everything SDK IPC Query2, Unicode. No DLL, HTTP server, or filesystem crawling.
public static class EverythingProtocol
{
    public const uint RequestFlags = 0x53; // Name, path, size, date modified.
    public static byte[] BuildQuery(uint hwnd, uint replyId, string query, uint sort, uint maximum)
    {
        var text = Encoding.Unicode.GetBytes(query + '\0');
        var data = new byte[28 + text.Length];
        uint[] fields = [hwnd, replyId, 0, 0, maximum, RequestFlags, sort];
        for (var i = 0; i < fields.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4), fields[i]);
        text.CopyTo(data, 28);
        return data;
    }
    public static FileSearchPage Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 20) throw new InvalidDataException("Everything 返回数据不完整。");
        var total = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        var sort = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
        if (flags != RequestFlags || count > (data.Length - 20) / 8) throw new InvalidDataException("Everything 返回数据格式不匹配。");
        var entries = new List<FileEntry>();
        for (var i = 0; i < count; i++)
        {
            var flagsItem = BinaryPrimitives.ReadUInt32LittleEndian(data[(20 + i * 8)..]);
            var offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[(24 + i * 8)..]));
            if (offset < 20 + count * 8 || offset > data.Length) throw new InvalidDataException("Everything 返回了无效偏移。");
            var name = ReadString(data, ref offset);
            var directory = ReadString(data, ref offset);
            if (offset > data.Length - 16) throw new InvalidDataException("Everything 文件信息不完整。");
            var size = BinaryPrimitives.ReadInt64LittleEndian(data[offset..]); offset += 8;
            var time = BinaryPrimitives.ReadInt64LittleEndian(data[offset..]);
            DateTimeOffset? modified = null;
            try { if (time > 0) modified = new DateTimeOffset(DateTime.FromFileTimeUtc(time)); } catch (ArgumentOutOfRangeException) { }
            var path = System.IO.Path.Combine(directory, name);
            if ((flagsItem & 2) != 0 && path.EndsWith(':')) path += System.IO.Path.DirectorySeparatorChar;
            entries.Add(new(name, directory, path, (flagsItem & 3) != 0, size < 0 ? null : size, modified));
        }
        return new(entries, total, sort);
    }
    private static string ReadString(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset < 0 || offset > data.Length - 4) throw new InvalidDataException("Everything 字符串不完整。");
        var chars = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]); offset += 4;
        if (chars > (data.Length - offset) / 2 - 1) throw new InvalidDataException("Everything 字符串长度无效。");
        var length = checked((int)chars * 2);
        var value = Encoding.Unicode.GetString(data.Slice(offset, length));
        if (data[offset + length] != 0 || data[offset + length + 1] != 0) throw new InvalidDataException("Everything 字符串缺少结束符。");
        offset += length + 2;
        return value;
    }
}
