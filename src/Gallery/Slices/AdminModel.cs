namespace NuDepot.Gallery.Slices;

/// <summary>
/// The model of <see cref="Admin"/>, where administrators manage the feed's users and signing, and see its settings.
/// Its script reads them from the feed.
/// </summary>
/// <param name="PathBase">The application's path base, which links start with.</param>
internal record AdminModel(string PathBase);
