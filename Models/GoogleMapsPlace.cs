namespace GMapListToKml.Models;

/// <summary>
/// Represents a place in a Google Maps list.
/// </summary>
public class GoogleMapsPlace
{
    public string Name { get; }
    public string? Address { get; }
    public string? Notes { get; }
    public double? Latitude { get; }
    public double? Longitude { get; }

    public GoogleMapsPlace(string name, string? address, string? notes, double? latitude, double? longitude)
    {
        Name = name;
        Address = address;
        Notes = notes;
        Latitude = latitude;
        Longitude = longitude;
    }
}