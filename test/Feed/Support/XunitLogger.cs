using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

// Based off:
// https://stackoverflow.com/questions/46169169/net-core-2-0-configurelogging-xunit-test
public class XunitLogger(ITestOutputHelper output, string category) : ILogger
{
    private ITestOutputHelper Output { get; } = output;
    private string Category { get; } = category;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Output.WriteLine($"{Category} [{eventId}] {formatter(state, exception)}");

        if (exception != null)
            Output.WriteLine(exception.ToString());
    }

    private class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose() { }
    }
}
