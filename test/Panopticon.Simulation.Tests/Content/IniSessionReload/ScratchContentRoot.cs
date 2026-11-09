#nullable enable

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Panopticon.Tests.Content.IniSessionReload;

internal sealed class ScratchContentRoot : IDisposable
{
    public const string ObjectName = "H6IniProbeObject";

    public static readonly string IniRelativePath = Path.Combine("Data", "INI", "Object", "H6IniProbe.ini");

    public ScratchContentRoot()
    {
        RootDirectory = Path.Combine(Path.GetTempPath(), "h6_ini_reload_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootDirectory);
    }

    public string RootDirectory { get; }

    public void WriteObjectDefinition(int buildCost)
    {
        var iniPath = Path.Combine(RootDirectory, IniRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);
        File.WriteAllText(iniPath, $"""
            Object {ObjectName}
              Side = America
              BuildCost = {buildCost}
            End
            """);
    }

    public string[] RelativeFiles() =>
        Directory.GetFiles(RootDirectory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(RootDirectory, file))
            .ToArray();

    public void AssertNoPackageInteraction(ContentRootProbe probe)
    {
        Assert.Equal(new[] { IniRelativePath }, RelativeFiles());
        Assert.DoesNotContain(probe.Requests, ContentRootProbe.IsManifest);
        Assert.Contains(probe.Requests, request => Path.GetFileName(request) == Path.GetFileName(IniRelativePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
