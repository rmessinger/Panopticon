using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Panopticon.FileFormats.Casc;
using Panopticon.IO;
using Xunit;

namespace Panopticon.Tests.Data.Casc;

public sealed class CascFileSystemTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "panopticon-casc-fs-" + Guid.NewGuid().ToString("N"));

    public CascFileSystemTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void SyntheticInstallReadsFilesAndListsDirectories()
    {
        var file1 = CascKey.Parse("11111111111111111111111111111111");
        var file2 = CascKey.Parse("22222222222222222222222222222222");
        BuildInstall(
            [
                ("mods/a.sc2mod/base.sc2assets/assets/a.txt", file1, "alpha"u8.ToArray()),
                ("mods/a.sc2mod/base.sc2assets/assets/folder/b.bin", file2, "beta"u8.ToArray())
            ]);

        using var fileSystem = new CascFileSystem(_tempDirectory);
        var entry = fileSystem.GetFile("MODS/A.SC2MOD/BASE.SC2ASSETS/ASSETS/A.TXT");

        Assert.NotNull(entry);
        using (var reader = new StreamReader(entry.Open()))
        {
            Assert.Equal("alpha", reader.ReadToEnd());
        }

        Assert.Equal(
            ["a.txt"],
            fileSystem.GetFilesInDirectory("mods/a.sc2mod/base.sc2assets/assets", "*.txt")
                .Select(e => Path.GetFileName(e.FilePath)));
        Assert.Equal(
            2,
            fileSystem.GetFilesInDirectory("MODS/A.SC2MOD/BASE.SC2ASSETS", "*", SearchOption.AllDirectories).Count());
    }

    private void BuildInstall((string Path, CascKey ContentKey, byte[] Bytes)[] files)
    {
        var buildKey = "00112233445566778899aabbccddeeff";
        var encodingContentKey = CascKey.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var encodingEncodingKey = CascKey.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var rootContentKey = CascKey.Parse("cccccccccccccccccccccccccccccccc");
        var rootEncodingKey = CascKey.Parse("dddddddddddddddddddddddddddddddd");

        WriteBuildInfo(buildKey, rootContentKey, encodingContentKey, encodingEncodingKey);

        var dataDirectory = Path.Combine(_tempDirectory, "SC2Data", "data");
        Directory.CreateDirectory(dataDirectory);

        var encodingKeys = files
            .Select((file, index) => CascKey.Parse($"{index + 1:x2}eeeeeeeeeeeeeeeeeeeeeeeeeeeeee"))
            .ToArray();
        var root = BuildMndxRoot(files);
        var encoding = BuildEncoding(
            files.Select((file, index) => (file.ContentKey, (long)file.Bytes.Length, encodingKeys[index]))
                .Append((rootContentKey, (long)root.Length, rootEncodingKey))
                .ToArray());

        using var data = new MemoryStream();
        var spans = new (CascKey EncodingKey, long Offset, int Size)[files.Length + 2];
        spans[0] = WriteDataSpan(data, encodingEncodingKey, encoding);
        spans[1] = WriteDataSpan(data, rootEncodingKey, root);
        for (var i = 0; i < files.Length; i++)
        {
            spans[i + 2] = WriteDataSpan(data, encodingKeys[i], files[i].Bytes);
        }

        File.WriteAllBytes(Path.Combine(dataDirectory, "data.000"), data.ToArray());
        WriteIndex(dataDirectory, spans);
    }

    private void WriteBuildInfo(string buildKey, CascKey rootContentKey, CascKey encodingContentKey, CascKey encodingEncodingKey)
    {
        File.WriteAllText(
            Path.Combine(_tempDirectory, ".build.info"),
            "Active!DEC:1|Build Key!HEX:16\n" +
            $"1|{buildKey}\n");
        var configDirectory = Path.Combine(_tempDirectory, "SC2Data", "config", buildKey[..2], buildKey[2..4]);
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(
            Path.Combine(configDirectory, buildKey),
            $"root = {rootContentKey}\n" +
            $"encoding = {encodingContentKey} {encodingEncodingKey}\n" +
            "build-uid = s2\n");
    }

    private static (CascKey EncodingKey, long Offset, int Size) WriteDataSpan(Stream data, CascKey encodingKey, byte[] decoded)
    {
        var offset = data.Position;
        var span = BuildBlteSpan(decoded);
        data.Write(span);
        return (encodingKey, offset, span.Length);
    }

    private static byte[] BuildBlteSpan(byte[] decoded)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[30]);
        stream.Write("BLTE"u8);
        Span<byte> headerSize = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(headerSize, 0);
        stream.Write(headerSize);
        stream.WriteByte((byte)'N');
        stream.Write(decoded);
        return stream.ToArray();
    }

    private static byte[] BuildEncoding((CascKey ContentKey, long Size, CascKey EncodingKey)[] entries)
    {
        using var stream = new MemoryStream();
        stream.Write("EN"u8);
        stream.WriteByte(1);
        stream.WriteByte(16);
        stream.WriteByte(16);
        Span<byte> header = stackalloc byte[17];
        BinaryPrimitives.WriteUInt16BigEndian(header[..2], 1);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..4], 1);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..8], 1);
        BinaryPrimitives.WriteUInt32BigEndian(header[8..12], 0);
        BinaryPrimitives.WriteUInt32BigEndian(header[13..17], 0);
        stream.Write(header);
        stream.Write(new byte[32]);

        var page = new byte[1024];
        var offset = 0;
        foreach (var entry in entries)
        {
            page[offset++] = 1;
            WriteUInt40BigEndian(page.AsSpan(offset, 5), entry.Size);
            offset += 5;
            entry.ContentKey.WriteTo(page.AsSpan(offset, CascKey.Length));
            offset += CascKey.Length;
            entry.EncodingKey.WriteTo(page.AsSpan(offset, CascKey.Length));
            offset += CascKey.Length;
        }

        stream.Write(page);
        return stream.ToArray();
    }

    private static byte[] BuildMndxRoot((string Path, CascKey ContentKey, byte[] Bytes)[] files)
    {
        const string Package = "mods/a.sc2mod/base.sc2assets";
        var names = files.Select(static f => f.Path["mods/a.sc2mod/base.sc2assets/".Length..]).ToArray();
        var packagesTrie = MarTestTrieBuilder.BuildTailTrie([Package]);
        var namesTrie = MarTestTrieBuilder.BuildTailTrie(names);
        var fullTrie = MarTestTrieBuilder.BuildTailTrie(files.Select(static f => f.Path).ToArray());
        var mars = new[] { packagesTrie, namesTrie, fullTrie };

        using var stream = new MemoryStream();
        stream.Write(new byte[48]);
        stream.Write(new byte[mars.Length * 20]);
        var contentEntryOffset = checked((int)stream.Position);
        for (var i = 0; i < files.Length; i++)
        {
            WriteCKeyEntry(stream, 0x80000000, files[i].ContentKey, files[i].Bytes.Length);
        }

        var marOffsets = new int[mars.Length];
        for (var i = 0; i < mars.Length; i++)
        {
            marOffsets[i] = checked((int)stream.Position);
            stream.Write(mars[i]);
        }

        var data = stream.ToArray();
        "MNDX"u8.CopyTo(data.AsSpan(0, 4));
        WriteInt32(data, 4, 2);
        WriteInt32(data, 8, 2);
        WriteInt32(data, 12, 97563);
        WriteInt32(data, 16, 97563);
        WriteInt32(data, 20, 48);
        WriteInt32(data, 24, mars.Length);
        WriteInt32(data, 28, 20);
        WriteInt32(data, 32, contentEntryOffset);
        WriteInt32(data, 36, files.Length);
        WriteInt32(data, 40, files.Length);
        WriteInt32(data, 44, 24);
        for (var i = 0; i < mars.Length; i++)
        {
            var offset = 48 + i * 20;
            WriteInt32(data, offset, i);
            WriteInt32(data, offset + 4, mars[i].Length);
            WriteInt32(data, offset + 12, marOffsets[i]);
        }

        return data;
    }

    private static void WriteCKeyEntry(Stream stream, uint flags, CascKey key, int size)
    {
        Span<byte> entry = stackalloc byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(entry[..4], flags);
        key.WriteTo(entry[4..20]);
        BinaryPrimitives.WriteInt32LittleEndian(entry[20..24], size);
        stream.Write(entry);
    }

    private static void WriteIndex(string dataDirectory, (CascKey EncodingKey, long Offset, int Size)[] entries)
    {
        using var stream = File.Create(Path.Combine(dataDirectory, "0000000001.idx"));
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header[..4], 8);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..10], 7);
        header[12] = 4;
        header[13] = 5;
        header[14] = 9;
        header[15] = 30;
        stream.Write(header);

        Span<byte> entriesHeader = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(entriesHeader[..4], (uint)(entries.Length * 18));
        stream.Write(entriesHeader);

        Span<byte> keyBytes = stackalloc byte[CascKey.Length];
        Span<byte> record = stackalloc byte[18];
        foreach (var entry in entries)
        {
            record.Clear();
            entry.EncodingKey.WriteTo(keyBytes);
            keyBytes[..9].CopyTo(record);
            WriteUInt40BigEndian(record[9..14], entry.Offset);
            BinaryPrimitives.WriteInt32LittleEndian(record[14..18], entry.Size);
            stream.Write(record);
        }
    }

    private static void WriteUInt40BigEndian(Span<byte> destination, long value)
    {
        for (var i = 4; i >= 0; i--)
        {
            destination[i] = (byte)value;
            value >>= 8;
        }
    }

    private static void WriteInt32(byte[] data, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }
}
