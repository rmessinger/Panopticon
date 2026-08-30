namespace OpenSage.Physics;

/// <summary>
/// Selects which <see cref="IPhysicsWorld"/> implementation a GameEngine creates.
/// The default is the legacy backend; the whole process shares one selection
/// because a game and its physics world are created together.
/// </summary>
public static class PhysicsBackendPolicy
{
    public static PhysicsBackendKind Current { get; set; } = PhysicsBackendKind.Legacy;
}

public enum PhysicsBackendKind
{
    Legacy,
    Bepu,
}
