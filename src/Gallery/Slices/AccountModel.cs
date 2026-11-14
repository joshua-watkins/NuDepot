namespace NuDepot.Gallery.Slices;

/// <summary>
/// The model of <see cref="Account"/>, the signed-in user's account: their passkeys and API keys.
/// </summary>
/// <param name="PathBase">The application's path base, which links start with.</param>
/// <param name="ServiceIndexUrl">The feed's service index, for the nuget.config example.</param>
internal record AccountModel(string PathBase, string ServiceIndexUrl);
