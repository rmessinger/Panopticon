using System;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// Thrown when a GR2 section uses an unsupported compression format.
/// </summary>
public sealed class GrannyCompressedSectionException : Exception
{
    public GrannyCompressedSectionException(string? filePath, int sectionIndex, uint compression)
        : base(ComposeMessage(filePath, sectionIndex, compression))
    {
        FilePath = filePath;
        SectionIndex = sectionIndex;
        Compression = compression;
    }

    public string? FilePath { get; }

    public int SectionIndex { get; }

    public uint Compression { get; }

    private static string ComposeMessage(string? filePath, int sectionIndex, uint compression)
    {
        var name = DescribeCompression(compression);
        var file = filePath is null ? "GR2 stream" : $"GR2 file '{filePath}'";
        return $"{file} section {sectionIndex} uses compression {compression} ({name}); "
            + "this compression format is not supported.";
    }

    private static string DescribeCompression(uint compression)
    {
        return compression switch
        {
            2 => "Oodle1",
            4 => "BitKnit2",
            _ => "unknown",
        };
    }
}
