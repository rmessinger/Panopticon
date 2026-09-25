using System;
using System.Threading.Tasks;

namespace Panopticon.Tools.ReplaySketch.Services;

public sealed class SynchronousMainThreadDispatcher : IMainThreadDispatcher
{
    public Task InvokeOnMainThreadAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
