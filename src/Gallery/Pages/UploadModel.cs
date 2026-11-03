using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Core.Extensions;
using NuGet.Core.ServiceIndex;

namespace NuGet.Gallery.Pages;

/// <summary>
/// The page that explains how to push packages.
/// </summary>
public class UploadModel
{
    private readonly IServiceIndexClient _serviceIndex;
    private readonly FeedConnection _feed;

    public UploadModel(IServiceIndexClient serviceIndex, FeedConnection feed)
    {
        _serviceIndex = serviceIndex ?? throw new ArgumentNullException(nameof(serviceIndex));
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
    }

    /// <summary>
    /// The application's path base, which links start with.
    /// </summary>
    public string PathBase { get; set; } = string.Empty;

    /// <summary>
    /// The absolute URL of the website's root.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string? ServiceIndexUrl { get; private set; }

    public string? PublishUrl { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var serviceIndex = await _serviceIndex.GetAsync(cancellationToken);

        ServiceIndexUrl = _feed.ToPublicUrl(_feed.ServiceIndexUrl);
        PublishUrl = _feed.ToPublicUrl(serviceIndex.GetPackagePublishResourceUrl());
    }

    /// <summary>
    /// The commands that push a package, shown in tabs.
    /// </summary>
    public IReadOnlyList<PushInstructions> GetPushInstructions()
    {
        return new[]
        {
            new PushInstructions(
                ".NET CLI",
                new[] { $"dotnet nuget push -s {ServiceIndexUrl} package.nupkg" },
                "https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push"),

            new PushInstructions(
                "NuGet",
                new[] { $"nuget push -Source {ServiceIndexUrl} package.nupkg" },
                "https://docs.microsoft.com/en-us/nuget/tools/cli-ref-push"),

            new PushInstructions(
                "Paket",
                new[] { $"paket push --url {BaseUrl} package.nupkg" },
                "https://fsprojects.github.io/Paket/paket-push.html"),

            // TODO: This is broken...
            new PushInstructions(
                "PowerShellGet",
                new[]
                {
                    $"Register-PSRepository -Name \"NuGetFeed\" -SourceLocation \"{ServiceIndexUrl}\" "
                        + $"-PublishLocation \"{PublishUrl}\" -InstallationPolicy \"Trusted\"",
                    "Publish-Module -Name PS-Module -Repository NuGetFeed",
                },
                "https://docs.microsoft.com/en-us/powershell/module/powershellget/publish-module"),
        };
    }
}

/// <summary>
/// How to push a package with one tool.
/// </summary>
/// <param name="Name">The tool's name.</param>
/// <param name="Content">The command's lines.</param>
/// <param name="Documentation">The tool's documentation.</param>
public record PushInstructions(string Name, IReadOnlyList<string> Content, string Documentation);
