using Moq;
using NuDepot.Core.Models;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Metadata;
using NuGet.Versioning;
using Xunit;

namespace NuDepot.Feed.Tests.Metadata;

public class RegistrationBuilderTests
{
    private Mock<IUrlGenerator> UrlGenerator { get; }

    public RegistrationBuilderTests()
    {
        UrlGenerator = new Mock<IUrlGenerator>();
    }

    [Fact]
    public void TheRegistrationIndexResponseIsSortedByVersion()
    {
        // Arrange
        string packageId = "NuDepot.Test";
        string[] authors = ["test"];

        List<Package> packages =
        [
            GetTestPackage(packageId, "3.1.0"),
            GetTestPackage(packageId, "10.0.5"),
            GetTestPackage(packageId, "3.2.0"),
            GetTestPackage(packageId, "3.1.0-pre"),
            GetTestPackage(packageId, "1.0.0-beta1"),
            GetTestPackage(packageId, "1.0.0"),
        ];

        PackageRegistration registration = new(packageId, packages);

        RegistrationBuilder registrationBuilder = new(UrlGenerator.Object);

        // Act
        NuDepotRegistrationIndexResponse response = registrationBuilder.BuildIndex(registration);

        // Assert
        Assert.Equal(packages.Count, response.Pages[0].ItemsOrNull!.Count);
        int index = 0;
        foreach (Package? package in packages.OrderBy(p => p.Version))
        {
            Assert.Equal(package.Version.ToFullString(), response.Pages[0].ItemsOrNull![index++].PackageMetadata.Version);
        }
    }

    /// <summary>
    /// Create a fake <see cref="Package"></see> with the minimum metadata needed by the <see cref="RegistrationBuilder"></see>.
    /// </summary>
    private Package GetTestPackage(string packageId, string version)
    {
        return new Package
        {
            Id = packageId,
            Authors = ["test"],
            PackageTypes = [new() { Name = "test" }],
            Dependencies = [],
            Version = new NuGetVersion(version),
        };
    }
}
