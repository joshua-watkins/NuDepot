namespace NuDepot.Gallery.Slices;

/// <summary>
/// What manage.js needs, for the management on <see cref="Package"/>, <see cref="Upload"/> and <see cref="Admin"/>.
/// </summary>
/// <param name="PathBase">The application's path base, which the feed's endpoints start with.</param>
/// <param name="PackageId">The package being managed, on the package page.</param>
/// <param name="Version">The version being managed, on the package page.</param>
internal record ManageScriptSettings(string PathBase, string? PackageId = null, string? Version = null);
