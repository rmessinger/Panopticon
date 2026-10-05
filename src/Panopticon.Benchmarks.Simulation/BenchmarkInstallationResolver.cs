using System.Linq;
using Panopticon.Data;
using Panopticon.Mods.Generals;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Resolves the Generals installation for benchmark runs through the standard locators.
/// Plan and CLI construction never call this, so argument handling stays install-free.
/// </summary>
public static class BenchmarkInstallationResolver
{
    public static GameInstallation? Resolve()
    {
        return InstallationLocators
            .FindAllInstallations(GeneralsDefinition.Instance)
            .FirstOrDefault();
    }
}
