using NuDepot.Core.Models;
using System.Text.Json.Serialization;

namespace NuDepot.Core.Serialization;

/// <summary>
/// Source generated JSON serialization for the NuGet protocol's models. Properties that
/// are null are left out, as the NuGet protocol expects.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AutocompleteResponse))]
[JsonSerializable(typeof(CatalogIndex))]
[JsonSerializable(typeof(CatalogPage))]
[JsonSerializable(typeof(PackageDeleteCatalogLeaf))]
[JsonSerializable(typeof(PackageDetailsCatalogLeaf))]
[JsonSerializable(typeof(DependentsResponse))]
[JsonSerializable(typeof(NuDepotRegistrationIndexPage))]
[JsonSerializable(typeof(NuDepotRegistrationIndexResponse))]
[JsonSerializable(typeof(PackageVersionsResponse))]
[JsonSerializable(typeof(RegistrationIndexResponse))]
[JsonSerializable(typeof(RegistrationLeafResponse))]
[JsonSerializable(typeof(RegistrationPageResponse))]
[JsonSerializable(typeof(RepositorySignaturesResponse))]
[JsonSerializable(typeof(SearchResponse))]
[JsonSerializable(typeof(ServiceIndexResponse))]
[JsonSerializable(typeof(IReadOnlyList<VulnerabilityIndexEntry>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, IReadOnlyList<VulnerabilityFileEntry>>))]
public partial class NuGetJsonContext : JsonSerializerContext
{
}
