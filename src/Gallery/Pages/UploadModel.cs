using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.RepositorySignatures;
using NuDepot.Core.ServiceIndex;

namespace NuDepot.Gallery.Pages;

/// <summary>
/// The page that explains how to push packages, and how to trust the feed's repository signatures.
/// </summary>
/// <param name="serviceIndex">Reads the feed's service index.</param>
/// <param name="repositorySignatures">Reads the certificates the feed signs packages with.</param>
/// <param name="feed">The feed the website shows.</param>
public class UploadModel(IServiceIndexClient serviceIndex, IRepositorySignaturesClient repositorySignatures, FeedConnection feed)
{
    private IServiceIndexClient ServiceIndex { get; } = serviceIndex;
    private IRepositorySignaturesClient RepositorySignatures { get; } = repositorySignatures;
    private FeedConnection Feed { get; } = feed;

    /// <summary>
    /// The application's path base, which links start with.
    /// </summary>
    public string PathBase { get; set; } = string.Empty;

    /// <summary>
    /// The absolute URL of the website's root.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The feed's service index URL, as the browser reaches it.
    /// </summary>
    public string? ServiceIndexUrl { get; private set; }

    /// <summary>
    /// The feed's package publish URL, as the browser reaches it.
    /// </summary>
    public string? PublishUrl { get; private set; }

    /// <summary>
    /// The certificates the feed signs packages with, which are read from the feed each time the page is shown.
    /// Empty if the feed doesn't repository sign its packages.
    /// </summary>
    public IReadOnlyList<SigningCertificateModel> SigningCertificates { get; private set; } = [];

    /// <summary>
    /// Reads the feed's URLs from its service index, and its signing certificates.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when the page's properties are set.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ServiceIndexResponse serviceIndex = await ServiceIndex.GetAsync(cancellationToken);

        ServiceIndexUrl = Feed.ToPublicUrl(Feed.ServiceIndexUrl);
        PublishUrl = Feed.ToPublicUrl(serviceIndex.GetPackagePublishResourceUrl());

        RepositorySignaturesResponse? signatures = await RepositorySignatures.GetOrNullAsync(cancellationToken);
        SigningCertificates = [.. (signatures?.SigningCertificates ?? [])
            .Where(certificate => certificate.Fingerprints.ContainsKey(RepositorySigningCertificate.Sha256Oid))
            .Select(certificate => new SigningCertificateModel(
                certificate.Fingerprints[RepositorySigningCertificate.Sha256Oid],
                certificate.Subject,
                certificate.NotBefore,
                certificate.NotAfter,
                IsSelfSigned: certificate.Subject != null && certificate.Subject == certificate.Issuer))];
    }

    /// <summary>
    /// The nuget.config section that trusts the feed's signing certificates.
    /// </summary>
    /// <returns>The trustedSigners element, or null if the feed doesn't sign packages.</returns>
    public string? GetTrustedSignersConfig()
    {
        if (SigningCertificates.Count == 0)
            return null;

        IEnumerable<string> certificates = SigningCertificates.Select(certificate =>
            $"""      <certificate fingerprint="{certificate.Fingerprint}" hashAlgorithm="SHA256" allowUntrustedRoot="{(certificate.IsSelfSigned ? "true" : "false")}" />""");

        return string.Join('\n', [
            "<trustedSigners>",
            $"""  <repository name="NuDepot" serviceIndex="{ServiceIndexUrl}">""",
            .. certificates,
            "  </repository>",
            "</trustedSigners>",
        ]);
    }

    /// <summary>
    /// The commands that push a package, shown in tabs.
    /// </summary>
    public IReadOnlyList<PushInstructions> GetPushInstructions()
    {
        return
        [
            new PushInstructions(
                ".NET CLI",
                [$"dotnet nuget push -s {ServiceIndexUrl} package.nupkg"],
                "https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push"),

            new PushInstructions(
                "NuGet",
                [$"nuget push -Source {ServiceIndexUrl} package.nupkg"],
                "https://docs.microsoft.com/en-us/nuget/tools/cli-ref-push"),

            new PushInstructions(
                "Paket",
                [$"paket push --url {BaseUrl} package.nupkg"],
                "https://fsprojects.github.io/Paket/paket-push.html"),

            // TODO: This is broken...
            new PushInstructions(
                "PowerShellGet",
                [
                    $"Register-PSRepository -Name \"NuDepot\" -SourceLocation \"{ServiceIndexUrl}\" "
                        + $"-PublishLocation \"{PublishUrl}\" -InstallationPolicy \"Trusted\"",
                    "Publish-Module -Name PS-Module -Repository NuDepot",
                ],
                "https://docs.microsoft.com/en-us/powershell/module/powershellget/publish-module"),
        ];
    }
}

/// <summary>
/// How to push a package with one tool.
/// </summary>
/// <param name="Name">The tool's name.</param>
/// <param name="Content">The command's lines.</param>
/// <param name="Documentation">The tool's documentation.</param>
public record PushInstructions(string Name, IReadOnlyList<string> Content, string Documentation);

/// <summary>
/// A certificate the feed signs packages with.
/// </summary>
/// <param name="Fingerprint">The lowercase hex SHA-256 fingerprint that NuGet's trusted signers use.</param>
/// <param name="Subject">The certificate's subject.</param>
/// <param name="NotBefore">When the certificate becomes valid.</param>
/// <param name="NotAfter">When the certificate expires.</param>
/// <param name="IsSelfSigned">Whether the certificate signed itself, so clients have to allow an untrusted root.</param>
public record SigningCertificateModel(string Fingerprint, string? Subject, DateTimeOffset NotBefore, DateTimeOffset NotAfter, bool IsSelfSigned);
