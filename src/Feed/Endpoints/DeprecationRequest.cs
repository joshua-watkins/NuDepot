namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The body of a request to deprecate a package version.
/// </summary>
internal class DeprecationRequest
{
    /// <summary>
    /// One or more of "Legacy", "CriticalBugs" or "Other".
    /// </summary>
    public string[]? Reasons { get; set; }

    /// <summary>
    /// An optional message for users.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// An optional package to use instead.
    /// </summary>
    public string? AlternatePackageId { get; set; }

    /// <summary>
    /// The alternate package's versions. Defaults to any version.
    /// </summary>
    public string? AlternatePackageVersionRange { get; set; }
}
