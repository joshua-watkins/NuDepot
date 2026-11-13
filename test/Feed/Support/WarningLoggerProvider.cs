using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace NuDepot.Feed.Tests.Support;

/// <summary>
/// Keeps the warnings that are logged in a category, so tests can check them.
/// </summary>
/// <param name="category">The loggers' category, like NuDepot.Feed.</param>
public sealed class WarningLoggerProvider(string category) : ILoggerProvider
{
    private string Category { get; } = category;
    private ConcurrentQueue<string> Logged { get; } = new();

    /// <summary>
    /// The warnings, in the order they were logged.
    /// </summary>
    public IReadOnlyList<string> Warnings => [.. Logged];

    public ILogger CreateLogger(string categoryName)
        => categoryName == Category
            ? new WarningLogger(Logged)
            : NullLogger.Instance;

    public void Dispose() { }

    private sealed class WarningLogger(ConcurrentQueue<string> logged) : ILogger
    {
        private ConcurrentQueue<string> Logged { get; } = logged;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Logged.Enqueue(formatter(state, exception));
        }
    }
}
