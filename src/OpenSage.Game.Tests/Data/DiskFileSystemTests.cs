using System.IO;
using OpenSage.IO;
using Xunit;

namespace OpenSage.Tests.Data;

public class DiskFileSystemTests
{
    [Fact]
    public void GetFileMatchesPathCasingInsensitive()
    {
        var root = Path.Combine(Path.GetTempPath(), "opensage_diskfilesystem_tests");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Art", "W3D"));
            File.WriteAllText(Path.Combine(root, "Art", "W3D", "ModelName.W3D"), "contents");

            var fileSystem = new DiskFileSystem(root);

            Assert.NotNull(fileSystem.GetFile("Art/W3D/ModelName.W3D"));
            Assert.NotNull(fileSystem.GetFile("art/w3d/ModelName.w3d"));
            Assert.NotNull(fileSystem.GetFile("ART\\W3D\\MODELNAME.W3D"));
            Assert.Null(fileSystem.GetFile("art/w3d/OtherModel.w3d"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
