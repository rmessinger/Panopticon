using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.ReplaySketch.Model;

public static class ReplayScenarioFactory
{
    /// <summary>
    /// Loads the Alpine Assault USA vs GLA scenario from its embedded JSON profile.
    /// Edit <c>Profiles/alpine-assault-usa-vs-gla.json</c> to change actions or timing.
    /// </summary>
    public static ReplayScenario CreateAlpineAssaultUSAvGLA() =>
        ScenarioProfileLoader.LoadEmbedded("alpine-assault-usa-vs-gla.json");

    /// <summary>
    /// Loads the "build one of everything" variant of the Alpine Assault scenario from its
    /// embedded JSON profile. Both players construct every building and train every unit in
    /// their faction's <see cref="BuildingCatalog"/> / <see cref="UnitCatalog"/> before
    /// attacking. Edit <c>Profiles/alpine-assault-usa-vs-gla-full-build.json</c> to change
    /// actions or timing.
    /// </summary>
    public static ReplayScenario CreateAlpineAssaultUSAvGLAFullBuild() =>
        ScenarioProfileLoader.LoadEmbedded("alpine-assault-usa-vs-gla-full-build.json");
}
