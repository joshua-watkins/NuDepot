using NuGet.Frameworks;
using System.Collections.Concurrent;
using static NuGet.Frameworks.FrameworkConstants;

namespace NuDepot.Feed.Indexing;

/// <summary>
/// Finds the target frameworks a package's framework is compatible with, so searches can filter by framework.
/// </summary>
internal class FrameworkCompatibilityService : IFrameworkCompatibilityService
{
    private const string AnyFramework = "any";

    private static readonly Dictionary<string, NuGetFramework> KnownFrameworks;
    private static readonly IReadOnlyList<OneWayCompatibilityMappingEntry> CompatibilityMapping;
    private static readonly ConcurrentDictionary<NuGetFramework, IReadOnlyList<string>> CompatibleFrameworks;

    static FrameworkCompatibilityService()
    {
        HashSet<string> supportedFrameworks =
        [
            FrameworkIdentifiers.NetStandard,
            FrameworkIdentifiers.NetCoreApp,
            FrameworkIdentifiers.Net
        ];

        CompatibilityMapping = [.. DefaultFrameworkMappings.Instance.CompatibilityMappings];
        CompatibleFrameworks = new ConcurrentDictionary<NuGetFramework, IReadOnlyList<string>>();

        KnownFrameworks = typeof(CommonFrameworks)
            .GetFields()
            .Where(f => f.IsStatic)
            .Where(f => f.FieldType == typeof(NuGetFramework))
            .Select(f => (NuGetFramework)f.GetValue(null)!)
            .Where(f => supportedFrameworks.Contains(f.Framework))
            .ToDictionary(f => f.GetShortFolderName());

        // Add more frameworks missing from "CommonFrameworks"
        KnownFrameworks["net472"] = new NuGetFramework(FrameworkIdentifiers.Net, new Version(4, 7, 2, 0));
        KnownFrameworks["net471"] = new NuGetFramework(FrameworkIdentifiers.Net, new Version(4, 7, 1, 0));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> FindAllCompatibleFrameworks(string name)
    {
        if (!KnownFrameworks.TryGetValue(name, out NuGetFramework? framework))
            return [name, AnyFramework];

        return CompatibleFrameworks.GetOrAdd(framework, FindAllCompatibleFrameworks);
    }

    private IReadOnlyList<string> FindAllCompatibleFrameworks(NuGetFramework targetFramework)
    {
        HashSet<string> results = [AnyFramework];

        // Find all framework mappings that apply to the target framework
        foreach (OneWayCompatibilityMappingEntry mapping in CompatibilityMapping)
        {
            // Skip this mapping if it isn't for our target framework.
            if (!mapping.TargetFrameworkRange.Satisfies(targetFramework))
                continue;

            // Any framework that satisfies this mapping is compatible with the target framework.
            foreach (NuGetFramework possibleFramework in KnownFrameworks.Values)
            {
                if (mapping.SupportedFrameworkRange.Satisfies(possibleFramework))
                    results.Add(possibleFramework.GetShortFolderName());
            }
        }

        // Find all frameworks that have the same "framework identifier" and a version
        // less than equal to our target framework. For example, "net45" is compatible
        // with "net20" and "net45".
        foreach (NuGetFramework possibleFramework in KnownFrameworks.Values)
        {
            if (possibleFramework.Framework == targetFramework.Framework &&
                possibleFramework.Version <= targetFramework.Version)
                results.Add(possibleFramework.GetShortFolderName());
        }

        return [.. results];
    }
}
