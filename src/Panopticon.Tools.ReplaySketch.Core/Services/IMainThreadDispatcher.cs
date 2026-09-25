using System;
using System.Threading.Tasks;

namespace Panopticon.Tools.ReplaySketch.Services;

public interface IMainThreadDispatcher
{
    Task InvokeOnMainThreadAsync(Action action);
}
