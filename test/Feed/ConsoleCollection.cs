using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// The commands write to the console and set the process's exit code, so these tests don't run alongside others that
/// might.
/// </summary>
[CollectionDefinition(nameof(ConsoleCollection), DisableParallelization = true)]
public class ConsoleCollection;
