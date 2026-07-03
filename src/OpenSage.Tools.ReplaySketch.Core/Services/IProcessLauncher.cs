using System.Diagnostics;

namespace OpenSage.Tools.ReplaySketch.Services;

public interface IProcessLauncher
{
    void Start(ProcessStartInfo psi);
}
