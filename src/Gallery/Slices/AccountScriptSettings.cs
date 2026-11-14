namespace NuDepot.Gallery.Slices;

/// <summary>
/// What the account slices' script needs, from <see cref="SignIn"/> and <see cref="Account"/>.
/// </summary>
/// <param name="PathBase">The application's path base, which the account endpoints start with.</param>
/// <param name="ReturnUrl">Where to go after signing in or out.</param>
/// <param name="ServiceIndexUrl">The feed's service index, for the nuget.config example.</param>
internal record AccountScriptSettings(string PathBase, string ReturnUrl, string? ServiceIndexUrl = null);
