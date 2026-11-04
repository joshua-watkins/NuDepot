namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The body of a request to deprecate a package version.
/// </summary>
public class DeprecationRequest
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

/// <summary>
/// The body of a request to add or update a known vulnerability, and the response describing the result.
/// </summary>
public class AdvisoryRequest
{
    /// <summary>
    /// The ID of the vulnerable package.
    /// </summary>
    public string? PackageId { get; set; }

    /// <summary>
    /// The vulnerable versions, in NuGet's version range syntax.
    /// </summary>
    public string? Versions { get; set; }

    /// <summary>
    /// Where users can get more information about the vulnerability.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// 0 = Low, 1 = Moderate, 2 = High, 3 = Critical.
    /// </summary>
    public int Severity { get; set; }
}
