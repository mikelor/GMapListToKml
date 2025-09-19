using System.Collections.Generic;

namespace GMapListToKml.Models;

/// <summary>
/// Represents the metadata and places returned from the Google Maps list.
/// </summary>
public sealed record GoogleMapsListData(
    string Name,
    string? Description,
    string? Creator,
    IReadOnlyList<GoogleMapsPlace> Places);

