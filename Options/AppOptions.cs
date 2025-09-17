using System;

namespace GMapListToKml.Options;

/// <summary>
/// Represents the validated command line options consumed by the application.
/// </summary>
public sealed class AppOptions(Uri inputListUri, string? outputFilePath, bool verbose)
{
    /// <summary>Gets the required Google Maps list URL.</summary>
    public Uri InputListUri { get; } = inputListUri ?? throw new ArgumentNullException(nameof(inputListUri));

    /// <summary>Gets the optional destination file path for the generated KML document.</summary>
    public string? OutputFilePath { get; } = outputFilePath;

    /// <summary>Gets a flag indicating whether verbose/debug logging is enabled.</summary>
    public bool Verbose { get; } = verbose;
}
