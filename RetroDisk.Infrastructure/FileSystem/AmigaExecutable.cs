using System.Buffers.Binary;

namespace AmiDiskLab.Infrastructure.FileSystem;

internal static class AmigaExecutable
{
    internal static async Task<bool> IsHunkFileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous);
        if (await ReadLongwordAsync(stream, token) != 0x3F3) return false; // HUNK_HEADER
        uint? nameLength;
        while ((nameLength = await ReadLongwordAsync(stream, token)) is not null)
        {
            if (nameLength == 0) break;
            if (nameLength > 1024 || stream.Position + nameLength.Value * 4L > stream.Length)
                return false;
            stream.Position += nameLength.Value * 4L;
        }
        if (nameLength is null) return false;
        var tableSize = await ReadLongwordAsync(stream, token);
        var first = await ReadLongwordAsync(stream, token);
        var last = await ReadLongwordAsync(stream, token);
        if (tableSize is null or 0 or > 4096 || first is null || last is null ||
            first > last || last >= tableSize || stream.Position + tableSize.Value * 4L > stream.Length)
            return false;
        stream.Position += tableSize.Value * 4L;
        var kind = await ReadLongwordAsync(stream, token);
        return kind is 0x3E9 or 0x3EA or 0x3EB; // CODE, DATA or BSS
    }

    private static async Task<uint?> ReadLongwordAsync(Stream stream, CancellationToken token)
    {
        var value = new byte[4];
        if (stream.Length - stream.Position < value.Length) return null;
        await stream.ReadExactlyAsync(value, token);
        return BinaryPrimitives.ReadUInt32BigEndian(value);
    }
}
