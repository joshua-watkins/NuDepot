using System;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NuDepot.Core;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Users;

namespace NuDepot.Feed.Extensions;

/// <summary>
/// Turns on the feed's API keys and users. Without these, the feed is open.
/// </summary>
public static class FeedAccessExtensions
{
    private const string UserCookieName = "NuDepot.User";

    /// <summary>
    /// Requires a valid API key for what requests without one may not do. Any key may do everything,
    /// unless the application also adds <see cref="AddNuDepotPolicies"/>.
    /// </summary>
    public static AuthenticationBuilder AddNuDepotApiKey(this AuthenticationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Authenticate requests as the feed does, unless the application authenticates them another way.
        builder.Services.Configure<AuthenticationOptions>(options => options.DefaultScheme ??= NuDepotAuthenticationSchemes.Feed);

        // Requests with an API key use it. Otherwise, a signed-in user's cookie is used, if the application has users.
        builder.AddPolicyScheme(NuDepotAuthenticationSchemes.Feed, displayName: null, options =>
        {
            options.ForwardDefaultSelector = context =>
                HasApiKey(context.Request) ||
                !context.Request.Cookies.ContainsKey(UserCookieName) ||
                !context.RequestServices.GetRequiredService<FeedAccess>().UsersEnabled
                    ? NuDepotAuthenticationSchemes.ApiKey
                    : NuDepotAuthenticationSchemes.User;
        });

        return builder.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
            NuDepotAuthenticationSchemes.ApiKey,
            configureOptions: null);
    }

    /// <summary>
    /// Lets people sign in to the website with passkeys, and create their own API keys. Each user
    /// has a role. Requires <see cref="AddNuDepotApiKey"/> and <see cref="AddNuDepotPolicies"/>.
    /// </summary>
    public static AuthenticationBuilder AddNuDepotUsers(this AuthenticationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddDataProtection();
        builder.Services.TryAddScoped<UserService>();
        builder.Services.TryAddScoped<PasskeyService>();
        builder.Services.TryAddSingleton<UserCookieEvents>();

        return builder.AddCookie(NuDepotAuthenticationSchemes.User, options =>
        {
            options.Cookie.Name = UserCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            // Each request gets a new cookie, so the user stays signed in for a year after their
            // last request. The cookie's session is checked in the database with each request.
            options.ExpireTimeSpan = UserService.SessionLifetime;
            options.SlidingExpiration = false;
            options.EventsType = typeof(UserCookieEvents);
        });
    }

    /// <summary>
    /// Gives each API key and user its role. Requires <see cref="AddNuDepotApiKey"/>.
    /// </summary>
    public static AuthorizationBuilder AddNuDepotPolicies(this AuthorizationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, FeedPermissionHandler>());

        return builder.AddFeedPolicies();
    }

    /// <summary>
    /// Whether the configuration has API keys, in <see cref="NuDepotOptions.ApiKey"/>
    /// or <see cref="FeedAuthorizationOptions.ApiKeys"/>.
    /// </summary>
    public static bool HasNuDepotApiKeys(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return !string.IsNullOrEmpty(configuration[nameof(NuDepotOptions.ApiKey)]) ||
            configuration
                .GetSection($"{nameof(NuDepotOptions.Authorization)}:{nameof(FeedAuthorizationOptions.ApiKeys)}")
                .GetChildren()
                .Any(apiKey => !string.IsNullOrEmpty(apiKey[nameof(ApiKeyOptions.Key)]));
    }

    /// <summary>
    /// Whether the configuration turns on users, with <see cref="UserOptions.Enabled"/>.
    /// </summary>
    public static bool HasNuDepotUsers(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetValue<bool>($"{nameof(NuDepotOptions.Authorization)}:{nameof(FeedAuthorizationOptions.Users)}:{nameof(UserOptions.Enabled)}");
    }

    private static bool HasApiKey(HttpRequest request)
        => !string.IsNullOrEmpty(request.GetApiKey()) || request.Headers.Authorization.Count > 0;
}
