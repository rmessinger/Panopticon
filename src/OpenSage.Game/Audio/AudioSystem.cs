using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSage.Graphics.Cameras;
using OpenSage.IO;
using OpenSage.Logic.Object;
using SharpAudio;
using SharpAudio.Codec;
using SharpAudio.Codec.Wave;

namespace OpenSage.Audio;

public sealed class AudioSystem : GameSystem
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly List<AudioSource> _sources;
    private readonly Dictionary<string, AudioBuffer> _cached;
    private readonly AudioEngine _engine;
    private readonly AudioSettings _settings;
    private readonly Audio3DEngine _3dengine;
    private readonly Dictionary<AudioVolumeSlider, Submixer> _mixers;

    private readonly Random _random;

    private readonly Dictionary<string, int> _musicTrackFinishedCounts = new Dictionary<string, int>();

    private string _currentTrackName;
    private SoundStream _currentTrack;

    private readonly bool _noAudio;

    public AudioSystem(IGame game) : base(game)
    {
        _noAudio = game.Configuration.NoAudio;
        _sources = new List<AudioSource>();
        _cached = new Dictionary<string, AudioBuffer>();
        _mixers = new Dictionary<AudioVolumeSlider, Submixer>();
        _settings = game.AssetStore.AudioSettings.Current;

        try
        {
            _engine = AddDisposable(AudioEngine.CreateDefault());
        }
        catch (Exception ex)
        {
            Logger.Warn($"Audio engine initialization failed, disabling audio: {ex.Message}");
        }

        if (_engine == null)
        {
            Logger.Warn("No audio engine available, disabling audio.");
            _noAudio = true;
            return;
        }

        _3dengine = _engine.Create3DEngine();

        CreateSubmixers();

        // TODO: Sync RNG seed from replay?
        _random = new Random();
    }

    internal override void OnSceneChanged()
    {
        _musicTrackFinishedCounts.Clear();
    }

    public void Update(Camera camera)
    {
        if (camera != null)
        {
            UpdateListener(camera);
        }

        if (_currentTrack != null && !_currentTrack.IsPlaying)
        {
            _musicTrackFinishedCounts.TryGetValue(_currentTrackName, out var count);
            _musicTrackFinishedCounts[_currentTrackName] = count + 1;
            _currentTrack.Dispose();
            _currentTrack = null;
            _currentTrackName = null;
        }
    }

    private void CreateSubmixers()
    {
        if (_engine == null) return;

        var noAudio = Game.Configuration.NoAudio;

        // Create all available mixers
        _mixers[AudioVolumeSlider.SoundFX] = _engine.CreateSubmixer();
        _mixers[AudioVolumeSlider.SoundFX].Volume = noAudio ? 0f : (float)_settings.DefaultSoundVolume;

        _mixers[AudioVolumeSlider.Music] = _engine.CreateSubmixer();
        _mixers[AudioVolumeSlider.Music].Volume = noAudio ? 0f : (float)_settings.DefaultMusicVolume;

        _mixers[AudioVolumeSlider.Ambient] = _engine.CreateSubmixer();
        _mixers[AudioVolumeSlider.Ambient].Volume = noAudio ? 0f : (float)_settings.DefaultAmbientVolume;

        _mixers[AudioVolumeSlider.Voice] = _engine.CreateSubmixer();
        _mixers[AudioVolumeSlider.Voice].Volume = noAudio ? 0f : (float)_settings.DefaultVoiceVolume;

        _mixers[AudioVolumeSlider.Movie] = _engine.CreateSubmixer();
        _mixers[AudioVolumeSlider.Movie].Volume = noAudio ? 0f : (float)_settings.DefaultMovieVolume;
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        base.Dispose(disposeManagedResources);

        _sources.Clear();
        _cached.Clear();
    }

    /// <summary>
    /// Opens a cached audio file. Usually used for small audio files (.wav)
    /// </summary>
    public AudioSource GetSound(FileSystemEntry entry,
        AudioVolumeSlider? vslider = AudioVolumeSlider.None, bool loop = false)
    {
        if (_engine == null) return null;

        AudioBuffer buffer;

        if (!_cached.ContainsKey(entry.FilePath))
        {
            var decoder = new WaveDecoder(entry.Open());
            byte[] data = null;
            decoder.GetSamples(ref data);

            buffer = AddDisposable(_engine.CreateBuffer());
            buffer.BufferData(data, decoder.Format);

            _cached[entry.FilePath] = buffer;
        }
        else
        {
            buffer = _cached[entry.FilePath];
        }

        var mixer = (vslider.HasValue && vslider.Value != AudioVolumeSlider.None) ?
                    _mixers[vslider.Value] : null;
        var source = AddDisposable(_engine.CreateSource(mixer));
        source.QueueBuffer(buffer);
        // TODO: Implement looping

        _sources.Add(source);

        return source;
    }

    private FileSystemEntry ResolveAudioEventEntry(AudioEvent ev)
    {
        if (ev.Sounds.Length == 0)
        {
            return null;
        }

        // TOOD: Check control flag before choosing at random.
        var sound = ev.Sounds[_random.Next(ev.Sounds.Length)];
        return sound.AudioFile.Value?.Entry;
    }

    private FileSystemEntry ResolveDialogEventEntry(DialogEvent ev)
    {
        return ev.File.Value.Entry;
    }

    /// <summary>
    /// Open a a music/audio file that gets streamed.
    /// </summary>
    public SoundStream GetStream(FileSystemEntry entry)
    {
        if (_engine == null) return null;
        // TODO: Use submixer (currently not possible)
        return AddDisposable(new SoundStream(entry.Open(), _engine));
    }

    public void PlayAudioEvent(string eventName)
    {
        if (_noAudio) return;
        var audioEvent = Game.AssetStore.AudioEvents.GetByName(eventName);

        if (audioEvent == null)
        {
            Logger.Warn($"Missing AudioEvent: {eventName}");
            return;
        }

        PlayAudioEvent(audioEvent);
    }

    public void DisposeSource(AudioSource source)
    {
        if (source == null)
        {
            return;
        }

        if (source.IsPlaying())
        {
            source.Stop();
        }
        _sources.Remove(source);
    }

    private AudioSource PlayAudioEventBase(BaseAudioEventInfo baseAudioEvent, bool looping = false)
    {
        if (baseAudioEvent is not BaseSingleSound audioEvent)
        {
            return null;
        }

        var entry = baseAudioEvent switch
        {
            AudioEvent ae => ResolveAudioEventEntry(ae),
            DialogEvent de => ResolveDialogEventEntry(de),
            _ => null, // todo
        };

        if (entry == null)
        {
            Logger.Warn($"Missing Audio File: {audioEvent.Name}");
            return null;
        }

        var source = GetSound(entry, audioEvent.SubmixSlider, looping || audioEvent.Control.HasFlag(AudioControlFlags.Loop));
        source.Volume = (float)audioEvent.Volume;
        return source;
    }

    private void UpdateListener(Camera camera)
    {
        if (_3dengine == null) return;

        _3dengine.SetListenerPosition(camera.Position);
        var front = Vector3.Normalize(camera.Target - camera.Position);
        _3dengine.SetListenerOrientation(camera.Up, front);
    }

    public AudioSource PlayAudioEvent(GameObject emitter, BaseAudioEventInfo baseAudioEvent, bool looping = false)
    {
        if (_noAudio) return null;
        var source = PlayAudioEventBase(baseAudioEvent, looping);
        if (source == null)
        {
            return null;
        }

        // TODO: fix issues with some units
        //_3dengine.SetSourcePosition(source, emitter.Transform.Translation);
        source.Play();
        return source;
    }

    public AudioSource PlayAudioEvent(Vector3 position, BaseAudioEventInfo baseAudioEvent, bool looping = false)
    {
        if (_noAudio) return null;
        var source = PlayAudioEventBase(baseAudioEvent, looping);
        if (source == null)
        {
            return null;
        }

        _3dengine.SetSourcePosition(source, position);
        source.Play();
        return source;
    }

    public AudioSource PlayAudioEvent(BaseAudioEventInfo baseAudioEvent, bool looping = false)
    {
        if (_noAudio) return null;
        var source = PlayAudioEventBase(baseAudioEvent, looping);
        if (source == null)
        {
            return null;
        }

        source.Play();
        return source;
    }

    public void PlayMusicTrack(MusicTrack musicTrack, bool fadeIn, bool fadeOut)
    {
        if (_noAudio) return;
        // TODO: fading
        StopCurrentMusicTrack(fadeOut);

        _currentTrackName = musicTrack.Name;
        _currentTrack = GetStream(musicTrack.File.Value.Entry);
        _currentTrack.Volume = (float)musicTrack.Volume;
        _currentTrack.Play();
    }

    public void StopCurrentMusicTrack(bool fadeOut = false)
    {
        // todo: fade out
        if (_currentTrack != null)
        {
            _currentTrack.Stop();
            _currentTrack.Dispose();
        }
    }

    public int GetFinishedCount(string musicTrackName)
    {
        return _musicTrackFinishedCounts.TryGetValue(musicTrackName, out var number) ? number : 0;
    }
}
