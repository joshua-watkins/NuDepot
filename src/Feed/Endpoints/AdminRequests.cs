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

/// <summary>
/// A user, for the administration page.
/// </summary>
public class AdminUserResponse
{
    /// <summary>
    /// The user's ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The user's name.
    /// </summary>
    public string UserName { get; set; } = null!;

    /// <summary>
    /// The user's role: "ReadOnly", "ReadWrite" or "Admin".
    /// </summary>
    public string Role { get; set; } = null!;

    /// <summary>
    /// When the account was created, in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the user last signed in with a passkey, in UTC.
    /// </summary>
    public DateTime? LastSignedInAt { get; set; }

    /// <summary>
    /// How many passkeys the user has.
    /// </summary>
    public int Passkeys { get; set; }

    /// <summary>
    /// How many API keys the user has.
    /// </summary>
    public int ApiKeys { get; set; }

    /// <summary>
    /// How many browsers the user is signed in on.
    /// </summary>
    public int Sessions { get; set; }

    /// <summary>
    /// Whether it's the user who asked.
    /// </summary>
    public bool IsCurrentUser { get; set; }
}

/// <summary>
/// The body of a request to change a user's role.
/// </summary>
public class RoleRequest
{
    /// <summary>
    /// The new role: "ReadOnly", "ReadWrite" or "Admin".
    /// </summary>
    public string? Role { get; set; }
}

/// <summary>
/// The feed's repository signing.
/// </summary>
public class SigningResponse
{
    /// <summary>
    /// Whether the feed signs pushed packages.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Whether the feed generates its certificates, which can be replaced early.
    /// </summary>
    public bool GeneratesCertificates { get; set; }

    /// <summary>
    /// The feed's certificates: the configured one, or every generated one, newest first.
    /// </summary>
    public SigningCertificateResponse[] Certificates { get; set; } = [];
}

/// <summary>
/// A certificate the feed signs, or signed, packages with.
/// </summary>
public class SigningCertificateResponse
{
    /// <summary>
    /// The lowercase hex SHA-256 fingerprint.
    /// </summary>
    public string Fingerprint { get; set; } = null!;

    /// <summary>
    /// The certificate's subject.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// When the certificate became valid.
    /// </summary>
    public DateTimeOffset NotBefore { get; set; }

    /// <summary>
    /// When the certificate expires.
    /// </summary>
    public DateTimeOffset NotAfter { get; set; }

    /// <summary>
    /// When the certificate was revoked, in UTC, if it was replaced early.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Whether the feed signs packages with it now.
    /// </summary>
    public bool IsCurrent { get; set; }
}

/// <summary>
/// The feed's settings, which are in its settings files, and the state of its mirrors.
/// </summary>
public class FeedStatusResponse
{
    /// <summary>
    /// The feed's health: each of its health checks, which "health" sums up for monitors.
    /// </summary>
    public HealthResponse Health { get; set; } = null!;

    /// <summary>
    /// The folder the settings files are in, which is the feed's working directory.
    /// </summary>
    public string SettingsFolder { get; set; } = null!;

    /// <summary>
    /// Whether nobody may change the feed's packages.
    /// </summary>
    public bool IsReadOnlyMode { get; set; }

    /// <summary>
    /// What deleting a package does: "Unlist" or "HardDelete".
    /// </summary>
    public string PackageDeletionBehavior { get; set; } = null!;

    /// <summary>
    /// Whether pushing a package that exists replaces it.
    /// </summary>
    public bool AllowPackageOverwrites { get; set; }

    /// <summary>
    /// What requests without an API key or a signed-in user may do.
    /// </summary>
    public string AnonymousRole { get; set; } = null!;

    /// <summary>
    /// The roles whose API key is set in the settings.
    /// </summary>
    public string[] ApiKeyRoles { get; set; } = [];

    /// <summary>
    /// Whether anyone may create an account.
    /// </summary>
    public bool AllowSignUp { get; set; }

    /// <summary>
    /// The role of new accounts.
    /// </summary>
    public string DefaultRole { get; set; } = null!;

    /// <summary>
    /// Whether requests without a key or a signed-in user may read the feed.
    /// </summary>
    public bool AnonymousReads { get; set; }

    /// <summary>
    /// Whether the feed signs pushed packages.
    /// </summary>
    public bool IsSigning { get; set; }

    /// <summary>
    /// Whether the feed mirrors other feeds.
    /// </summary>
    public bool IsMirroring { get; set; }

    /// <summary>
    /// How long a mirror that couldn't be reached isn't asked, in minutes.
    /// </summary>
    public int UnreachableMirrorMinutes { get; set; }

    /// <summary>
    /// The mirrors, in the order they're asked.
    /// </summary>
    public MirrorResponse[] Mirrors { get; set; } = [];

    /// <summary>
    /// Where packages are stored.
    /// </summary>
    public string? StoragePath { get; set; }
}

/// <summary>
/// The feed's health.
/// </summary>
public class HealthResponse
{
    /// <summary>
    /// The worst of the checks' statuses: "Healthy", "Degraded" or "Unhealthy".
    /// </summary>
    public string Status { get; set; } = null!;

    /// <summary>
    /// The checks, like the database's and the storage's.
    /// </summary>
    public HealthCheckResponse[] Checks { get; set; } = [];
}

/// <summary>
/// One of the feed's health checks.
/// </summary>
public class HealthCheckResponse
{
    /// <summary>
    /// The check's name, like "database".
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// "Healthy", "Degraded" or "Unhealthy".
    /// </summary>
    public string Status { get; set; } = null!;

    /// <summary>
    /// What the check found.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// How long the check took, in milliseconds.
    /// </summary>
    public double DurationMilliseconds { get; set; }
}

/// <summary>
/// A mirror, and whether it can be reached.
/// </summary>
public class MirrorResponse
{
    /// <summary>
    /// The mirror's name.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// The mirrored feed's service index.
    /// </summary>
    public string PackageSource { get; set; } = null!;

    /// <summary>
    /// What requests do while the mirror can't be reached: "Skip" or "Stop".
    /// </summary>
    public string WhenUnreachable { get; set; } = null!;

    /// <summary>
    /// Until when the mirror isn't asked, because it couldn't be reached. Null while it's asked.
    /// </summary>
    public DateTimeOffset? UnreachableUntil { get; set; }
}
