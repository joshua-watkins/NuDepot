using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Serialization;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Upstream;
using NuDepot.Feed.Upstream.Clients;
using NuDepot.Feed.Users;
using System.Security.Cryptography.X509Certificates;

namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The feed's administration, used by the gallery's administration page: its users, its signing certificates and
/// its settings. These aren't part of the NuGet protocol, and need <see cref="FeedPermissions.Administer"/>.
/// </summary>
internal static class AdminEndpoints
{
    private static readonly FeedRole[] AssignableRoles = [FeedRole.ReadOnly, FeedRole.ReadWrite, FeedRole.Admin];

    // GET api/admin/users
    public static async Task<IResult> GetUsersAsync(HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        int? currentUser = FeedClaims.GetUserKey(httpContext.User);
        List<UserSummary> summaries = await users.GetUsersAsync(cancellationToken);

        return Results.Json(
            [.. summaries.Select(u => new AdminUserResponse
            {
                Id = u.Key,
                UserName = u.UserName,
                Role = u.Role.ToString(),
                CreatedAt = u.CreatedAt,
                LastSignedInAt = u.LastSignedInAt,
                Passkeys = u.Passkeys,
                ApiKeys = u.ApiKeys,
                Sessions = u.Sessions,
                IsCurrentUser = u.Key == currentUser,
            })],
            AdminJsonContext.Default.AdminUserResponseArray);
    }

    // PUT api/admin/users/{id}
    public static async Task<IResult> SetRoleAsync(int id, HttpRequest request, UserService users, CancellationToken cancellationToken)
    {
        RoleRequest? body = await EndpointHelpers.ReadJsonOrNullAsync(request, AdminJsonContext.Default.RoleRequest, cancellationToken);
        if (!Enum.TryParse(body?.Role, ignoreCase: true, out FeedRole role) || !AssignableRoles.Contains(role))
            return EndpointHelpers.Text(StatusCodes.Status400BadRequest, "The role must be ReadOnly, ReadWrite or Admin.");

        return ToResult(await users.SetRoleAsync(id, role, cancellationToken), "The feed's last Admin must stay an Admin.");
    }

    // DELETE api/admin/users/{id}
    public static async Task<IResult> DeleteUserAsync(int id, UserService users, CancellationToken cancellationToken)
        => ToResult(await users.DeleteUserAsync(id, cancellationToken), "The feed's last Admin can't be deleted.");

    // DELETE api/admin/users/{id}/sessions
    public static async Task<IResult> DeleteSessionsAsync(int id, UserService users, CancellationToken cancellationToken)
        => await users.DeleteSessionsAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    // DELETE api/admin/users/{id}/apikeys
    public static async Task<IResult> DeleteApiKeysAsync(int id, UserService users, CancellationToken cancellationToken)
        => await users.DeleteApiKeysAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    // GET api/admin/signing
    public static async Task<IResult> GetSigningAsync(HttpContext httpContext, CancellationToken cancellationToken)
        => Results.Json(await GetSigningResponseAsync(httpContext.RequestServices, cancellationToken), AdminJsonContext.Default.SigningResponse);

    // POST api/admin/signing/rotate
    // Replaces this year's generated certificate, like the "rotate signing-certificate" command.
    public static async Task<IResult> RotateSigningCertificateAsync(HttpContext httpContext, IOptions<RepositorySigningOptions> options, CancellationToken cancellationToken)
    {
        if (!options.Value.GeneratesCertificates)
            return EndpointHelpers.Text(StatusCodes.Status409Conflict, "Only generated certificates can be replaced: the feed must sign packages, without a certificate of its own.");

        IServiceProvider services = httpContext.RequestServices;
        await services
            .GetRequiredService<GeneratedSigningCertificates>()
            .RotateAsync(cancellationToken);

        return Results.Json(await GetSigningResponseAsync(services, cancellationToken), AdminJsonContext.Default.SigningResponse);
    }

