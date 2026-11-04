namespace NuDepot.Feed.Endpoints;

/// <summary>
/// Who is signed in, and whether they can sign up.
/// </summary>
public class AccountResponse
{
    /// <summary>
    /// Whether someone is signed in.
    /// </summary>
    public bool SignedIn { get; set; }

    /// <summary>
    /// The signed-in user's name.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// The signed-in user's role.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Whether someone who isn't signed in can create an account.
    /// </summary>
    public bool CanSignUp { get; set; }

    /// <summary>
    /// Whether nobody has an account yet. The first account is an Admin.
    /// </summary>
    public bool IsFirstUser { get; set; }
}

/// <summary>
/// The body of a request to start creating a passkey: for a new account, or for the signed-in user.
/// </summary>
public class PasskeyCreationRequest
{
    /// <summary>
    /// The new account's user name. Not used when adding a passkey to the signed-in user's account.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// A name for the passkey, like "Laptop" or "Password manager".
    /// </summary>
    public string? PasskeyName { get; set; }
}

/// <summary>
/// The body of a request to start signing in.
/// </summary>
public class SignInRequest
{
    /// <summary>
    /// The name of the account to sign in to. If empty, the browser offers any of the site's passkeys.
    /// </summary>
    public string? UserName { get; set; }
}

/// <summary>
/// One of the signed-in user's passkeys.
/// </summary>
public class PasskeyResponse
{
    /// <summary>
    /// The passkey's ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The name the user gave the passkey.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// When the passkey was created, in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the passkey was last used to sign in, in UTC.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Whether the passkey is synced, for example by a password manager.
    /// </summary>
    public bool IsBackedUp { get; set; }
}

/// <summary>
/// The body of a request to create an API key.
/// </summary>
public class ApiKeyCreationRequest
{
    /// <summary>
    /// A name for the key, like "Build server".
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>
/// One of the signed-in user's API keys.
/// </summary>
public class ApiKeyResponse
{
    /// <summary>
    /// The key's ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The name the user gave the key.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// The start of the key, to recognize it.
    /// </summary>
    public string Prefix { get; set; } = null!;

    /// <summary>
    /// When the key was created, in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the key was last used, in UTC.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// The key itself. Only returned when it's created.
    /// </summary>
    public string? Key { get; set; }
}
