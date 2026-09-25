using Panopticon.Content;

namespace Panopticon.Audio;

public abstract class SoundOrEvaEvent
{
    public LazyAssetReference<BaseAudioEventInfo> Sound { get; internal set; }
    public string EvaEvent { get; private set; }
}
