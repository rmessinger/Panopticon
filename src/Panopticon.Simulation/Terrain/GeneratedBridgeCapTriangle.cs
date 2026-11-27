#nullable enable

namespace Panopticon.Terrain;

/// <summary>
/// One cap triangle at the cut plane. <c>LoopIndex</c> is the closed loop it closes; vertices are route-local.
/// </summary>
internal readonly record struct GeneratedBridgeCapTriangle(int LoopIndex, (double X, double Y, double Z) A, (double X, double Y, double Z) B, (double X, double Y, double Z) C);
