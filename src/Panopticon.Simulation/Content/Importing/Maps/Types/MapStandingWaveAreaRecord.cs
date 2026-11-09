#nullable enable

using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Content.Importing.Maps;

/// <summary>One standing wave area geometry and animation parameters.</summary>
public sealed record MapStandingWaveAreaRecord(
    uint UniqueId,
    string Name,
    string LayerName,
    float UvScrollSpeed,
    bool UseAdditiveBlending,
    IReadOnlyList<Vector2> Points,
    uint FinalWidth,
    uint FinalHeight,
    uint InitialWidthFraction,
    uint InitialHeightFraction,
    uint InitialVelocity,
    uint TimeToFade,
    uint TimeToCompress,
    uint TimeOffset2ndWave,
    uint DistanceFromShore,
    string Texture,
    bool EnablePcaWave,
    string WaveParticleFXName);
