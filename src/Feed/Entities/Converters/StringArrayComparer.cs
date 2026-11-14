using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace NuDepot.Feed.Entities.Converters;

/// <summary>
/// Compares string arrays by their items, so Entity Framework notices when an item changes.
/// </summary>
internal class StringArrayComparer : ValueComparer<string[]>
{
    /// <summary>
    /// The comparer.
    /// </summary>
    public static readonly StringArrayComparer Instance = new();

    /// <summary>
    /// Creates the comparer. Use <see cref="Instance"/>.
    /// </summary>
    public StringArrayComparer()
        : base(
            (c1, c2) => c1.SequenceEqual(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToArray())
    {
    }
}
