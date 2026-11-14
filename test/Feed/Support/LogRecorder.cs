using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace NuDepot.Feed.Tests.Support;

/// <summary>
/// Keeps what's logged, so tests can check it.
/// </summary>
public sealed class LogRecorder : ILoggerProvider
{
    private ConcurrentQueue<LogEntry> Logged { get; } = new();

    /// <summary>
    /// What was logged, in order.
    /// </summary>
    public IReadOnlyList<LogEntry> Entries => [.. Logged];

    /// <summary>
    /// The warnings logged in a category, like NuDepot.Feed.
    /// </summary>
    public IReadOnlyList<string> Warnings(string category)
        => [.. Entries
            .Where(entry => entry.Level == LogLevel.Warning && entry.Category == category)
            .Select(entry => entry.Message)];

    /// <summary>
    /// The messages logged at a level.
    /// </summary>
    public IReadOnlyList<string> Messages(LogLevel level)
        => [.. Entries
            .Where(entry => entry.Level == level)
            .Select(entry => entry.Message)];

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Logged);

    public void Dispose() { }

    private sealed class RecordingLogger(string category, ConcurrentQueue<LogEntry> logged) : ILogger
    {
        private string Category { get; } = category;
        private ConcurrentQueue<LogEntry> Logged { get; } = logged;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Logged.Enqueue(new LogEntry(Category, logLevel, formatter(state, exception)));
    }
}

/// <summary>
/// Something that was logged.
/// </summary>
/// <param name="Category">The logger's category.</param>
/// <param name="Level">The level.</param>
/// <param name="Message">The message, with its values.</param>
public sealed record LogEntry(string Category, LogLevel Level, string Message);
