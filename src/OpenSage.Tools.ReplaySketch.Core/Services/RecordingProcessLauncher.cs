using System.Collections.Generic;
using System.Diagnostics;

namespace OpenSage.Tools.ReplaySketch.Services;

public sealed class RecordingProcessLauncher : IProcessLauncher
{
    public List<ProcessStartInfo> Invocations { get; } = new();

    public void Start(ProcessStartInfo psi)
    {
        Invocations.Add(psi);
    }
}
