using System.IO;
using System.Text;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

/// <summary>
/// WI-A5-28: the editor's new-map flow produces a map that saves without errors into a
/// structurally sound map file (full parse round-trip lives in Simulation.Tests, which can
/// reach the internal parser).
/// </summary>
public sealed class NewMapFactoryTests
{
    [Fact]
    public void NewMap_SavesNonEmptyMapFile()
    {
        var editorMap = NewMapFactory.Create(new NewMapParameters
        {
            PlayableWidth = 32,
            PlayableHeight = 32,
            InitialHeight = 10,
        });

        var path = Path.Combine(Path.GetTempPath(), $"panopticon_newmap_{System.Guid.NewGuid():N}.map");

        try
        {
            editorMap.SaveAs(path);

            Assert.True(new FileInfo(path).Length > 0);

            // The seven unconditional chunks are serialized: the file starts with the
            // uncompressed-map FourCC.
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            Assert.Equal("CkMp", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
