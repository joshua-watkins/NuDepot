using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Feed.Configuration;

namespace NuGet.Feed.Validation;

/// <summary>
/// Validates NuGetFeed's options, used at startup.
/// </summary>
public class ValidateStartupOptions
{
    private readonly IOptions<NuGetFeedOptions> _root;
    private readonly IOptions<DatabaseOptions> _database;
    private readonly IOptions<StorageOptions> _storage;
    private readonly IOptions<MirrorOptions> _mirror;
    private readonly IOptions<FeedAuthorizationOptions> _authorization;
    private readonly ILogger<ValidateStartupOptions> _logger;

    public ValidateStartupOptions(
        IOptions<NuGetFeedOptions> root,
        IOptions<DatabaseOptions> database,
        IOptions<StorageOptions> storage,
        IOptions<MirrorOptions> mirror,
        IOptions<FeedAuthorizationOptions> authorization,
        ILogger<ValidateStartupOptions> logger)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Validate()
    {
        try
        {
            // Access each option to force validations to run.
            // Invalid options will trigger an "OptionsValidationException" exception.
            _ = _root.Value;
            _ = _database.Value;
            _ = _storage.Value;
            _ = _mirror.Value;
            _ = _authorization.Value;

            return true;
        }
        catch (OptionsValidationException e)
        {
            foreach (var failure in e.Failures)
            {
                _logger.LogError("{OptionsFailure}", failure);
            }

            _logger.LogError(e, "NuGetFeed configuration is invalid.");
            return false;
        }
        catch (InvalidOperationException e)
        {
            // The configuration binder throws for values it can't convert, like an unknown role.
            _logger.LogError(e, "NuGetFeed configuration is invalid.");
            return false;
        }
    }
}
