using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.ServiceIndex;

namespace NuDepot.Gallery.Pages;

/// <summary>
/// The page that explains how to push packages.
/// </summary>
public class UploadModel(IServiceIndexClient serviceIndex, FeedConnection feed)
{
    private IServiceIndexClient ServiceIndex { get; } = serviceIndex;
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
    /// Reads the feed's URLs from its service index.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when the URLs are set.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ServiceIndexResponse serviceIndex = await ServiceIndex.GetAsync(cancellationToken);

        ServiceIndexUrl = Feed.ToPublicUrl(Feed.ServiceIndexUrl);
        PublishUrl = Feed.ToPublicUrl(serviceIndex.GetPackagePublishResourceUrl());
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
