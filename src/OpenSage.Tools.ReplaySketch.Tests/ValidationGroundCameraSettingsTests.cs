using System;
using System.IO;
using OpenSage.Tools.TestHarness;
using Xunit;

namespace OpenSage.Tools.ReplaySketch.Tests;

public class ValidationGroundCameraSettingsTests
{
    [Fact]
    public void SaveAndLoad_PreserveIndependentCameraSpeeds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vg-camera-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Equal(1f, ValidationGroundCameraSettings.Load(path).HorizontalSpeed);

            new ValidationGroundCameraSettings { HorizontalSpeed = 0.5f, VerticalSpeed = 2.5f }.Save(path);
            var loaded = ValidationGroundCameraSettings.Load(path);

            Assert.Equal(0.5f, loaded.HorizontalSpeed);
            Assert.Equal(2.5f, loaded.VerticalSpeed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsOutOfRangeSpeeds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vg-camera-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"HorizontalSpeed\":0,\"VerticalSpeed\":2}");
            Assert.Throws<InvalidDataException>(() => ValidationGroundCameraSettings.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
