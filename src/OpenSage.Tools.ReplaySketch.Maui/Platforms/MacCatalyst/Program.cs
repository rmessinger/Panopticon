using ObjCRuntime;
using OpenSage.Tools.ReplaySketch.Maui.Platforms.MacCatalyst;
using UIKit;

namespace OpenSage.Tools.ReplaySketch.Maui;

public class Program
{
    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
