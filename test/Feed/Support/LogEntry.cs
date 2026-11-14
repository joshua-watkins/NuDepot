using Microsoft.Extensions.Logging;

namespace NuDepot.Feed.Tests.Support;

/// <summary>
/// Something that was logged.
/// </summary>
/// <param name="Category">The logger's category.</param>
/// <param name="Level">The level.</param>
/// <param name="Message">The message, with its values.</param>
public sealed record LogEntry(string Category, LogLevel Level, string Message);
