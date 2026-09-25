using Panopticon.Data.Ini;

namespace Panopticon.Audio;

[AddedIn(SageGame.Bfme)]
public enum AudioVolumeSlider
{
    [IniEnum("SOUNDFX")]
    SoundFX,

    [IniEnum("VOICE")]
    Voice,

    [IniEnum("MUSIC")]
    Music,

    [IniEnum("AMBIENT")]
    Ambient,

    [IniEnum("MOVIE")]
    Movie,

    [IniEnum("None"), AddedIn(SageGame.Bfme)]
    None,
}
