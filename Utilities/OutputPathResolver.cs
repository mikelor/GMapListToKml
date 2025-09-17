using System;
using System.IO;
using System.Linq;
using System.Text;

namespace GMapListToKml.Utilities;

/// <summary>
/// Converts user input into a safe and absolute file path for the generated KML document.
/// </summary>
public static class OutputPathResolver
{
    /// <summary>
    /// Returns the absolute path for the KML file. When no explicit path is provided, the list name is used.
    /// </summary>
    public static string Resolve(string? requestedPath, string listName)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            // We always work with absolute paths to make log statements and error messages unambiguous.
            return Path.GetFullPath(requestedPath);
        }

        var sanitizedName = SanitizeFileName(listName);
        var fileName = sanitizedName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase)
            ? sanitizedName
            : sanitizedName + ".kml";

        return Path.GetFullPath(fileName);
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "GoogleMapsList";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            builder.Append(invalidCharacters.Contains(character) || char.IsControl(character) ? '_' : character);
        }

        var sanitized = builder.ToString().Trim('_', ' ');
        return string.IsNullOrEmpty(sanitized) ? "GoogleMapsList" : sanitized;
    }
}
