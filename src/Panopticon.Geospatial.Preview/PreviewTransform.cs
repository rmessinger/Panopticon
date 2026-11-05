namespace Panopticon.Geospatial.Preview;

/// <summary>
/// The explicit coordinate transform of one built preview map (B4b deep dive section 6.1):
/// engine XY is payload game XY minus the crop origin game XY, and stored heights are
/// payload game Z minus the crop minimum game Z, quantized by <see cref="HeightQuantum"/>.
/// Restoring Z means <c>elevation * HeightQuantum + CropMinimumGameZ</c>.
/// </summary>
public sealed record PreviewTransform(double CropOriginGameX, double CropOriginGameY, double CropMinimumGameZ, double HeightQuantum);
