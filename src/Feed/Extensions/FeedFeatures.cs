using Microsoft.Extensions.DependencyInjection;

namespace NuDepot.Feed.Extensions;

/// <summary>
/// The optional features the application added: with <c>Add</c> methods, which turn them on unless the
/// configuration turns them off, or with <c>Enable</c> methods, which leave them off unless the configuration turns
/// them on. A feature's options are only bound from the configuration once it's added either way.
/// </summary>
internal sealed class FeedFeatures
{
    /// <summary>
    /// Whether users' options are bound, and their services added.
    /// </summary>
    public bool Users { get; set; }

    /// <summary>
    /// Whether users are on unless the configuration turns them off, with <c>AddUsers()</c>.
    /// </summary>
    public bool UsersByDefault { get; set; }

    /// <summary>
    /// Whether signing's options are bound.
    /// </summary>
    public bool Signing { get; set; }

    /// <summary>
    /// Whether signing is on unless the configuration turns it off, with <c>AddSigning()</c>.
    /// </summary>
    public bool SigningByDefault { get; set; }

    /// <summary>
    /// Whether mirroring's options are bound.
    /// </summary>
    public bool Mirroring { get; set; }

    /// <summary>
    /// Whether mirroring is on unless the configuration turns it off, with <c>AddMirroring()</c>.
    /// </summary>
    public bool MirroringByDefault { get; set; }

    /// <summary>
    /// The application's features, which <c>AddFeed()</c> and the features' methods register.
    /// </summary>
    public static FeedFeatures Get(IServiceCollection services)
    {
        if (services.FirstOrDefault(service => service.ServiceType == typeof(FeedFeatures))?.ImplementationInstance is FeedFeatures existing)
            return existing;

        FeedFeatures features = new();
        services.AddSingleton(features);
        return features;
    }
}
