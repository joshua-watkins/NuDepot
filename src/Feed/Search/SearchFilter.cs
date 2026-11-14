namespace NuDepot.Feed.Search;

/// <param name="Field">The normalized field: id, packageid, version, title, tags, authors, description or summary.</param>
/// <param name="Value">The value the field must match.</param>
internal sealed record SearchFilter(string Field, string Value);
