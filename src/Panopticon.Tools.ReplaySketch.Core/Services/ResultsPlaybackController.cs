#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// WI-A5-31: playback speeds in multiples of real time. The fixed set keeps the view combo and
/// the controller tests in sync; 1x advances one recorded frame per 30 Hz tick.
/// </summary>
public static class PlaybackSpeeds
{
    public static readonly IReadOnlyList<double> All = [0.5, 1.0, 2.0, 4.0];

    public const double Default = 1.0;

    public static bool Contains(double value)
    {
        foreach (var speed in All)
        {
            if (speed == value)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// WI-A5-31: drives play/pause/step/fast-forward over a recorded timeline. Playback advances
/// through the artifact reader only; it never reruns the simulation. Reaching the last recorded
/// frame pauses explicitly, and truncated artifacts surface the reader's diagnostic so the UI can
/// stop with an explanation instead of advancing into missing data.
/// </summary>
public sealed class ResultsPlaybackController : IDisposable
{
    private readonly RunBundleTimeline _timeline;
    private readonly IPlaybackClock _clock;
    private double _speed = PlaybackSpeeds.Default;
    private double _accumulator;

    public ResultsPlaybackController(RunBundleTimeline timeline, IPlaybackClock clock)
    {
        _timeline = timeline;
        _clock = clock;
        _clock.Tick += OnClockTick;
    }

    public event EventHandler? StateChanged;

    public bool IsPlaying { get; private set; }

    public double Speed
    {
        get => _speed;

        set
        {
            if (!PlaybackSpeeds.Contains(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"Speed must be one of {string.Join(", ", PlaybackSpeeds.All)}.");
            }

            _speed = value;
            OnStateChanged();
        }
    }

    public uint CurrentFrame => _timeline.CurrentFrame;

    public uint FirstFrame => _timeline.FirstFrame;

    public uint LastFrame => _timeline.LastFrame;

    public bool IsTruncated => _timeline.IsTruncated;

    public string? TruncationDiagnostic => _timeline.TruncationDiagnostic;

    public bool AtEnd => CurrentFrame >= LastFrame;

    public void Play()
    {
        if (IsPlaying)
        {
            return;
        }

        if (AtEnd)
        {
            // Restart from the beginning when play is pressed at the end, mirroring common
            // player behavior; the explicit stop state stays observable via AtEnd.
            SeekTo(FirstFrame);
        }

        IsPlaying = true;
        _clock.Start();
        OnStateChanged();
    }

    public void Pause()
    {
        if (!IsPlaying)
        {
            return;
        }

        IsPlaying = false;
        _clock.Stop();
        OnStateChanged();
    }

    public void Toggle()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void StepForward()
    {
        Pause();
        SeekTo(Math.Min(LastFrame, CurrentFrame + 1));
    }

    public void StepBackward()
    {
        Pause();
        // Guard the uint subtraction: stepping back at FirstFrame stays at FirstFrame.
        SeekTo(CurrentFrame == FirstFrame ? FirstFrame : CurrentFrame - 1);
    }

    /// <summary>Bounded fast-forward: jumps at most up to the last recorded frame.</summary>
    public void FastForward(uint frames)
    {
        Pause();
        var target = LastFrame - CurrentFrame >= frames ? CurrentFrame + frames : LastFrame;
        SeekTo(target);
    }

    public void SeekTo(uint frame)
    {
        var clamped = Math.Clamp(frame, FirstFrame, LastFrame);
        if (clamped == CurrentFrame)
        {
            return;
        }

        _timeline.SeekTo(clamped);
        OnStateChanged();
    }

    /// <summary>One clock tick: advances the fractional frame budget for the current speed.</summary>
    public void OnTick()
    {
        if (!IsPlaying)
        {
            return;
        }

        _accumulator += _speed;
        while (_accumulator >= 1.0)
        {
            _accumulator -= 1.0;
            var next = CurrentFrame + 1;
            if (next >= LastFrame)
            {
                SeekTo(LastFrame);
                Pause();
                return;
            }

            SeekTo(next);
        }
    }

    private void OnClockTick(object? sender, EventArgs e) => OnTick();

    public void Dispose()
    {
        _clock.Tick -= OnClockTick;
        IsPlaying = false;
        _clock.Stop();
    }

    private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>WI-A5-31: clock seam so tests drive ticks synchronously without real timers.</summary>
public interface IPlaybackClock
{
    event EventHandler? Tick;

    void Start();

    void Stop();
}
