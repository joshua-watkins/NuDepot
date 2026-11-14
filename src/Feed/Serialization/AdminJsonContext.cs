using NuDepot.Feed.Endpoints;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NuDepot.Feed.Serialization;

/// <summary>
/// Source generated JSON serialization for the administration endpoints' requests and responses,
/// which use camel case property names like other ASP.NET Core web APIs.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(DeprecationRequest))]
[JsonSerializable(typeof(AdvisoryRequest))]
[JsonSerializable(typeof(AdvisoryRequest[]))]
[JsonSerializable(typeof(AdminUserResponse[]))]
[JsonSerializable(typeof(RoleRequest))]
[JsonSerializable(typeof(SigningResponse))]
[JsonSerializable(typeof(FeedStatusResponse))]
[JsonSerializable(typeof(HealthResponse))]
internal partial class AdminJsonContext : JsonSerializerContext
{
}
