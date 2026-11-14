namespace NuDepot.Core.Tests;

/// <summary>
/// The NuGet protocol documents the client tests are served, from the JSON files in the TestData folder, and their
/// URLs.
/// </summary>
internal static class TestData
{
    public const string AutocompleteUrl = "https://test.example/v3/autocomplete";
    public const string CatalogIndexUrl = "https://test.example/v3/catalog/index.json";
    public const string CatalogPageUrl = "https://test.example/v3/catalog/page0.json";
    public const string DefaultAutocompleteUrl = "https://test.example/v3/autocomplete?take=20&prerelease=True&semVerLevel=2.0.0";
    public const string DefaultSearchUrl = "https://test.example/v3/search?take=20&prerelease=True&semVerLevel=2.0.0";
    public const string PackageContentUrl = "https://test.example/v3/content";
    public const string PackageContentVersionListUrl = "https://test.example/v3/content/test.package/index.json";
    public const string PackageDeleteCatalogLeafUrl = "https://test.example/v3/catalog/2010.01.05.00.00.00/deleted.package.1.0.0.json";
    public const string PackageDetailsCatalogLeafUrl = "https://test.example/v3/catalog/2010.01.05.00.00.00/test.package.1.0.0.json";
    public const string PackageMetadataUrl = "https://test.example/v3/metadata";
    public const string RegistrationIndexInlinedItemsUrl = "https://test.example/v3/metadata/test.package/index.json";
    public const string RegistrationIndexPagedItemsUrl = "https://test.example/v3/metadata/paged.package/index.json";
    public const string RegistrationLeafListedUrl = "https://test.example/v3/metadata/test.package/1.0.0.json";
    public const string RegistrationLeafUnlistedUrl = "https://test.example/v3/metadata/paged.package/2.0.0.json";
    public const string RegistrationPageDeprecatedUrl = "https://test.example/v3/metadata/deprecated.package/page/1.0.0/2.0.0.json";
    public const string RegistrationPageUrl = "https://test.example/v3/metadata/paged.package/page/2.0.0/3.0.0.json";
    public const string SearchUrl = "https://test.example/v3/search";
    public const string ServiceIndexUrl = "https://test.example/v3/index.json";

    public static string CatalogIndex => Read("CatalogIndex.json");
    public static string CatalogPage => Read("CatalogPage.json");
    public static string DefaultAutocomplete => Read("DefaultAutocomplete.json");
    public static string DefaultSearch => Read("DefaultSearch.json");
    public static string PackageContentVersionList => Read("PackageContentVersionList.json");
    public static string PackageDeleteCatalogLeaf => Read("PackageDeleteCatalogLeaf.json");
    public static string PackageDetailsCatalogLeaf => Read("PackageDetailsCatalogLeaf.json");
    public static string RegistrationIndexInlinedItems => Read("RegistrationIndexInlinedItems.json");
    public static string RegistrationIndexPagedItems => Read("RegistrationIndexPagedItems.json");
    public static string RegistrationLeafListed => Read("RegistrationLeafListed.json");
    public static string RegistrationLeafUnlisted => Read("RegistrationLeafUnlisted.json");
    public static string RegistrationPage => Read("RegistrationPage.json");
    public static string RegistrationPageDeprecated => Read("RegistrationPageDeprecated.json");
    public static string ServiceIndex => Read("ServiceIndex.json");

    // A file's text with LF line endings and no final newline, whatever the checkout's line endings.
    private static string Read(string fileName)
    {
        using Stream stream = typeof(TestData).Assembly.GetManifestResourceStream("NuDepot.Core.Tests.TestData." + fileName)
            ?? throw new ArgumentException($"The test file '{fileName}' doesn't exist.", nameof(fileName));
        using StreamReader reader = new(stream);

        return reader
            .ReadToEnd()
            .ReplaceLineEndings("\n")
            .TrimEnd('\n');
    }
}
