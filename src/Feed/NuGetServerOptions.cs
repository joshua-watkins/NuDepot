using System;
using System.IO;

namespace NuGet.Feed;

public class NuGetServerOptions
{
    public const string DefaultSection = "NuGetServer";

    /// <summary>
    /// Determines if an Api Key is required to push\delete packages from the server.
    /// </summary>
    public bool RequireApiKey { get; set; } = true;
    /// <summary>
    /// Set the value here to allow people to push/delete packages from the server.
    /// NOTE: This is a shared key(password) for all users.
    /// </summary>
    public string ApiKey { get; set; } = Guid.NewGuid().ToString();
    /// <summary>
    /// Change the path to the packages folder. /packages.
    /// </summary>
    public string PackagesPath { get; set; } = $"{Path.DirectorySeparatorChar}packages";
    /// <summary>
    /// Change the name of the internal cache file. Default is machine name.
    /// This is the name of the cache file in the packages folder. No paths allowed.
    /// </summary>
    public string CacheFileName { get; set; } = Environment.MachineName; // TODO: Why machine name? How about nugetcache.sql or whatever
    /// <summary>
    /// Allow overwriting packages with same id + version.
    /// Set to false to mimic NuGet.org's behaviour.
    /// </summary>
    public bool EnableOverwrites { get; set; } = false;
    /// <summary>
    /// Allow serving symbol packages.
    /// Set to false to ignore files named `.symbols.nupkg` or packages containing a `/src` folder.
    /// </summary>
    public bool EnableSymbolsPackages { get; set; } = true;
    /// <summary>
    /// Allows delist instead of delete as a result of the `nuget delete` command.
    /// If enabled, `nuget delete` will remove packages from `nuget list` command, but `nuget install packageid -version version` will succeed.
    /// If disabled, `nuget delete` will delete packages from the file system and cache.
    /// </summary>
    public bool EnableDelisting { get; set; } = true;
    /// <summary>
    /// Allows packages to be filtered by their supported frameworks during search.
    /// </summary>
    public bool EnableFrameworkFiltering { get; set; } = true;
    /// <summary>
    /// Allows monitoring for file system changes and updating the cache.
    /// Disabling monitoring may result in reduced performance to check cache integrity.
    /// </summary>
    public bool EnableFileSystemMonitors { get; set; } = true;
    /// <summary>
    /// Allows remote hosts to call `clear cache` and similar operations.
    /// </summary>
    public bool EnableRemoteCacheManagement { get; set; } = false; // TODO: Are we even going to allow this?
    /// <summary>
    /// Number of seconds to wait before starting the cache rebuild timer.
    /// </summary>
    public int CacheRebuildTimerSeconds { get; set; } = 15; // TODO: probably don't need this.
    /// <summary>
    /// Frequency in minutes to rebuild the cache.
    /// </summary>
    public int CacheRebuildFrequencyMinutes { get; set; } = 60; // TODO: probably don't need this.
}
