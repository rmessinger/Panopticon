using System.Collections.Generic;

namespace Panopticon.Tools.ReplaySketch.Model.Capture;

/// <summary>
/// Unit content imported through the shared H2a layer before actors spawn. Actors reference the
/// published units as <c>&lt;Source&gt;:&lt;name&gt;</c>, for example
/// <c>ee2:HeavyInfantry1UpgradeEpoch1</c>. Source files resolve against the operator install named by
/// the source's environment variable (<c>EMPIRE_EARTH_II_PATH</c> for <c>ee2</c>).
/// </summary>
public class CaptureContentImport
{
    /// <summary>Content source id; only <c>ee2</c> is supported.</summary>
    public string Source { get; set; } = "ee2";

    /// <summary>Source unit type names to publish.</summary>
    public List<string> UnitTypes { get; set; } = [];

    /// <summary>Tech-tree epoch whose stat row is published.</summary>
    public int Epoch { get; set; } = 1;

    /// <summary>Regional model variant suffix, for example <c>we</c>.</summary>
    public string Region { get; set; } = "we";

    /// <summary>Declared scale from one source distance unit (an EE2 tile) to game units.</summary>
    public double GameUnitsPerSourceDistance { get; set; }

    /// <summary>DDF files read for the closure, in load order.</summary>
    public List<string> DdfPaths { get; set; } = [];

    /// <summary>Unsupported gameplay mechanics the run accepts leaving out; each is reported.</summary>
    public List<string> ExcludedMechanics { get; set; } = [];
}
