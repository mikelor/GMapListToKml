using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using GMapListToKml.Models;
using Microsoft.Extensions.Logging;

namespace GMapListToKml.Services;

/// <summary>
/// Responsible for translating the parsed Google Maps data into a valid KML document.
/// </summary>
public sealed class KmlWriter(ILogger<KmlWriter> logger)
{
    private readonly ILogger<KmlWriter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Writes the provided list to the given file path as a Keyhole Markup Language (KML) document.
    /// </summary>
    public async Task WriteAsync(GoogleMapsListData list, string outputPath, CancellationToken cancellationToken = default)
    {
        if (list is null)
        {
            throw new ArgumentNullException(nameof(list));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path must be provided.", nameof(outputPath));
        }

        _logger.LogDebug("Preparing to write KML file to {OutputPath}.", outputPath);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var settings = new XmlWriterSettings
        {
            Async = true,
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };

        await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = XmlWriter.Create(stream, settings);

        // XML writers buffer data, so we ensure cancellation support by checking the token during long operations.
        cancellationToken.ThrowIfCancellationRequested();

        await writer.WriteStartDocumentAsync().ConfigureAwait(false);
        await writer.WriteStartElementAsync(null, "kml", "http://www.opengis.net/kml/2.2").ConfigureAwait(false);
        await writer.WriteStartElementAsync(null, "Document", null).ConfigureAwait(false);

        await writer.WriteElementStringAsync(null, "name", null, list.Name).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(list.Description))
        {
            await writer.WriteElementStringAsync(null, "description", null, list.Description).ConfigureAwait(false);
        }

        foreach (var place in list.Places)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WritePlacemarkAsync(writer, place).ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false); // Document
        await writer.WriteEndElementAsync().ConfigureAwait(false); // kml
        await writer.WriteEndDocumentAsync().ConfigureAwait(false);

        await writer.FlushAsync().ConfigureAwait(false);
        _logger.LogDebug("Finished writing {Count} placemarks to {OutputPath}.", list.Places.Count, outputPath);
    }

    private static async Task WritePlacemarkAsync(XmlWriter writer, GoogleMapsPlace place)
    {
        await writer.WriteStartElementAsync(null, "Placemark", null).ConfigureAwait(false);
        await writer.WriteElementStringAsync(null, "name", null, place.Name).ConfigureAwait(false);

        List<string> descriptionParts = [];
        if (!string.IsNullOrWhiteSpace(place.Address))
        {
            descriptionParts.Add(place.Address!);
        }

        if (!string.IsNullOrWhiteSpace(place.Notes))
        {
            descriptionParts.Add(place.Notes!);
        }

        if (descriptionParts.Count > 0)
        {
            await writer.WriteStartElementAsync(null, "description", null).ConfigureAwait(false);
            await writer.WriteCDataAsync(string.Join("\n\n", descriptionParts)).ConfigureAwait(false);
            await writer.WriteEndElementAsync().ConfigureAwait(false);
        }

        if (place.Latitude.HasValue && place.Longitude.HasValue)
        {
            await writer.WriteStartElementAsync(null, "Point", null).ConfigureAwait(false);
            var coordinates = string.Format(CultureInfo.InvariantCulture, "{0},{1},0", place.Longitude.Value, place.Latitude.Value);
            await writer.WriteElementStringAsync(null, "coordinates", null, coordinates).ConfigureAwait(false);
            await writer.WriteEndElementAsync().ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);
    }
}
