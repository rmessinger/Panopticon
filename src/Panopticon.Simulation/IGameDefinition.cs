#nullable enable

using System.Collections.Generic;
using System.IO;
using Panopticon.Content;
using Panopticon.Data;
using Panopticon.Gui;
using Panopticon.Gui.CommandListOverlay;
using Panopticon.Gui.ControlBar;

namespace Panopticon;

public interface IGameDefinition
{
    SageGame Game { get; }
    string DisplayName { get; }
    IGameDefinition? BaseGame { get; }

    bool LauncherImagePrefixLang { get; }
    string LauncherImagePath { get; }
    string LauncherExecutable { get; }

    IEnumerable<RegistryKeyPath> RegistryKeys { get; }
    IEnumerable<RegistryKeyPath> LanguageRegistryKeys { get; }

    SteamInstallationDefinition? Steam { get; }

    IMainMenuSource MainMenu { get; }
    IControlBarSource? ControlBar { get; }
    ICommandListOverlaySource? CommandListOverlay { get; }

    string Identifier { get; }

    uint ScriptingTicksPerSecond { get; }

    string GetLocalizedStringsPath(string language);

    OnDemandAssetLoadStrategy CreateAssetLoadStrategy();

    Scene25D CreateScene25D(IScene3D scene3D, AssetStore assetStore);

    bool Probe(string directory) => File.Exists(Path.Combine(directory, LauncherExecutable));
}

public class SteamInstallationDefinition(uint steamAppId, string folderName)
{
    public readonly uint AppId = steamAppId;
    public readonly string FolderName = folderName;
}
