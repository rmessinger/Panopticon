using System;
using System.Linq;
using OpenSage.Data;
using OpenSage.Mods.Generals;

namespace OpenSage.Tools.ReplaySketch.Services;

public sealed class GeneralsInstallationService
{
    private GameInstallation? _cachedInstallation;
    private bool _searchDone;

    /// <summary>
    /// Overrides the install path for this session: sets the CNC_GENERALS_PATH
    /// environment variable and clears the cached result so the next
    /// <see cref="TryGetInstallation"/> call re-discovers using the new path.
    /// </summary>
    public void SetPath(string folderPath)
    {
        Environment.SetEnvironmentVariable("CNC_GENERALS_PATH", folderPath);
        _cachedInstallation = null;
        _searchDone = false;
    }

    /// <summary>
    /// Tries to locate a Generals installation via the registry, Steam, or the
    /// <c>CNC_GENERALS_PATH</c> environment variable.
    /// Returns <c>false</c> and a null <paramref name="installation"/> if none is found.
    /// </summary>
    public bool TryGetInstallation(out GameInstallation? installation)
    {
        if (!_searchDone)
        {
            _cachedInstallation = InstallationLocators
                .FindAllInstallations(GeneralsDefinition.Instance)
                .FirstOrDefault();
            _searchDone = true;
        }

        installation = _cachedInstallation;
        return installation is not null;
    }
}
