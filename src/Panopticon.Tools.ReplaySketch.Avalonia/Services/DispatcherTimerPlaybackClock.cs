#nullable enable

using System;
using Avalonia.Threading;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.ReplaySketch.Avalonia.Services;

/// <summary>
/// WI-A5-31: IPlaybackClock over a DispatcherTimer. One shared 30 Hz clock instance drives the
/// playback controller; the controller decides per tick how far to advance.
/// </summary>
public sealed class DispatcherTimerPlaybackClock : IPlaybackClock
{
    private readonly DispatcherTimer _timer;

    public DispatcherTimerPlaybackClock()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Tick;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}
