namespace Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

public sealed record HarnessCatalogResult(HarnessCatalog? Catalog, string? ErrorCode, string? Message)
{
    public bool Succeeded => Catalog is not null;
}
