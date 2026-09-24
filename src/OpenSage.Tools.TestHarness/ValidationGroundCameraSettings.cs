using System;
using System.IO;
using System.Text.Json;

namespace OpenSage.Tools.TestHarness;

public sealed class ValidationGroundCameraSettings
{
    public const float MinimumSpeed = 0.1f;
    public const float MaximumSpeed = 4f;

    public float HorizontalSpeed { get; set; } = 1f;
    public float VerticalSpeed { get; set; } = 1f;

    public static ValidationGroundCameraSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new ValidationGroundCameraSettings();
        }

        var settings = JsonSerializer.Deserialize<ValidationGroundCameraSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Camera settings must be a JSON object.");
        settings.Validate();
        return settings;
    }

    public void Save(string path)
    {
        Validate();
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Validate()
    {
        if (!float.IsFinite(HorizontalSpeed) || HorizontalSpeed < MinimumSpeed || HorizontalSpeed > MaximumSpeed ||
            !float.IsFinite(VerticalSpeed) || VerticalSpeed < MinimumSpeed || VerticalSpeed > MaximumSpeed)
        {
            throw new InvalidDataException($"Camera speeds must be between {MinimumSpeed} and {MaximumSpeed}.");
        }
    }
}
