using System.Collections.Generic;

namespace Panopticon.Audio;

public sealed class AudioArrayVoice
{
    public List<AudioVoiceEntry> AudioEntries { get; private set; } = new List<AudioVoiceEntry>();
}
