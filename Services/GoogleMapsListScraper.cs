using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GMapListToKml.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace GMapListToKml.Services;

/// <summary>
/// Retrieves a Google Maps list page and translates it into strongly typed models.
/// </summary>
public sealed class GoogleMapsListScraper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleMapsListScraper> _logger;

    public GoogleMapsListScraper(HttpClient httpClient, ILogger<GoogleMapsListScraper> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Downloads the Google Maps list and extracts its metadata and places.
    /// </summary>
    public async Task<GoogleMapsListData> FetchListAsync(Uri listUri, CancellationToken cancellationToken = default)
    {
        if (listUri is null)
        {
            throw new ArgumentNullException(nameof(listUri));
        }

        _logger.LogInformation("Downloading Google Maps list from {Uri}", listUri);

        using var response = await _httpClient.GetAsync(listUri, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var htmlContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // HtmlAgilityPack gives us a tolerant DOM-like API which is extremely helpful when working with complex pages.
        var htmlDocument = new HtmlDocument();
        htmlDocument.LoadHtml(htmlContent);

        var scriptContent = ExtractInitializationScript(htmlDocument.DocumentNode);
        if (scriptContent is null)
        {
            throw new InvalidOperationException(
                "Unable to locate the window.APP_INITIALIZATION_STATE script in the retrieved HTML response.");
        }

        var jsonPayload = ExtractJsonPayload(scriptContent);
        if (jsonPayload is null)
        {
            throw new InvalidOperationException("Failed to isolate the APP_INITIALIZATION_STATE JSON payload.");
        }

        var rootNode = JsonNode.Parse(jsonPayload) ?? throw new InvalidOperationException("Empty initialization payload.");
        var listNode = FindListNode(rootNode);
        if (listNode is null)
        {
            throw new InvalidOperationException("Could not locate the list details within the initialization payload.");
        }

        var listData = ParseListNode(listNode);
        _logger.LogInformation("Extracted {Count} places from list '{ListName}'.", listData.Places.Count, listData.Name);
        return listData;
    }

    /// <summary>
    /// Searches the DOM for the script element that hosts the initialization data. We use the DOM instead of string search
    /// to remain resilient to minification or formatting changes in Google's HTML.
    /// </summary>
    private string? ExtractInitializationScript(HtmlNode documentNode)
    {
        foreach (var scriptNode in documentNode.SelectNodes("//script") ?? Enumerable.Empty<HtmlNode>())
        {
            var text = scriptNode.InnerText;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (text.Contains("window.APP_INITIALIZATION_STATE", StringComparison.Ordinal))
            {
                _logger.LogDebug("Found the script node that contains the initialization state block.");
                return text;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the JSON array assigned to the global <c>window.APP_INITIALIZATION_STATE</c> variable.
    /// </summary>
    private static string? ExtractJsonPayload(string scriptContent)
    {
        const string marker = "window.APP_INITIALIZATION_STATE=";

        var markerIndex = scriptContent.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var startIndex = scriptContent.IndexOf('[', markerIndex + marker.Length);
        if (startIndex < 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        var depth = 0;
        var inString = false;
        var isEscaped = false;

        for (var index = startIndex; index < scriptContent.Length; index++)
        {
            var character = scriptContent[index];
            builder.Append(character);

            if (inString)
            {
                if (isEscaped)
                {
                    isEscaped = false;
                }
                else if (character == '\\')
                {
                    isEscaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inString = true;
                    break;

                case '[':
                    depth++;
                    break;

                case ']':
                    depth--;
                    if (depth == 0)
                    {
                        return builder.ToString();
                    }

                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// Recursively traverses the initialization payload looking for the entry that contains the placelist share URL.
    /// </summary>
    private JsonArray? FindListNode(JsonNode node)
    {
        switch (node)
        {
            case JsonArray array:
                {
                    // Scan all elements for a nested array containing the share URL
                    foreach (var element in array)
                    {
                        if (element is JsonArray candidate)
                        {
                            foreach (var item in candidate)
                            {
                                if (item is JsonValue value)
                                {
                                    // Try to get the value as a string, otherwise skip
                                    string? shareUrl = null;
                                    try
                                    {
                                        shareUrl = value.GetValue<string?>();
                                    }
                                    catch
                                    {
                                        // Not a string, ignore
                                    }

                                    if (!string.IsNullOrEmpty(shareUrl) &&
                                        shareUrl.Contains("https://www.google.com/maps/placelists/list/", StringComparison.Ordinal))
                                    {
                                        _logger.LogDebug("Identified share URL '{ShareUrl}' while traversing the payload.", shareUrl);
                                        // Return a new JsonArray containing only the found item's value (not the node itself)
                                         string? valueToAdd = null;
                                        try
                                        {
                                            valueToAdd = value.GetValue<string?>();

                                            if (!string.IsNullOrEmpty(valueToAdd))
                                            {
                                                _logger.LogDebug("Raw shareUrl string before manipulation: {ShareUrl}", valueToAdd);

                                                // Remove everything before the first occurrence of [[[" (including the quote)
                                                int startIdx = valueToAdd.IndexOf("[[[\"");
                                                string processed = startIdx >= 0 ? valueToAdd.Substring(startIdx) : valueToAdd;

                                                // Remove trailing quotation mark if present
                                                if (processed.EndsWith("\"", StringComparison.Ordinal))
                                                {
                                                    processed = processed.Substring(0, processed.Length - 1);
                                                }

                                                _logger.LogDebug("Processed shareUrl string after manipulation: {ShareUrl}", processed);

                                                // Parse the resulting string as JSON and return as a JsonArray
                                                try
                                                {
                                                    var jsonArray = JsonNode.Parse(processed) as JsonArray;
                                                    if (jsonArray != null)
                                                        return jsonArray;
                                                }
                                                catch (Exception ex)
                                                {
                                                    _logger.LogError(ex, "Failed to parse processed shareUrl string as JsonArray: {ShareUrl}", processed);
                                                }
                                            }
                                        }
                                        catch
                                        {
                                            // If value can't be extracted, leave as null
                                        }
                                    }
                                }
                                else if (item is JsonArray nestedArray)
                                {
                                    // Recursively search nested arrays
                                    var result = FindListNode(nestedArray);
                                    if (result is not null)
                                    {
                                        return result;
                                    }
                                }
                                // If item is a JsonObject, you may want to handle it similarly if needed
                            }
                        }
                    }

                    // Continue recursive search
                    foreach (var child in array)
                    {
                        if (child is JsonNode childNode)
                        {
                            var result = FindListNode(childNode);               
                            if (result is not null)
                            {
                                return result;
                            }
                        }
                    }

                    break;
                }

            case JsonObject obj:
                foreach (var property in obj)
                {
                    if (property.Value is JsonNode childNode)
                    {
                        var result = FindListNode(childNode);
                        if (result is not null)
                        {
                            return result;
                        }
                    }
                }

                break;
        }

        return null;
    }

    /// <summary>
    /// Converts the raw JSON array into domain models that are easier for the rest of the application to consume.
    /// </summary>
    private GoogleMapsListData ParseListNode(JsonArray listNode)
    {
        // Defensive: Ensure the array is not empty
        if (listNode is null || listNode.Count == 0)
            throw new InvalidOperationException("listNode is empty or null.");

        // Unwrap to get the main data array
        var innerArray = listNode[0] as JsonArray;
        if (innerArray is null)
            throw new InvalidOperationException("Could not find the expected inner array in listNode.");

        // --- Shared URL ---
        // The third element (index 2) is a JsonArray of 3 elements, the third of which (index 2) is the shared URL string
        string? sharedUrl = null;
        if (innerArray.Count > 2 && innerArray[2] is JsonArray urlArray && urlArray.Count > 2)
        {
            sharedUrl = urlArray[2]?.GetValue<string?>();
        }

        // --- Creator ---
        // The fourth element (index 3) is a JsonArray, first element is the creator
        string? creator = null;
        if (innerArray.Count > 3 && innerArray[3] is JsonArray creatorArray && creatorArray.Count > 0)
        {
            creator = creatorArray[0]?.GetValue<string?>();
        }

        // --- Name ---
        // The fifth element (index 4) is the name string
        string? name = null;
        if (innerArray.Count > 4)
        {
            name = innerArray[4]?.GetValue<string?>();
        }

        // --- Description ---
        // The sixth element (index 5) is the description string
        string? description = null;
        if (innerArray.Count > 5)
        {
            description = innerArray[5]?.GetValue<string?>();
        }

        // --- Places ---
        // The ninth element (index 8) is a JsonArray of places
        var places = new List<GoogleMapsPlace>();
        if (innerArray.Count > 8 && innerArray[8] is JsonArray placesArray)
        {
            foreach (var placeNode in placesArray)
            {
                if (placeNode is JsonArray placeArray)
                {
                    var place = ParsePlaceNode(placeArray);
                    if (place is not null)
                    {
                        places.Add(place);
                    }
                }
            }
        }

        // Comments for clarity:
        // sharedUrl: The shared URL string for the list (not used in GoogleMapsListData, but available if needed)
        // creator: The creator of the list
        // name: The name of the list
        // description: The description of the list
        // places: The list of places in the list

        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Unable to determine the list name.");

        return new GoogleMapsListData(name, description, creator, places);
    }

    /// <summary>
    /// Extracts individual pieces of data from a place entry. The Google payload is largely positional, so we defensively
    /// check the indices before reading anything.
    /// </summary>
    private GoogleMapsPlace? ParsePlaceNode(JsonArray placeArray)
    {
        var name = placeArray.Count > 2 ? placeArray[2]?.GetValue<string?>() : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogDebug("Encountered a place entry without a name. Skipping it to keep the KML clean.");
            return null;
        }

        var notes = placeArray.Count > 3 ? placeArray[3]?.GetValue<string?>() : null;

        string? address = null;
        double? latitude = null;
        double? longitude = null;

        if (placeArray.Count > 1 && placeArray[1] is JsonArray locationArray)
        {
            address = locationArray.Count > 4 ? locationArray[4]?.GetValue<string?>() : null;

            if (locationArray.Count > 5 && locationArray[5] is JsonArray coordinatesArray)
            {
                latitude = TryGetDouble(coordinatesArray, 2);
                longitude = TryGetDouble(coordinatesArray, 3);
            }
        }

        return new GoogleMapsPlace(name, address, notes, latitude, longitude);
    }

    private static double? TryGetDouble(JsonArray array, int index)
    {
        if (array.Count > index && array[index] is JsonValue value && value.TryGetValue<double>(out var number))
        {
            return number;
        }

        return null;
    }
}
