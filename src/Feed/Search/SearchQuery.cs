using System;
using System.Collections.Generic;
using System.Text;

namespace NuGet.Feed.Search;

/// <summary>
/// A parsed search query: free text terms and field filters, like <c>json tags:serialization author:"Jane Doe"</c>.
/// See: https://learn.microsoft.com/en-us/nuget/consume-packages/finding-and-choosing-packages#search-syntax
/// </summary>
public class SearchQuery
{
    /// <summary>
    /// The filter fields this server understands. Owners aren't modeled, so "owner" matches authors.
    /// </summary>
    private static readonly Dictionary<string, string> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        { "id", "id" },
        { "packageid", "packageid" },
        { "version", "version" },
        { "title", "title" },
        { "tag", "tags" },
        { "tags", "tags" },
        { "author", "authors" },
        { "authors", "authors" },
        { "owner", "authors" },
        { "owners", "authors" },
        { "description", "description" },
        { "summary", "summary" },
    };

    private SearchQuery(IReadOnlyList<string> terms, IReadOnlyList<SearchFilter> filters)
    {
        Terms = terms;
        Filters = filters;
    }

    /// <summary>
    /// Free text terms. Each must match the package's ID, title, tags, authors, summary or description.
    /// </summary>
    public IReadOnlyList<string> Terms { get; }

    /// <summary>
    /// Field filters. Each must match its field.
    /// </summary>
    public IReadOnlyList<SearchFilter> Filters { get; }

    public bool IsEmpty => Terms.Count == 0 && Filters.Count == 0;

    public static SearchQuery Parse(string query)
    {
        var terms = new List<string>();
        var filters = new List<SearchFilter>();

        foreach (var token in Tokenize(query ?? string.Empty))
        {
            var separator = token.Field;
            if (separator != null && Fields.TryGetValue(separator, out var field))
            {
                if (token.Value.Length > 0) filters.Add(new SearchFilter(field, token.Value));
            }
            else
            {
                var term = separator == null ? token.Value : $"{separator}:{token.Value}";
                if (term.Length > 0) terms.Add(term);
            }
        }

        return new SearchQuery(terms, filters);
    }

    private static IEnumerable<(string? Field, string Value)> Tokenize(string query)
    {
        var position = 0;
        while (position < query.Length)
        {
            if (char.IsWhiteSpace(query[position]))
            {
                position++;
                continue;
            }

            // A token is either "value", field:value or field:"quoted value".
            string? field = null;
            var start = position;
            while (position < query.Length && !char.IsWhiteSpace(query[position]) && query[position] != ':' && query[position] != '"')
            {
                position++;
            }

            if (position < query.Length && query[position] == ':' && position > start)
            {
                field = query.Substring(start, position - start);
                position++;
            }
            else
            {
                position = start;
            }

            yield return (field, ReadValue(query, ref position));
        }
    }

    private static string ReadValue(string query, ref int position)
    {
        var value = new StringBuilder();

        if (position < query.Length && query[position] == '"')
        {
            position++;
            while (position < query.Length && query[position] != '"')
            {
                value.Append(query[position++]);
            }

            position++;
        }
        else
        {
            while (position < query.Length && !char.IsWhiteSpace(query[position]))
            {
                value.Append(query[position++]);
            }
        }

        return value.ToString().Trim();
    }
}

/// <param name="Field">The normalized field: id, packageid, version, title, tags, authors, description or summary.</param>
/// <param name="Value">The value the field must match.</param>
public sealed record SearchFilter(string Field, string Value);
