using System.Diagnostics;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.ReplaySketch.Maui.Services;

public sealed class SystemProcessLauncher : IProcessLauncher
{
    public void Start(ProcessStartInfo psi)
    {
        Process.Start(psi);
    }
}
