public sealed class AppOptions
{
    public Uri InputListUri { get; }
    public string? OutputFilePath { get; }
    public bool Verbose { get; }
    public bool Csv { get; } // Add this property

    public AppOptions(Uri inputListUri, string? outputFilePath, bool verbose, bool csv)
    {
        InputListUri = inputListUri;
        OutputFilePath = outputFilePath;
        Verbose = verbose;
        Csv = csv;
    }
}