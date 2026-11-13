using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

// Based off:
// https://stackoverflow.com/questions/46169169/net-core-2-0-configurelogging-xunit-test
public sealed class XunitLoggerProvider(ITestOutputHelper output) : ILoggerProvider
{
    private ITestOutputHelper Output { get; } = output;

    public ILogger CreateLogger(string category) => new XunitLogger(Output, category);

    public void Dispose() { }
}
