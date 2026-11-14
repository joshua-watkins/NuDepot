using NuDepot.Feed.Endpoints;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NuDepot.Feed.Serialization;

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
[JsonSerializable(typeof(SessionResponse[]))]
[JsonSerializable(typeof(Fido2NetLib.AuthenticatorAttestationRawResponse))]
[JsonSerializable(typeof(Fido2NetLib.AuthenticatorAssertionRawResponse))]
internal partial class AccountJsonContext : JsonSerializerContext
{
}
