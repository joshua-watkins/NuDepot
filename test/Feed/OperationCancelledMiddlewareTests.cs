using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Tests that requests whose work was cancelled end quietly instead of as server errors.
/// </summary>
public class OperationCancelledMiddlewareTests
{
    public static TheoryData<Exception> Cancellations => new()
    {
        new OperationCanceledException(),
        new TaskCanceledException(),
        new InvalidOperationException("Wrapped.", new OperationCanceledException()),
    };

    [Theory]
    [MemberData(nameof(Cancellations))]
    public async Task CancelledRequestsAreConflicts(Exception exception)
    {
        DefaultHttpContext context = new();
        context.Response.Headers["X-Partial"] = "set before the cancellation";

        await Create(exception).Invoke(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("X-Partial"));
    }

    [Fact]
    public async Task OtherFailuresAreNotHidden()
    {
        DefaultHttpContext context = new();

        await Assert.ThrowsAsync<IOException>(() => Create(new IOException("The disk is gone.")).Invoke(context));
    }

    [Fact]
    public async Task ResponsesThatStartedCantBeChanged()
    {
        DefaultHttpContext context = new();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        await Assert.ThrowsAsync<OperationCanceledException>(() => Create(new OperationCanceledException()).Invoke(context));
    }

    [Fact]
    public async Task RequestsThatSucceedAreUnchanged()
    {
        DefaultHttpContext context = new();
        OperationCancelledMiddleware middleware = new(c =>
        {
            c.Response.StatusCode = StatusCodes.Status201Created;
            return Task.CompletedTask;
        }, NullLogger<OperationCancelledMiddleware>.Instance);

        await middleware.Invoke(context);

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
    }

    private static OperationCancelledMiddleware Create(Exception exception)
        => new(_ => Task.FromException(exception), NullLogger<OperationCancelledMiddleware>.Instance);

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
