using System;

namespace NuGet.Feed.Endpoints;

/// <summary>
/// Who is signed in, and whether they can sign up.
/// </summary>
public class AccountResponse
{
    public bool SignedIn { get; set; }

    public string? UserName { get; set; }

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
    public string? UserName { get; set; }
}

public class PasskeyResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

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
    public string? Name { get; set; }
}

public class ApiKeyResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>
    /// The start of the key, to recognize it.
    /// </summary>
    public string Prefix { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// The key itself. Only returned when it's created.
    /// </summary>
    public string? Key { get; set; }
}
