using System;
using System.IO;
using System.Linq;

namespace GMapListToKml.Options;

/// <summary>
/// Parses command line arguments into a strongly typed <see cref="AppOptions"/> instance.
/// </summary>
public static class AppOptionsParser
{
    private static readonly string[] HelpFlags = ["--help", "-h", "-?", "/?"];

    /// <summary>
    /// Determines whether the provided argument set is requesting help/usage information.
    /// </summary>
    public static bool ShouldShowHelp(string[] args) => args.Any(arg => HelpFlags.Contains(arg, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Attempts to parse the supplied arguments into a validated <see cref="AppOptions"/> instance.
    /// </summary>
    public static bool TryParse(string[] args, out AppOptions? options, out string? errorMessage)
    {
        string? inputListValue = null;
        string? outputFileValue = null;
        var verbose = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (HelpFlags.Contains(argument, StringComparer.OrdinalIgnoreCase))
            {
                // The help case is handled by the caller, so we quietly ignore it here to avoid noise.
                continue;
            }

            switch (argument)
            {
                case "--inputList":
                    if (++index >= args.Length)
                    {
                        errorMessage = "The --inputList option requires a value.";
                        options = null;
                        return false;
                    }

                    inputListValue = args[index];
                    break;

                case "--outputFile":
                    if (++index >= args.Length)
                    {
                        errorMessage = "The --outputFile option requires a value.";
                        options = null;
                        return false;
                    }

                    outputFileValue = args[index];
                    break;

                case "--verbose":
                    verbose = true;
                    break;

                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        errorMessage = $"Unknown option '{argument}'.";
                    }
                    else
                    {
                        errorMessage = $"Unexpected argument '{argument}'.";
                    }

                    options = null;
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(inputListValue))
        {
            errorMessage = "The --inputList argument is required.";
            options = null;
            return false;
        }

        if (!Uri.TryCreate(inputListValue, UriKind.Absolute, out var inputListUri))
        {
            errorMessage = "The value provided to --inputList must be a valid absolute URL.";
            options = null;
            return false;
        }

        options = new AppOptions(inputListUri, string.IsNullOrWhiteSpace(outputFileValue) ? null : outputFileValue, verbose);
        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Outputs usage information for the application to the console.
    /// </summary>
    public static void PrintUsage()
    {
        var executableName = Path.GetFileName(Environment.ProcessPath) ?? "GMapListToKml";
        Console.WriteLine($"Usage: {executableName} --inputList <url> [--outputFile <path>] [--verbose]");
        Console.WriteLine();
        Console.WriteLine("Required arguments:");
        Console.WriteLine("  --inputList     The Google Maps list URL to download and convert into KML.");
        Console.WriteLine();
        Console.WriteLine("Optional arguments:");
        Console.WriteLine("  --outputFile    Path to the KML file to create. Defaults to the list name with a .kml extension.");
        Console.WriteLine("  --verbose       Enables verbose logging for troubleshooting.");
        Console.WriteLine("  --help, -h      Displays this usage information.");
    }
}
