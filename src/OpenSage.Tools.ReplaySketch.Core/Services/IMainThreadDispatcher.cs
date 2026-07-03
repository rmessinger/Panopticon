using System;
using System.Threading.Tasks;

namespace OpenSage.Tools.ReplaySketch.Services;

public interface IMainThreadDispatcher
{
    Task InvokeOnMainThreadAsync(Action action);
}
