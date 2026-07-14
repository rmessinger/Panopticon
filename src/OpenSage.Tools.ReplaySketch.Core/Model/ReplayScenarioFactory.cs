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
}
