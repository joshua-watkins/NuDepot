using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NuGet.Core;
using NuGet.Feed.Authentication;
using NuGet.Feed.Configuration;

namespace NuGet.Feed.Validation;

/// <summary>
/// Checks that the API key settings match the authentication and authorization the application adds.
/// Otherwise the feed would be more open than its settings say.
/// </summary>
public class ValidateFeedAccess(
    IOptions<NuGetFeedOptions> options,
    IOptions<AuthenticationOptions> authentication,
    IOptions<AuthorizationOptions> authorization) : IValidateOptions<FeedAuthorizationOptions>
{
    private const string Section = nameof(NuGetFeedOptions.Authorization);

    private readonly IOptions<NuGetFeedOptions> _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IOptions<AuthenticationOptions> _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
    private readonly IOptions<AuthorizationOptions> _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));

    public ValidateOptionsResult Validate(string? name, FeedAuthorizationOptions options)
    {
        var failures = new List<string>();
        var mode = FeedAccess.GetMode(_authentication.Value, _authorization.Value);
        var hasApiKeys = FeedAccess.GetApiKeys(_options.Value, options).Count > 0;
        var hasUsers = FeedAccess.HasUsers(_authentication.Value);

        switch (mode)
        {
            case FeedAccessMode.Open when hasApiKeys || options.AnonymousRole != null:
                failures.Add(
                    $"API keys or '{Section}:{nameof(FeedAuthorizationOptions.AnonymousRole)}' are configured, but the application " +
                    "doesn't add the feed's authentication, so the feed would be open and ignore them. Call " +
                    "services.AddAuthentication().AddNuGetFeedApiKey() (and services.AddAuthorizationBuilder().AddNuGetFeedPolicies() " +
                    "for roles), or remove the settings.");
                break;

            // Users create their own keys.
            case FeedAccessMode.ApiKey or FeedAccessMode.Roles when !hasApiKeys && !hasUsers:
                failures.Add(
                    $"The application adds the feed's authentication, but no API keys are configured. Add keys to " +
                    $"'{Section}:{nameof(FeedAuthorizationOptions.ApiKeys)}' or set '{nameof(NuGetFeedOptions.ApiKey)}'.");
                break;
        }

        if (mode == FeedAccessMode.Roles &&
            !_authentication.Value.SchemeMap.ContainsKey(NuGetFeedAuthenticationSchemes.ApiKey))
        {
            failures.Add(
                "The application adds the feed's authorization policies, which need its authentication. " +
                "Call services.AddAuthentication().AddNuGetFeedApiKey().");
        }

        if (hasUsers && mode != FeedAccessMode.Roles)
        {
            failures.Add(
                "The application adds the feed's users, who need roles. Call services.AddAuthentication().AddNuGetFeedApiKey() " +
                "and services.AddAuthorizationBuilder().AddNuGetFeedPolicies().");
        }

        if (options.Users.Enabled && !hasUsers)
        {
            failures.Add(
                $"'{Section}:{nameof(FeedAuthorizationOptions.Users)}:{nameof(UserOptions.Enabled)}' is true, but the application " +
                "doesn't add the feed's users. Call services.AddAuthentication().AddNuGetFeedUsers(), or remove the setting.");
        }

        for (var i = 0; i < options.ApiKeys.Count; i++)
        {
            var role = options.ApiKeys[i].Role;
            var field = $"'{Section}:{nameof(FeedAuthorizationOptions.ApiKeys)}:{i}:{nameof(ApiKeyOptions.Role)}'";

            if (mode == FeedAccessMode.ApiKey && role != null && role != FeedRole.Admin)
            {
                failures.Add(
                    $"{field} is {role}, but the application doesn't add the feed's authorization policies, so every key " +
                    "may do everything. Call services.AddAuthorizationBuilder().AddNuGetFeedPolicies(), or remove the role.");
            }

            if (mode == FeedAccessMode.Roles && role == null)
            {
                failures.Add($"{field} is required. Allowed values: {string.Join(", ", Enum.GetNames<FeedRole>())}");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
