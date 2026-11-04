using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NuDepot.Feed.Entities.Converters;

namespace NuDepot.Feed.Entities;

/// <summary>
/// The package database, in SQLite at <see cref="Configuration.DatabaseOptions.ConnectionString"/>.
/// Settings that only apply to one provider are behind checks like <see cref="SqliteDatabaseFacadeExtensions.IsSqlite"/>.
/// </summary>
public class NuDepotContext(DbContextOptions<NuDepotContext> options) : DbContext(options)
{
    /// <summary>
    /// The longest string a column holds, unless it has its own limit.
    /// </summary>
    public const int DefaultMaxStringLength = 4000;

    /// <summary>
    /// The longest package ID.
    /// </summary>
    public const int MaxPackageIdLength = 128;

    /// <summary>
    /// The longest normalized package version.
    /// </summary>
    public const int MaxPackageVersionLength = 64;

    /// <summary>
    /// The longest minimum client version.
    /// </summary>
    public const int MaxPackageMinClientVersionLength = 44;

    /// <summary>
    /// The longest package language.
    /// </summary>
    public const int MaxPackageLanguageLength = 20;

    /// <summary>
    /// The longest package title.
    /// </summary>
    public const int MaxPackageTitleLength = 256;

    /// <summary>
    /// The longest package type name.
    /// </summary>
    public const int MaxPackageTypeNameLength = 512;

    /// <summary>
    /// The longest package type version.
    /// </summary>
    public const int MaxPackageTypeVersionLength = 64;

    /// <summary>
    /// The longest repository type.
    /// </summary>
    public const int MaxRepositoryTypeLength = 100;

    /// <summary>
    /// The longest target framework moniker.
    /// </summary>
    public const int MaxTargetFrameworkLength = 256;

    /// <summary>
    /// The longest dependency version range.
    /// </summary>
    public const int MaxPackageDependencyVersionRangeLength = 256;

    /// <summary>
    /// The longest package hash.
    /// </summary>
    public const int MaxPackageHashLength = 128;

    /// <summary>
    /// The longest list of deprecation reasons, as JSON.
    /// </summary>
    public const int MaxDeprecationReasonsLength = 256;

    /// <summary>
    /// The longest catalog entry type.
    /// </summary>
    public const int MaxCatalogEntryTypeLength = 32;

    /// <summary>
    /// The longest catalog commit ID.
    /// </summary>
    public const int MaxCommitIdLength = 64;

    /// <summary>
    /// The longest user name.
    /// </summary>
    public const int MaxUserNameLength = 64;

    /// <summary>
    /// The longest user role.
    /// </summary>
    public const int MaxUserRoleLength = 32;

    /// <summary>
    /// The longest WebAuthn user handle, in bytes.
    /// </summary>
    public const int MaxUserHandleLength = 64;

    /// <summary>
    /// The longest passkey ceremony name.
    /// </summary>
    public const int MaxCeremonyLength = 32;

    /// <summary>
    /// The longest WebAuthn credential ID, in bytes.
    /// </summary>
    public const int MaxCredentialIdLength = 1023;

    /// <summary>
    /// The longest passkey or API key name.
    /// </summary>
    public const int MaxCredentialNameLength = 128;

    /// <summary>
    /// The longest API key prefix.
    /// </summary>
    public const int MaxApiKeyPrefixLength = 16;

    /// <summary>
    /// The length of an API key's SHA-256 hash, in bytes.
    /// </summary>
    public const int MaxApiKeyHashLength = 32;

    /// <summary>
    /// The length of a session token's SHA-256 hash, in bytes.
    /// </summary>
    public const int MaxSessionTokenHashLength = 32;

    /// <summary>
    /// The SQLite error code for when a unique constraint is violated.
    /// </summary>
    private const int SqliteUniqueConstraintViolationErrorCode = 19;

    /// <summary>
    /// The package versions on the feed.
    /// </summary>
    public DbSet<Package> Packages { get; set; }

    /// <summary>
    /// The packages' dependencies.
    /// </summary>
    public DbSet<PackageDependency> PackageDependencies { get; set; }

    /// <summary>
    /// The packages' types.
    /// </summary>
    public DbSet<PackageType> PackageTypes { get; set; }

    /// <summary>
    /// The packages' target frameworks.
    /// </summary>
    public DbSet<TargetFramework> TargetFrameworks { get; set; }

    /// <summary>
    /// The known vulnerabilities.
    /// </summary>
    public DbSet<VulnerabilityAdvisory> VulnerabilityAdvisories { get; set; }

    /// <summary>
    /// The catalog's entries.
    /// </summary>
    public DbSet<CatalogEntry> CatalogEntries { get; set; }

