#nullable enable
using System.Numerics;

namespace OpenSage.Content.Importing;

public enum ImportedTextureMappingType
{
    Uv,
    Environment,
    LinearOffset,
    Rotate,
    SineLinearOffset,
    StepLinearOffset,
    Screen,
    Scale,
    Grid,
    Random,
    BumpEnv,
    WsEnvironment,
}

/// <summary>
/// Fixed-function texture-coordinate generation for one stage. Mirrors the runtime
/// fixed-function material state so mapping data survives the seam unchanged.
/// </summary>
public sealed class ImportedTextureMapping
{
    public ImportedTextureMappingType MappingType { get; }

    public Vector2 UVPerSec { get; }
    public Vector2 UVScale { get; }
    public Vector2 UVCenter { get; }
    public Vector2 UVAmplitude { get; }
    public Vector2 UVFrequency { get; }
    public Vector2 UVPhase { get; }
    public Vector2 UVStep { get; }

    public float Speed { get; }
    public float Fps { get; }
    public int Log2Width { get; }
    public float StepsPerSecond { get; }

    public ImportedTextureMapping(
        ImportedTextureMappingType mappingType,
        in Vector2 uvPerSec,
        in Vector2 uvScale,
        in Vector2 uvCenter,
        in Vector2 uvAmplitude,
        in Vector2 uvFrequency,
        in Vector2 uvPhase,
        in Vector2 uvStep,
        float speed,
        float fps,
        int log2Width,
        float stepsPerSecond)
    {
        MappingType = mappingType;
        UVPerSec = uvPerSec;
        UVScale = uvScale;
        UVCenter = uvCenter;
        UVAmplitude = uvAmplitude;
        UVFrequency = uvFrequency;
        UVPhase = uvPhase;
        UVStep = uvStep;
        Speed = speed;
        Fps = fps;
        Log2Width = log2Width;
        StepsPerSecond = stepsPerSecond;
    }
}
