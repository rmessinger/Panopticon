using System.Diagnostics;

namespace Panopticon.Tools.ReplaySketch.Services;

public interface IProcessLauncher
{
    void Start(ProcessStartInfo psi);
}