    /// <summary>
    /// The users who sign in with passkeys.
    /// </summary>
    public DbSet<User> Users { get; set; }

    /// <summary>
    /// The users' passkeys.
    /// </summary>
    public DbSet<UserPasskey> UserPasskeys { get; set; }

    /// <summary>
    /// The users' API keys.
    /// </summary>
    public DbSet<UserApiKey> UserApiKeys { get; set; }

    /// <summary>
    /// The users' signed-in browsers.
    /// </summary>
    public DbSet<UserSession> UserSessions { get; set; }

    /// <summary>
    /// Check whether a <see cref="DbUpdateException"/> is due to a SQL unique constraint violation.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns>Whether the exception was caused to SQL unique constraint violation.</returns>
    public bool IsUniqueConstraintViolationException(DbUpdateException exception)
    {
        return exception.InnerException is SqliteException sqliteException &&
            sqliteException.SqliteErrorCode == SqliteUniqueConstraintViolationErrorCode;
    }

    /// <summary>
    /// Applies any pending migrations to the database, creating the database if it doesn't exist.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    public async Task RunMigrationsAsync(CancellationToken cancellationToken)
    {
        if (Database.IsSqlite())
        {
            // SQLite creates the database file, but not the folder it lives in.
            SqliteConnectionStringBuilder connectionString = new(Database.GetConnectionString());
            string dataSource = connectionString.DataSource;
            if (!string.IsNullOrEmpty(dataSource) && dataSource != ":memory:" && connectionString.Mode != SqliteOpenMode.Memory)
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
            }
        }

        await Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Package>(BuildPackageEntity);
        builder.Entity<PackageDependency>(BuildPackageDependencyEntity);
        builder.Entity<PackageType>(BuildPackageTypeEntity);
        builder.Entity<TargetFramework>(BuildTargetFrameworkEntity);
        builder.Entity<VulnerabilityAdvisory>(BuildVulnerabilityAdvisoryEntity);
        builder.Entity<CatalogEntry>(BuildCatalogEntryEntity);
        builder.Entity<User>(BuildUserEntity);
        builder.Entity<UserPasskey>(BuildUserPasskeyEntity);
        builder.Entity<UserApiKey>(BuildUserApiKeyEntity);
        builder.Entity<UserSession>(BuildUserSessionEntity);

