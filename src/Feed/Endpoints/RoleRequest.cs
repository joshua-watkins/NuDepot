namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The body of a request to change a user's role.
/// </summary>
internal class RoleRequest
{
    /// <summary>
    /// The new role: "ReadOnly", "ReadWrite" or "Admin".
    /// </summary>
    public string? Role { get; set; }
}
