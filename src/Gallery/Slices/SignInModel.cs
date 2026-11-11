namespace NuDepot.Gallery.Slices;

/// <summary>
/// The model of <see cref="SignIn"/>, where people sign in with a passkey, or create an account.
/// </summary>
/// <param name="PathBase">The application's path base, which links start with.</param>
/// <param name="ReturnUrl">Where to go after signing in.</param>
public record SignInModel(string PathBase, string ReturnUrl);
