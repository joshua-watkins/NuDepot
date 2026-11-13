namespace NuDepot.Feed.Tests;

public static class TestResources
{
    // TODO: either typeof(TestData).name or nameof to namespace
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
}
