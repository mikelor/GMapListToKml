using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using GMapListToKml.Models;
using GMapListToKml.Options;
using GMapListToKml.Services;
using GMapListToKml.Utilities;
using Microsoft.Extensions.Logging;

namespace GMapListToKml;

/// <summary>
/// Entry point of the application. Responsible for orchestrating the overall execution flow and
/// wiring up infrastructure such as logging and HTTP dependencies.
/// </summary>
public static class Program
{
    /// <summary>
    /// Main method that coordinates argument parsing, data retrieval, and KML file generation.
    /// </summary>
    /// <param name="args">Command line arguments provided by the user.</param>
    /// <returns>Zero when the application finishes successfully, otherwise a non-zero error code.</returns>
    public static async Task<int> Main(string[] args)
    {
        // A dedicated help flag is easier for users than throwing an error, so we short-circuit early.
        if (AppOptionsParser.ShouldShowHelp(args))
        {
            AppOptionsParser.PrintUsage();
            return 0;
        }

        if (!AppOptionsParser.TryParse(args, out var options, out var errorMessage))
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
            }

            AppOptionsParser.PrintUsage();
            return 1;
        }

        // We immediately store the parsed options in a non-nullable variable so the remainder of the method can use it safely.
        var appOptions = options ?? throw new InvalidOperationException("Options parsing returned a null result.");

        // The logger factory is built once so every service shares the same formatting and level configuration.
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                options.SingleLine = true;
            });
            builder.SetMinimumLevel(appOptions.Verbose ? LogLevel.Debug : LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger(typeof(Program));

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler? cancelHandler = null;
        cancelHandler = (_, eventArgs) =>
        {
            // Cancelling prevents the process from terminating abruptly, giving us time to clean up resources.
            eventArgs.Cancel = true;
            if (!cancellation.IsCancellationRequested)
            {
                logger.LogWarning("Cancellation requested. Attempting to stop gracefully...");
                cancellation.Cancel();
            }
        };

        Console.CancelKeyPress += cancelHandler;

        try
        {
            using var httpClient = CreateHttpClient();

            // Parse command line arguments
            bool exportCsv = args.Contains("--csv", StringComparer.OrdinalIgnoreCase);

            var scraper = new GoogleMapsListScraper(httpClient, loggerFactory.CreateLogger<GoogleMapsListScraper>());
            var listData = await scraper.FetchListAsync(appOptions.InputListUri, cancellation.Token).ConfigureAwait(false);

            var outputPath = OutputPathResolver.Resolve(appOptions.OutputFilePath, listData.Name);

            var kmlWriter = new KmlWriter(loggerFactory.CreateLogger<KmlWriter>());
            await kmlWriter.WriteAsync(listData, outputPath, cancellation.Token).ConfigureAwait(false);

            logger.LogInformation("KML file created at {OutputPath}", outputPath);

            if (exportCsv)
            {
                // Sanitize the name for a valid filename
                var csvFileName = string.Concat(listData.Name.Split(System.IO.Path.GetInvalidFileNameChars())) + ".csv";
                using var writer = new System.IO.StreamWriter(csvFileName);
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
                csv.WriteRecords(listData.Places);
                logger.LogInformation("CSV file created: {CsvFileName}", csvFileName);
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Operation cancelled by user.");
            return 1;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while generating the KML file.");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    /// <summary>
    /// Creates and configures an <see cref="HttpClient"/> instance tailored for Google Maps requests.
    /// </summary>
    private static HttpClient CreateHttpClient()
    {
        // We mimic a standard browser so Google returns the same HTML a user would see in practice.
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        return client;
    }
}