        if (Database.IsSqlite())
            BuildSqliteColumns(builder);
    }

    private void BuildPackageEntity(EntityTypeBuilder<Package> package)
    {
        package.HasKey(p => p.Key);
        package.HasIndex(p => p.Id);
        package
            .HasIndex(p => new { p.Id, p.NormalizedVersionString })
            .IsUnique();

        package
            .Property(p => p.Id)
            .HasMaxLength(MaxPackageIdLength)
            .IsRequired();

        package
            .Property(p => p.NormalizedVersionString)
            .HasColumnName("Version")
            .HasMaxLength(MaxPackageVersionLength)
            .IsRequired();

        package
            .Property(p => p.OriginalVersionString)
            .HasColumnName("OriginalVersion")
            .HasMaxLength(MaxPackageVersionLength);

        package
            .Property(p => p.ReleaseNotes)
            .HasColumnName("ReleaseNotes");

        package
            .Property(p => p.Authors)
            .HasMaxLength(DefaultMaxStringLength)
            .HasConversion(StringArrayToJsonConverter.Instance)
            .Metadata
            .SetValueComparer(StringArrayComparer.Instance);

        package
            .Property(p => p.IconUrl)
            .HasConversion(UriToStringConverter.Instance)
            .HasMaxLength(DefaultMaxStringLength);

        package
            .Property(p => p.LicenseUrl)
            .HasConversion(UriToStringConverter.Instance)
            .HasMaxLength(DefaultMaxStringLength);

        package
            .Property(p => p.ProjectUrl)
            .HasConversion(UriToStringConverter.Instance)
            .HasMaxLength(DefaultMaxStringLength);

        package
            .Property(p => p.RepositoryUrl)
            .HasConversion(UriToStringConverter.Instance)
            .HasMaxLength(DefaultMaxStringLength);

        package
            .Property(p => p.Tags)
            .HasMaxLength(DefaultMaxStringLength)
            .HasConversion(StringArrayToJsonConverter.Instance)
            .Metadata
            .SetValueComparer(StringArrayComparer.Instance);

        package
            .Property(p => p.Description)
            .HasMaxLength(DefaultMaxStringLength);
        package
            .Property(p => p.Language)
            .HasMaxLength(MaxPackageLanguageLength);
        package
            .Property(p => p.MinClientVersion)
            .HasMaxLength(MaxPackageMinClientVersionLength);
        package
            .Property(p => p.Summary)
            .HasMaxLength(DefaultMaxStringLength);
        package
            .Property(p => p.Title)
            .HasMaxLength(MaxPackageTitleLength);
        package
            .Property(p => p.RepositoryType)
            .HasMaxLength(MaxRepositoryTypeLength);
        package
            .Property(p => p.LicenseExpression)
            .HasMaxLength(DefaultMaxStringLength);
        package
            .Property(p => p.PackageHash)
            .HasMaxLength(MaxPackageHashLength);

        package
            .Property(p => p.DeprecationReasons)
            .HasMaxLength(MaxDeprecationReasonsLength)
            .HasConversion(StringArrayToJsonConverter.Instance)
            .Metadata
            .SetValueComparer(StringArrayComparer.Instance);

        package
            .Property(p => p.DeprecationMessage)
            .HasMaxLength(DefaultMaxStringLength);
        package
            .Property(p => p.AlternatePackageId)
            .HasMaxLength(MaxPackageIdLength);
        package
            .Property(p => p.AlternatePackageVersionRange)
            .HasMaxLength(MaxPackageDependencyVersionRangeLength);

        package.Ignore(p => p.IsDeprecated);
        package.Ignore(p => p.Version);
        package.Ignore(p => p.IconUrlString);
        package.Ignore(p => p.LicenseUrlString);
        package.Ignore(p => p.ProjectUrlString);
        package.Ignore(p => p.RepositoryUrlString);

        // TODO: This is needed to make the dependency to package relationship required.
        // Unfortunately, this would generate a migration that drops a foreign key, which
        // isn't supported by SQLite. The migrations will be need to be recreated for this.
        // Consumers will need to recreate their database and reindex all their packages.
        //package.HasMany(p => p.Dependencies)
        //    .WithOne(d => d.Package)
        //    .IsRequired();

        package
            .HasMany(p => p.PackageTypes)
            .WithOne(d => d.Package)
            .IsRequired();

        package
            .HasMany(p => p.TargetFrameworks)
            .WithOne(d => d.Package)
            .IsRequired();

        package
            .Property(p => p.RowVersion)
            .IsRowVersion();
    }

    private void BuildPackageDependencyEntity(EntityTypeBuilder<PackageDependency> dependency)
    {
        dependency.HasKey(d => d.Key);
        dependency.HasIndex(d => d.Id);

        dependency
            .Property(d => d.Id)
            .HasMaxLength(MaxPackageIdLength);
        dependency
            .Property(d => d.VersionRange)
            .HasMaxLength(MaxPackageDependencyVersionRangeLength);
        dependency
            .Property(d => d.TargetFramework)
            .HasMaxLength(MaxTargetFrameworkLength);
    }

    private void BuildPackageTypeEntity(EntityTypeBuilder<PackageType> type)
    {
        type.HasKey(d => d.Key);
        type.HasIndex(d => d.Name);

        type
            .Property(d => d.Name)
            .HasMaxLength(MaxPackageTypeNameLength);
        type
            .Property(d => d.Version)
            .HasMaxLength(MaxPackageTypeVersionLength);
    }

    private void BuildTargetFrameworkEntity(EntityTypeBuilder<TargetFramework> targetFramework)
    {
        targetFramework.HasKey(f => f.Key);
        targetFramework.HasIndex(f => f.Moniker);

        targetFramework
            .Property(f => f.Moniker)
            .HasMaxLength(MaxTargetFrameworkLength);
    }

    private void BuildVulnerabilityAdvisoryEntity(EntityTypeBuilder<VulnerabilityAdvisory> advisory)
    {
        advisory.HasKey(a => a.Key);
        advisory
            .HasIndex(a => new { a.PackageId, a.AdvisoryUrl })
            .IsUnique();

        advisory
            .Property(a => a.PackageId)
            .HasMaxLength(MaxPackageIdLength)
            .IsRequired();
        advisory
            .Property(a => a.VersionRange)
            .HasMaxLength(MaxPackageDependencyVersionRangeLength)
            .IsRequired();
        advisory
            .Property(a => a.AdvisoryUrl)
            .HasMaxLength(DefaultMaxStringLength)
            .IsRequired();
    }

    private void BuildCatalogEntryEntity(EntityTypeBuilder<CatalogEntry> entry)
    {
        entry.HasKey(e => e.Key);
        entry.HasIndex(e => e.CommittedAt);
        entry.HasIndex(e => new { e.PackageId, e.PackageVersion });

        entry
            .Property(e => e.CommitId)
            .HasMaxLength(MaxCommitIdLength)
            .IsRequired();
        entry
            .Property(e => e.Type)
            .HasMaxLength(MaxCatalogEntryTypeLength)
            .IsRequired();
        entry
            .Property(e => e.PackageId)
            .HasMaxLength(MaxPackageIdLength)
            .IsRequired();
        entry
            .Property(e => e.PackageVersion)
            .HasMaxLength(MaxPackageVersionLength)
            .IsRequired();
    }

    private void BuildUserEntity(EntityTypeBuilder<User> user)
    {
        user.HasKey(u => u.Key);
        user
            .HasIndex(u => u.UserName)
            .IsUnique();
        user
            .HasIndex(u => u.Handle)
            .IsUnique();

        user
            .Property(u => u.UserName)
            .HasMaxLength(MaxUserNameLength)
            .IsRequired();
        user
            .Property(u => u.Role)
            .HasConversion<string>()
            .HasMaxLength(MaxUserRoleLength)
            .IsRequired();
        user
            .Property(u => u.Handle)
            .HasMaxLength(MaxUserHandleLength)
            .IsRequired();
        user
            .Property(u => u.Ceremony)
            .HasConversion<string>()
            .HasMaxLength(MaxCeremonyLength);
        user
            .Property(u => u.CeremonyPasskeyName)
            .HasMaxLength(MaxCredentialNameLength);

        user
            .HasMany(u => u.Passkeys)
            .WithOne(p => p.User)
            .HasForeignKey(p => p.UserKey)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        user
            .HasMany(u => u.ApiKeys)
            .WithOne(k => k.User)
            .HasForeignKey(k => k.UserKey)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        user
            .HasMany(u => u.Sessions)
            .WithOne(s => s.User)
            .HasForeignKey(s => s.UserKey)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildUserSessionEntity(EntityTypeBuilder<UserSession> session)
    {
        session.HasKey(s => s.Key);

        session
            .Property(s => s.TokenHash)
            .HasMaxLength(MaxSessionTokenHashLength)
            .IsRequired();
        session
            .Property(s => s.PreviousTokenHash)
            .HasMaxLength(MaxSessionTokenHashLength);
    }

    private void BuildUserPasskeyEntity(EntityTypeBuilder<UserPasskey> passkey)
    {
        passkey.HasKey(p => p.Key);
        passkey
            .HasIndex(p => p.CredentialId)
            .IsUnique();

        passkey
            .Property(p => p.CredentialId)
            .HasMaxLength(MaxCredentialIdLength)
            .IsRequired();
        passkey
            .Property(p => p.PublicKey)
            .IsRequired();
        passkey
            .Property(p => p.Name)
            .HasMaxLength(MaxCredentialNameLength)
            .IsRequired();

        passkey
            .Property(p => p.Transports)
            .HasMaxLength(MaxCredentialNameLength)
            .HasConversion(StringArrayToJsonConverter.Instance)
            .Metadata
            .SetValueComparer(StringArrayComparer.Instance);
    }

    private void BuildUserApiKeyEntity(EntityTypeBuilder<UserApiKey> apiKey)
    {
        apiKey.HasKey(k => k.Key);
        apiKey
            .HasIndex(k => k.Hash)
            .IsUnique();

        apiKey
            .Property(k => k.Name)
            .HasMaxLength(MaxCredentialNameLength)
            .IsRequired();
        apiKey
            .Property(k => k.Prefix)
            .HasMaxLength(MaxApiKeyPrefixLength)
            .IsRequired();
        apiKey
            .Property(k => k.Hash)
            .HasMaxLength(MaxApiKeyHashLength)
            .IsRequired();
    }

    // Package IDs and versions, and user names, are compared without case.
    private static void BuildSqliteColumns(ModelBuilder builder)
    {
        builder
            .Entity<User>()
            .Property(u => u.UserName)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder
            .Entity<Package>()
            .Property(p => p.Id)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder
            .Entity<Package>()
            .Property(p => p.NormalizedVersionString)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder
            .Entity<PackageDependency>()
            .Property(d => d.Id)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder
            .Entity<PackageType>()
            .Property(t => t.Name)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder
            .Entity<TargetFramework>()
            .Property(f => f.Moniker)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder
            .Entity<VulnerabilityAdvisory>()
            .Property(a => a.PackageId)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder
            .Entity<CatalogEntry>()
            .Property(e => e.PackageId)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder
            .Entity<CatalogEntry>()
            .Property(e => e.PackageVersion)
            .HasColumnType("TEXT COLLATE NOCASE");
    }
}
