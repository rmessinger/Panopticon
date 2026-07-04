using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.ReplaySketch.Maui.Services;

public sealed class MauiMainThreadDispatcher : IMainThreadDispatcher
{
    public Task InvokeOnMainThreadAsync(Action action) =>
        MainThread.InvokeOnMainThreadAsync(action);
}
