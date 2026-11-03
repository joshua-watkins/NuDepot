using System.Collections.Generic;
using System.Threading.Tasks;

namespace NuGet.Feed.Upstream;

public interface IPackageDownloadsSource
{
    Task<Dictionary<string, Dictionary<string, long>>> GetPackageDownloadsAsync();
}
