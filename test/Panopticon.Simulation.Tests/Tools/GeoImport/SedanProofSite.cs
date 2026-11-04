namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>One selected site row from the final selection.tsv.</summary>
public sealed record SedanProofSite(
    string Id,
    string Role,
    int Rank,
    int Row,
    int Col,
    double UtmX,
    double UtmY,
    double L93X,
    double L93Y,
    double GameX,
    double GameY,
    double Wgs84Lat,
    double Wgs84Lon,
    double TruthZ,
    double? SigmaFit,
    double? SlopeDeg,
    double? CurvBound,
    double Density,
    string Stratum,
    string Sector);
