using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenSage.Tools.ReplaySketch.Profiles;
using SkiaSharp;

namespace OpenSage.Tests.Tools.ReplaySketch;

/// <summary>
/// Renders the action tree to a bitmap and compares it against stored golden PNG baselines.
/// <para>
/// To create or regenerate goldens, set the environment variable
/// <c>UPDATE_GOLDENS=1</c> before running the tests.  The helper writes updated files both
/// to the test output directory <em>and</em> back to the project source tree so they can
/// be committed.
/// </para>
/// </summary>
public static class GoldenImageHelper
{
    private const int DefaultWidth = 600;
    private const int DefaultHeight = 300;

    // ── Rendering ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders <paramref name="nodes"/> into an off-screen bitmap and returns the raw PNG bytes.
    /// </summary>
    public static byte[] RenderToPng(
        IReadOnlyList<NodeLayout> nodes,
        string? selectedId = null,
        int width = DefaultWidth,
        int height = DefaultHeight)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        ActionTreeRenderer.Render(surface.Canvas, nodes, selectedId);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // ── Golden comparison / update ────────────────────────────────────────────

    /// <summary>
    /// Compares <paramref name="actualPng"/> against the golden PNG stored as
    /// <c>GoldenImages/{<paramref name="name"/>}.png</c> next to the test assembly.
    /// <para>
    /// If the golden does not exist yet (or <c>UPDATE_GOLDENS=1</c> is set), the file is
    /// written to disk (both output directory and project source tree) and the method returns
    /// without asserting — so the first run always passes and produces the baseline.
    /// </para>
    /// </summary>
    public static void AssertMatchesGolden(byte[] actualPng, string name)
    {
        var outputDir = Path.Combine(AppContext.BaseDirectory, "GoldenImages");
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, $"{name}.png");

        var update = string.Equals(
            Environment.GetEnvironmentVariable("UPDATE_GOLDENS"), "1",
            StringComparison.Ordinal);

        if (update || !File.Exists(outputPath))
        {
            File.WriteAllBytes(outputPath, actualPng);
            TryWriteToSourceTree(name, actualPng);
            return; // baseline created — test passes
        }

        var expected = File.ReadAllBytes(outputPath);

        // Decode both images and compare pixel-by-pixel with a small tolerance
        // to handle minor anti-aliasing differences between runs.
        using var expectedBmp = SKBitmap.Decode(expected);
        using var actualBmp = SKBitmap.Decode(actualPng);

        Assert.Equal(expectedBmp.Width, actualBmp.Width);
        Assert.Equal(expectedBmp.Height, actualBmp.Height);

        var width = expectedBmp.Width;
        var height = expectedBmp.Height;
        var totalPixels = width * height;
        var mismatchCount = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var exp = expectedBmp.GetPixel(x, y);
                var act = actualBmp.GetPixel(x, y);
                if (!PixelsClose(exp, act))
                    mismatchCount++;
            }
        }

        var mismatchRatio = (double)mismatchCount / totalPixels;
        Assert.True(
            mismatchRatio < 0.01,
            $"Golden '{name}': {mismatchCount}/{totalPixels} pixels differ ({mismatchRatio:P1} > 1%). "
            + "Run with UPDATE_GOLDENS=1 to regenerate.");
    }

    /// <summary>
    /// Convenience overload that renders <paramref name="nodes"/> then asserts against the golden.
    /// </summary>
    public static void AssertRenderedMatchesGolden(
        IReadOnlyList<NodeLayout> nodes,
        string goldenName,
        string? selectedId = null,
        int width = DefaultWidth,
        int height = DefaultHeight)
    {
        var png = RenderToPng(nodes, selectedId, width, height);
        AssertMatchesGolden(png, goldenName);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool PixelsClose(SKColor a, SKColor b)
    {
        const int tolerance = 4;
        return Math.Abs(a.Red - b.Red) <= tolerance
            && Math.Abs(a.Green - b.Green) <= tolerance
            && Math.Abs(a.Blue - b.Blue) <= tolerance
            && Math.Abs(a.Alpha - b.Alpha) <= tolerance;
    }

    /// <summary>
    /// Attempts to write the golden PNG back to the project source tree so it can be
    /// committed.  Uses upward directory traversal to locate the Tests project folder.
    /// Silently skips if the project directory cannot be found (e.g., on a CI agent).
    /// </summary>
    private static void TryWriteToSourceTree(string name, byte[] png)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.Name == "OpenSage.Tools.ReplaySketch.Tests")
            {
                var sourceDir = Path.Combine(dir.FullName, "GoldenImages");
                Directory.CreateDirectory(sourceDir);
                File.WriteAllBytes(Path.Combine(sourceDir, $"{name}.png"), png);
                return;
            }
            dir = dir.Parent;
        }
    }
}
