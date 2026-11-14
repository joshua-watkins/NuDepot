namespace NuDepot.Feed.Tests;

public static class TestResources
{
    // The files in the TestData folder are embedded under the project's namespace and the folder's name.
    private const string ResourcePrefix = "NuDepot.Feed.Tests.TestData.";

    /// <summary>
    /// Test package created with the following properties:
    ///
    ///     <Authors>Test author</Authors>
    ///     <PackageDescription>Test description</PackageDescription>
    ///     <PackageVersion>1.2.3</PackageVersion>
    ///     <IncludeSymbols>true</IncludeSymbols>
    ///     <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    /// </summary>
    public const string Package = "TestData.1.2.3.nupkg";
    public const string SymbolPackage = "TestData.1.2.3.snupkg";

    /// <summary>
    /// The service index the test application serves, as its JSON.
    /// </summary>
    public const string ServiceIndex = "ServiceIndex.json";

    /// <summary>
    /// Buffer the resource stream into memory so the caller doesn't have to dispose.
    /// </summary>
    public static MemoryStream GetResourceStream(string resourceName)
    {
        using Stream? resourceStream = typeof(TestResources)
            .Assembly
            .GetManifestResourceStream(ResourcePrefix + resourceName)
            ?? throw new ArgumentException($"The test resource '{resourceName}' doesn't exist.", nameof(resourceName));

        MemoryStream bufferedStream = new();
        using (resourceStream)
        {
            resourceStream.CopyTo(bufferedStream);
        }

        bufferedStream.Position = 0;
        return bufferedStream;
    }

    /// <summary>
    /// A text file's content, with LF line endings and no final newline, whatever the checkout's line endings.
    /// </summary>
    public static string GetResourceText(string resourceName)
    {
        using StreamReader reader = new(GetResourceStream(resourceName));

        return reader
            .ReadToEnd()
            .ReplaceLineEndings("\n")
            .TrimEnd('\n');
    }
}
