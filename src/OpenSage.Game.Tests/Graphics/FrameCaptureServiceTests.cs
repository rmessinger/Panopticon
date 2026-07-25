using System.IO;
using OpenSage.Graphics;
using Xunit;

namespace OpenSage.Tests.Graphics;

public class FrameCaptureServiceTests
{
    [Fact]
    public void ClearCaptureDirectory_DeletesAllFiles()
    {
        Directory.CreateDirectory(FrameCaptureService.CaptureDirectory);
        var dummyA = Path.Combine(FrameCaptureService.CaptureDirectory, "capture_0001.png");
        var dummyB = Path.Combine(FrameCaptureService.CaptureDirectory, "capture_0002.png");
        File.WriteAllText(dummyA, "dummy");
        File.WriteAllText(dummyB, "dummy");

        FrameCaptureService.ClearCaptureDirectory();

        Assert.False(File.Exists(dummyA));
        Assert.False(File.Exists(dummyB));
    }

    [Fact]
    public void ClearCaptureDirectory_IsSafeWhenDirectoryMissing()
    {
        if (Directory.Exists(FrameCaptureService.CaptureDirectory))
        {
            Directory.Delete(FrameCaptureService.CaptureDirectory, recursive: true);
        }

        FrameCaptureService.ClearCaptureDirectory();

        Assert.False(Directory.Exists(FrameCaptureService.CaptureDirectory));
    }

    [Fact]
    public void Dispose_IsIdempotent_WhenNothingCaptured()
    {
        using (var service = new FrameCaptureService())
        {
            service.Dispose();
        }
    }
}
