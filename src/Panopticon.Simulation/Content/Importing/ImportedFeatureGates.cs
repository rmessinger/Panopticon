namespace Panopticon.Content.Importing;

/// <summary>
/// Names of the request-scoped feature gates importers and materializers honor. A gate is
/// enabled per import context; default contexts enable none, keeping shipped-game behavior
/// unchanged.
/// </summary>
public static class ImportedFeatureGates
{
    public const string MorphTargets = "MorphTargets";
}
