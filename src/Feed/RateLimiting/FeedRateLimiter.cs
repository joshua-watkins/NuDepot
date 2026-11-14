using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Upstream;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;

namespace NuDepot.Feed.RateLimiting;

/// <summary>
/// Limits how many requests each client sends, as <see cref="RateLimitingOptions"/> say, and answers requests that
/// need the mirrors while they're asked too often, which <see cref="MirroringOptions.MaxLookupsPerSecond"/> limits,
/// with 429 Too Many Requests. A client that's limited is logged once every <see cref="WarningInterval"/>, rather
/// than with each request.
/// </summary>
internal sealed partial class FeedRateLimiter : IDisposable
{
    /// <summary>
    /// How often a client that's still limited is logged.
    /// </summary>
    internal static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(10);

    // How many warned clients are remembered before the ones warned about long ago are forgotten.
    private const int MaxWarned = 10_000;

    // The item that says when a request's address may send keys again, because it sent too many that weren't valid.
    private const string AuthenticationBlockedItem = "NuDepot.AuthenticationBlocked";

    // The requests that sign up, sign in or add a passkey, which an address may only send a few of.
    private static readonly string[] AccountPaths =
    [
        "/api/account/signup",
        "/api/account/signin/options",
        "/api/account/signin",
        "/api/account/passkeys/options",
        "/api/account/passkeys",
    ];