    // GET api/admin/status
    public static async Task<IResult> GetStatusAsync(HttpContext httpContext, FeedAccess access, HealthCheckService health, CancellationToken cancellationToken)
    {
        IServiceProvider services = httpContext.RequestServices;
        HealthReport report = await health.CheckHealthAsync(cancellationToken);
        FeedOptions feed = services.GetRequiredService<IOptionsSnapshot<FeedOptions>>().Value;
        UserOptions users = services.GetRequiredService<IOptionsSnapshot<UserOptions>>().Value;
        MirroringOptions mirroring = services.GetRequiredService<IOptions<MirroringOptions>>().Value;
        IReadOnlyList<MirrorChain.MirrorStatus> mirrors = services.GetRequiredService<IUpstreamClient>() is MirrorChain chain ? chain.GetStatus() : [];

        FeedStatusResponse response = new()
        {
            Health = new HealthResponse
            {
                Status = report.Status.ToString(),
                Checks = [.. report.Entries.Select(entry => new HealthCheckResponse
                {
                    Name = entry.Key,
                    Status = entry.Value.Status.ToString(),
                    Description = entry.Value.Description,
                    DurationMilliseconds = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                })],
            },
            SettingsFolder = services.GetRequiredService<IHostEnvironment>().ContentRootPath,
            IsReadOnlyMode = feed.IsReadOnlyMode,
            PackageDeletionBehavior = feed.PackageDeletionBehavior.ToString(),
            AllowPackageOverwrites = feed.AllowPackageOverwrites,
            AnonymousRole = access.AnonymousRole.ToString(),
            ApiKeyRoles = [.. FeedAccess.GetApiKeys(feed.Authorization.ApiKeys).Select(key => key.Role.ToString())],
            AllowSignUp = users.AllowSignUp,
            DefaultRole = users.DefaultRole.ToString(),
            AnonymousReads = users.AnonymousReads,
            IsSigning = services.GetRequiredService<IOptions<RepositorySigningOptions>>().Value.IsEnabled,
            IsMirroring = mirroring.IsEnabled,
            UnreachableMirrorMinutes = mirroring.UnreachableMirrorMinutes,
            Mirrors = [.. mirrors.Select(m => new MirrorResponse
            {
                Name = m.Name,
                PackageSource = m.PackageSource.AbsoluteUri,
                WhenUnreachable = m.WhenUnreachable.ToString(),
                UnreachableUntil = m.UnreachableUntil,
            })],
            StoragePath = feed.Storage.Path,
        };

        return Results.Json(response, AdminJsonContext.Default.FeedStatusResponse);
    }

    private static async Task<SigningResponse> GetSigningResponseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        RepositorySigningOptions options = services.GetRequiredService<IOptions<RepositorySigningOptions>>().Value;
        if (!options.IsEnabled)
            return new SigningResponse();

        IRepositorySigningService signing = services.GetRequiredService<IRepositorySigningService>();
        string? current = await signing.GetSigningFingerprintAsync(cancellationToken);

        if (!options.GeneratesCertificates)
        {
            IReadOnlyList<X509Certificate2> configured = await signing.GetCertificatesAsync(cancellationToken);
            return new SigningResponse
            {
                IsEnabled = true,
                Certificates = [.. configured.Select(certificate => ToResponse(certificate, revokedAt: null, current))],
            };
        }

        // Every generated certificate, including the expired and revoked ones, which clients may still have trusted.
        NuDepotDbContext context = services.GetRequiredService<NuDepotDbContext>();
        var rows = await context.SigningCertificates
            .OrderByDescending(c => c.Key)
            .Select(c => new { c.Certificate, c.RevokedAt })
            .ToListAsync(cancellationToken);

        return new SigningResponse
        {
            IsEnabled = true,
            GeneratesCertificates = true,
            Certificates = [.. rows.Select(row =>
            {
                using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(row.Certificate);
                return ToResponse(certificate, row.RevokedAt, current);
            })],
        };
    }

    private static SigningCertificateResponse ToResponse(X509Certificate2 certificate, DateTime? revokedAt, string? current)
    {
        string fingerprint = CertificateFingerprint.Of(certificate);
        return new SigningCertificateResponse
        {
            Fingerprint = fingerprint,
            Subject = certificate.Subject,
            NotBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero),
            NotAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero),
            RevokedAt = revokedAt,
            IsCurrent = string.Equals(fingerprint, current, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static IResult ToResult(UserChangeResult result, string lastAdminMessage) => result switch
    {
        UserChangeResult.Changed => Results.NoContent(),
        UserChangeResult.LastAdmin => EndpointHelpers.Text(StatusCodes.Status409Conflict, lastAdminMessage),
        _ => Results.NotFound(),
    };
}
