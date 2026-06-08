using ObjCRuntime;
using OpenSage.Tools.WorldBuilder.Maui.Platforms.MacCatalyst;
using UIKit;

namespace OpenSage.Tools.WorldBuilder.Maui;

public class Program
{
    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
