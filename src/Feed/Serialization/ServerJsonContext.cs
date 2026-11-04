using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Endpoints;

namespace NuDepot.Feed.Serialization;

/// <summary>
/// Source generated JSON serialization for the types the server reads and writes outside of the NuGet protocol.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(CatalogPackageSnapshot))]
internal partial class ServerJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Serialization for values stored in the database. This only escapes what JSON requires,
    /// so stored values read the same as they did when the server used Json.NET.
    /// </summary>
    public static ServerJsonContext Database { get; } = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

/// <summary>
/// Source generated JSON serialization for the administration endpoints' requests and responses,
/// which use camel case property names like other ASP.NET Core web APIs.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(DeprecationRequest))]
[JsonSerializable(typeof(AdvisoryRequest))]
internal partial class AdminJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Source generated JSON serialization for the account endpoints' requests and responses.
/// Passkey options use Fido2's own serialization.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AccountResponse))]
[JsonSerializable(typeof(PasskeyCreationRequest))]
[JsonSerializable(typeof(SignInRequest))]
[JsonSerializable(typeof(PasskeyResponse[]))]
[JsonSerializable(typeof(ApiKeyCreationRequest))]
[JsonSerializable(typeof(ApiKeyResponse))]
[JsonSerializable(typeof(ApiKeyResponse[]))]
[JsonSerializable(typeof(Fido2NetLib.AuthenticatorAttestationRawResponse))]
[JsonSerializable(typeof(Fido2NetLib.AuthenticatorAssertionRawResponse))]
internal partial class AccountJsonContext : JsonSerializerContext
{
}
