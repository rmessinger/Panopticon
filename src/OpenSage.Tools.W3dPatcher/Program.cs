using System;
using System.IO;
using CommandLine;
using OpenSage.FileFormats.W3d;

namespace OpenSage.Tools.W3dPatcher;

internal static class Program
{
    internal sealed class Options
    {
        [Option('g', "grey", Default = W3dUnderbodyPatcher.DefaultGrey, Required = false, HelpText = "Grey level of the underbody, 0-255")]
        public byte Grey { get; set; }

        [Value(0, MetaName = "input", HelpText = "Input .w3d file", Required = true)]
        public string InputPath { get; set; } = "";

        [Value(1, MetaName = "output", HelpText = "Output .w3d file", Required = true)]
        public string OutputPath { get; set; } = "";
    }

    private static void Main(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args)
            .WithParsed(Run);
    }

    private static void Run(Options options)
    {
        W3dFile file;
        using (var inputStream = File.OpenRead(options.InputPath))
        {
            file = W3dFile.FromStream(inputStream, options.InputPath);
        }

        var patched = W3dUnderbodyPatcher.Patch(file, options.Grey);

        using (var outputStream = File.Create(options.OutputPath))
        {
            patched.WriteTo(outputStream);
        }

        Console.WriteLine($"added mesh {W3dUnderbodyPatcher.MeshName} to {options.InputPath} -> {options.OutputPath}");
    }
}