    public FeedRateLimiter(ILogger<FeedRateLimiter> logger, IOptions<FeedOptions> options, TimeProvider time)
    {
        RateLimitingOptions limits = options.Value.RateLimiting;

        Logger = logger;
        Time = time;
        Limits = limits;
        if (!limits.IsEnabled)
            return;

        // A client that sends more than its burst waits up to a second for the next tokens, then it's refused.
        Requests = PartitionedRateLimiter.Create<string, string>(client => RateLimitPartition.GetTokenBucketLimiter(client, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = limits.RequestBurst,
            TokensPerPeriod = limits.RequestsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = limits.RequestsPerSecond,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        }));
        Accounts = CreatePerMinute(limits.AccountRequestLimit, limits.AccountRequestMinutes);
        FailedAuthentication = CreatePerMinute(limits.FailedAuthenticationLimit, limits.FailedAuthenticationMinutes);
        if (limits.AnonymousRequestsPerSecond > 0)
        {
            Anonymous = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = limits.AnonymousRequestsPerSecond * 10,
                TokensPerPeriod = limits.AnonymousRequestsPerSecond,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                AutoReplenishment = true,
            });
        }
    }

    private ILogger Logger { get; }
    private TimeProvider Time { get; }
    private RateLimitingOptions Limits { get; }
    private PartitionedRateLimiter<string>? Requests { get; }
    private PartitionedRateLimiter<string>? Accounts { get; }
    private PartitionedRateLimiter<string>? FailedAuthentication { get; }
    private RateLimiter? Anonymous { get; }
    private ConcurrentDictionary<string, long> WarnedAt { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The middleware, which comes before routing. It authenticates the request, since who sends it decides its
    /// limit. The authentication middleware gets the same result.
    /// </summary>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            if (Requests == null || context.Request.Path.StartsWithSegments(FeedServiceCollectionExtensions.HealthPath))
            {
                await next(context);
                return;
            }

            AuthenticateResult authentication = await context.AuthenticateAsync();
            if (authentication.Succeeded)
                context.User = authentication.Principal;

            if (await IsAllowedAsync(context))
                await next(context);
        }
        catch (MirrorsBusyException e) when (!context.Response.HasStarted)
        {
            context.Response.Clear();
            await RefuseAsync(context, e.RetryAfter, "The feed is asking its mirrors for packages too often right now. Try again later.");
        }
    }

    /// <summary>
    /// Whether the request's address sent too many keys that weren't valid, so the request's key mustn't be checked.
    /// The request is then refused.
    /// </summary>
    public bool IsAuthenticationBlocked(HttpContext context)
    {
        if (FailedAuthentication == null)
            return false;

        // Asking for no permits only says whether there are any left.
        using RateLimitLease lease = FailedAuthentication.AttemptAcquire(AddressKey(context.Connection.RemoteIpAddress), 0);
        if (lease.IsAcquired)
            return false;

        context.Items[AuthenticationBlockedItem] = RetryAfter(lease, Limits.FailedAuthenticationMinutes);
        return true;
    }

    /// <summary>
    /// Counts a key or credentials that weren't valid against the request's address.
    /// </summary>
    public void AddFailedAuthentication(HttpContext context)
    {
        if (FailedAuthentication == null)
            return;

        string address = AddressKey(context.Connection.RemoteIpAddress);
        FailedAuthentication
            .AttemptAcquire(address)
            .Dispose();

        using RateLimitLease left = FailedAuthentication.AttemptAcquire(address, 0);
        if (!left.IsAcquired && ShouldWarn("authentication " + address))
            Log.AuthenticationBlocked(Logger, FeedActor.Address(context), Limits.FailedAuthenticationLimit, Limits.FailedAuthenticationMinutes);
    }

    /// <summary>
    /// Whether a limit should be logged now: it hasn't been in the last <see cref="WarningInterval"/>.
    /// </summary>
    /// <param name="limited">Who or what is limited.</param>
    internal bool ShouldWarn(string limited)
    {
        long now = Time.GetUtcNow().UtcTicks;
        if (WarnedAt.TryGetValue(limited, out long warnedAt) && now - warnedAt < WarningInterval.Ticks)
            return false;

        WarnedAt[limited] = now;
        if (WarnedAt.Count > MaxWarned)
        {
            foreach (KeyValuePair<string, long> warning in WarnedAt)
            {
                if (now - warning.Value >= WarningInterval.Ticks)
                    WarnedAt.TryRemove(warning);
            }
        }

        return true;
    }

    /// <summary>
    /// The client a request counts against: its user, or its address.
    /// </summary>
    internal static string ClientKey(HttpContext context)
        => FeedClaims.GetUserKey(context.User) is int user
            ? "user " + user.ToString(CultureInfo.InvariantCulture)
            : AddressKey(context.Connection.RemoteIpAddress);

    /// <summary>
    /// The address a request counts against: an IPv4 address, or an IPv6 /64 network, which one machine can have
    /// all of.
    /// </summary>
    internal static string AddressKey(IPAddress? address)
    {
        if (address == null)
            return "unknown";
        if (address.IsIPv4MappedToIPv6)
            return address
                .MapToIPv4()
                .ToString();
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();
        return new IPAddress(bytes) + "/64";
    }

    public void Dispose()
    {
        Requests?.Dispose();
        Accounts?.Dispose();
        FailedAuthentication?.Dispose();
        Anonymous?.Dispose();
    }

    // A limit of a number of requests in a number of minutes, which slides a minute at a time.
    private static PartitionedRateLimiter<string> CreatePerMinute(int limit, int minutes)
        => PartitionedRateLimiter.Create<string, string>(address => RateLimitPartition.GetSlidingWindowLimiter(address, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(minutes),
            SegmentsPerWindow = minutes,
        }));

    private async Task<bool> IsAllowedAsync(HttpContext context)
    {
        if (context.Items.TryGetValue(AuthenticationBlockedItem, out object? blocked))
        {
            await RefuseAsync(context, (TimeSpan)blocked!, "Too many API keys that aren't valid were sent from this address. Try again later.");
            return false;
        }

        // A request beyond the burst waits here for its turn.
        string client = ClientKey(context);
        using (RateLimitLease lease = await Requests!.AcquireAsync(client, 1, context.RequestAborted))
        {
            if (!lease.IsAcquired)
            {
                if (ShouldWarn(client))
                    Log.TooManyRequests(Logger, FeedActor.Describe(context), Limits.RequestBurst, Limits.RequestsPerSecond);
                await RefuseAsync(context, RetryAfter(lease, minutes: 0), "Too many requests. Try again later.");
                return false;
            }
        }

        if (Anonymous != null && FeedClaims.IsAnonymous(context.User))
        {
            using RateLimitLease lease = Anonymous.AttemptAcquire();
            if (!lease.IsAcquired)
            {
                if (ShouldWarn("anonymous"))
                    Log.TooManyAnonymousRequests(Logger, Limits.AnonymousRequestsPerSecond);
                await RefuseAsync(context, RetryAfter(lease, minutes: 0), "Too many requests. Try again later.");
                return false;
            }
        }

        if (IsAccountRequest(context.Request))
        {
            string address = AddressKey(context.Connection.RemoteIpAddress);
            using RateLimitLease lease = Accounts!.AttemptAcquire(address);
            if (!lease.IsAcquired)
            {
                if (ShouldWarn("account " + address))
                    Log.TooManyAccountRequests(Logger, FeedActor.Address(context), Limits.AccountRequestLimit, Limits.AccountRequestMinutes);
                await RefuseAsync(context, RetryAfter(lease, Limits.AccountRequestMinutes), "Too many attempts to sign up or sign in. Try again later.");
                return false;
            }
        }

        return true;
    }

    // How long a refused request should wait: what the limiter says, or else, for a limit over minutes, the minute it
    // slides by, and for one over seconds, a second.
    private static TimeSpan RetryAfter(RateLimitLease lease, int minutes)
    {
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            return retryAfter;

        return minutes > 0 ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(1);
    }

    private static bool IsAccountRequest(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
            return false;

        foreach (string path in AccountPaths)
        {
            if (string.Equals(request.Path.Value, path, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static async Task RefuseAsync(HttpContext context, TimeSpan retryAfter, string message)
    {
        HttpResponse response = context.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        response.ContentType = "text/plain; charset=utf-8";
        await response.WriteAsync(message, context.RequestAborted);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "{Actor} sent more than {Burst} requests at once, or more than {PerSecond} a second, so some are refused with 429 Too Many Requests")]
        public static partial void TooManyRequests(ILogger logger, string actor, int burst, int perSecond);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Anonymous clients together sent more than {PerSecond} requests a second, so some are refused with 429 Too Many Requests")]
        public static partial void TooManyAnonymousRequests(ILogger logger, int perSecond);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Address} tried to sign up, sign in or add a passkey more than {Limit} times in {Minutes} minutes, so those requests are refused with 429 Too Many Requests")]
        public static partial void TooManyAccountRequests(ILogger logger, string address, int limit, int minutes);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Address} sent {Limit} API keys or credentials that aren't valid in {Minutes} minutes, so its keys aren't checked, and its requests with keys are refused with 429 Too Many Requests, until it stops for a while")]
        public static partial void AuthenticationBlocked(ILogger logger, string address, int limit, int minutes);
    }
}
