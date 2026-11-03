using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NuGetFeed.Configuration;
using NuGetFeed.Entities;
using NuGetFeed.Extensions;

namespace NuGetFeed.Users;

/// <summary>
/// Creates passkeys and signs in with them, using WebAuthn. Each ceremony has two steps: the browser
/// gets options with a challenge, then sends back what the authenticator signed. Between the steps,
/// the options are kept on the user's row, so each user has one ceremony in progress at a time.
/// </summary>
public class PasskeyService(
    UserService users,
    SystemTime time,
    IOptionsMonitor<FeedAuthorizationOptions> options)
{
    private const string ServerName = "NuGetFeed";
    private const string ExpiredError = "The passkey request expired. Try again.";

    private readonly UserService _users = users ?? throw new ArgumentNullException(nameof(users));
    private readonly SystemTime _time = time ?? throw new ArgumentNullException(nameof(time));
    private readonly IOptionsMonitor<FeedAuthorizationOptions> _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Start creating an account with a passkey.
    /// </summary>
    /// <returns>The options for the browser, or null if the user name is taken.</returns>
    public async Task<CredentialCreateOptions?> BeginSignUpAsync(HttpContext httpContext, string userName, string passkeyName, CancellationToken cancellationToken)
    {
        var handle = RandomNumberGenerator.GetBytes(32);
        var credentialOptions = RequestNewCredential(httpContext, userName, handle, Array.Empty<UserPasskey>());

        var user = await _users.StartSignUpAsync(userName, handle, credentialOptions.ToJson(), passkeyName, cancellationToken);

        return user == null ? null : credentialOptions;
    }

    /// <summary>
    /// Start adding a passkey to a signed-in user's account.
    /// </summary>
    public async Task<CredentialCreateOptions> BeginAddPasskeyAsync(HttpContext httpContext, User user, string passkeyName, CancellationToken cancellationToken)
    {
        // The authenticator won't make a second passkey for an account it already has one for.
        var existing = await _users.GetPasskeysAsync(user.Key, cancellationToken);
        var credentialOptions = RequestNewCredential(httpContext, user.UserName, user.Handle, existing);

        await _users.StartCeremonyAsync(user, PasskeyCeremony.AddPasskey, credentialOptions.ToJson(), passkeyName, cancellationToken);

        return credentialOptions;
    }

    /// <summary>
    /// Finish creating an account.
    /// </summary>
    public async Task<PasskeyResult> CompleteSignUpAsync(
        HttpContext httpContext,
        string userName,
        AuthenticatorAttestationRawResponse response,
        CancellationToken cancellationToken)
    {
        var user = await _users.FindByNameAsync(userName, cancellationToken);
        if (user == null || user.Passkeys.Count > 0) return PasskeyResult.Failed(ExpiredError);

        var passkey = await CompleteRegistrationAsync(httpContext, user, PasskeyCeremony.SignUp, response, cancellationToken);
        if (passkey.Error != null) return PasskeyResult.Failed(passkey.Error);

        await _users.CompleteSignUpAsync(user, passkey.Passkey!, cancellationToken);
        return PasskeyResult.Succeeded(user);
    }

    /// <summary>
    /// Finish adding a passkey to the signed-in user's account.
    /// </summary>
    public async Task<PasskeyResult> CompleteAddPasskeyAsync(
        HttpContext httpContext,
        User user,
        AuthenticatorAttestationRawResponse response,
        CancellationToken cancellationToken)
    {
        var passkey = await CompleteRegistrationAsync(httpContext, user, PasskeyCeremony.AddPasskey, response, cancellationToken);
        if (passkey.Error != null) return PasskeyResult.Failed(passkey.Error);

        await _users.AddPasskeyAsync(user.Key, passkey.Passkey!, cancellationToken);
        return PasskeyResult.Succeeded(user);
    }

    /// <summary>
    /// Start signing in. The browser only offers the user's passkeys.
    /// </summary>
    /// <returns>The options for the browser, or null if there's no account with the user name.</returns>
    public async Task<AssertionOptions?> BeginSignInAsync(HttpContext httpContext, string userName, CancellationToken cancellationToken)
    {
        var user = await _users.FindByNameAsync(userName, cancellationToken);
        if (user == null || user.Passkeys.Count == 0) return null;

        var assertionOptions = CreateFido2(httpContext.Request).GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = user.Passkeys.Select(ToDescriptor).ToList(),
            UserVerification = UserVerificationRequirement.Required,
        });

        await _users.StartCeremonyAsync(user, PasskeyCeremony.SignIn, assertionOptions.ToJson(), passkeyName: null, cancellationToken);

        return assertionOptions;
    }

    /// <summary>
    /// Finish signing in.
    /// </summary>
    public async Task<PasskeyResult> CompleteSignInAsync(
        HttpContext httpContext,
        AuthenticatorAssertionRawResponse response,
        CancellationToken cancellationToken)
    {
        var passkey = await _users.FindPasskeyAsync(response.RawId, cancellationToken);
        if (passkey == null)
        {
            return PasskeyResult.Failed("This passkey isn't registered. It may have been removed from your account.");
        }

        // The challenge is the passkey's user's, so another user's passkey can't answer it.
        var user = passkey.User;
        if (!Answers(user, response.Response.ClientDataJson) ||
            !await _users.TakeCeremonyAsync(user, PasskeyCeremony.SignIn, cancellationToken))
        {
            return PasskeyResult.Failed("The sign-in request expired. Try again.");
        }

        VerifyAssertionResult result;
        try
        {
            result = await CreateFido2(httpContext.Request).MakeAssertionAsync(
                new MakeAssertionParams
                {
                    AssertionResponse = response,
                    OriginalOptions = AssertionOptions.FromJson(user.CeremonyOptions!),
                    StoredPublicKey = passkey.PublicKey,
                    StoredSignatureCounter = passkey.SignCount,
                    IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(args.UserHandle.AsSpan().SequenceEqual(user.Handle)),
                },
                cancellationToken);
        }
        catch (Fido2VerificationException e)
        {
            return PasskeyResult.Failed($"The passkey couldn't be verified: {e.Message}");
        }

        await _users.RecordSignInAsync(passkey, result.SignCount, result.IsBackedUp, cancellationToken);

        return PasskeyResult.Succeeded(user);
    }

    private async Task<(UserPasskey? Passkey, string? Error)> CompleteRegistrationAsync(
        HttpContext httpContext,
        User user,
        PasskeyCeremony ceremony,
        AuthenticatorAttestationRawResponse response,
        CancellationToken cancellationToken)
    {
        if (!Answers(user, response.Response.ClientDataJson) ||
            !await _users.TakeCeremonyAsync(user, ceremony, cancellationToken))
        {
            return (null, ExpiredError);
        }

        RegisteredPublicKeyCredential credential;
        try
        {
            credential = await CreateFido2(httpContext.Request).MakeNewCredentialAsync(
                new MakeNewCredentialParams
                {
                    AttestationResponse = response,
                    OriginalOptions = CredentialCreateOptions.FromJson(user.CeremonyOptions!),
                    IsCredentialIdUniqueToUserCallback = async (args, token) => !await _users.PasskeyExistsAsync(args.CredentialId, token),
                },
                cancellationToken);
        }
        catch (Fido2VerificationException e)
        {
            return (null, $"The passkey couldn't be verified: {e.Message}");
        }

        return (new UserPasskey
        {
            CredentialId = credential.Id,
            PublicKey = credential.PublicKey,
            SignCount = credential.SignCount,
            Transports = credential.Transports?.Select(t => t.ToEnumMemberValue()).ToArray() ?? Array.Empty<string>(),
            AaGuid = credential.AaGuid,
            IsBackedUp = credential.IsBackedUp,
            Name = user.CeremonyPasskeyName ?? "Passkey",
            CreatedAt = _time.UtcNow,
        }, null);
    }

    private CredentialCreateOptions RequestNewCredential(HttpContext httpContext, string userName, byte[] handle, IEnumerable<UserPasskey> existing)
    {
        return CreateFido2(httpContext.Request).RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = handle, Name = userName, DisplayName = userName },
            ExcludeCredentials = existing.Select(ToDescriptor).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                // A discoverable credential lets the browser offer the passkey when the user name is typed.
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });
    }

    /// <summary>
    /// Whether the browser answered the user's challenge in progress. An answer to an older one, like
    /// from another tab, doesn't use up the one in progress.
    /// </summary>
    private static bool Answers(User user, byte[] clientDataJson)
    {
        if (user.CeremonyOptions == null) return false;

        try
        {
            using var options = JsonDocument.Parse(user.CeremonyOptions);
            using var clientData = JsonDocument.Parse(clientDataJson);

            return options.RootElement.GetProperty("challenge").GetString() == clientData.RootElement.GetProperty("challenge").GetString();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static PublicKeyCredentialDescriptor ToDescriptor(UserPasskey passkey)
    {
        // The transports tell the browser where to look for the passkey, like on a phone.
        var transports = Enum.GetValues<AuthenticatorTransport>()
            .Where(t => passkey.Transports?.Contains(t.ToEnumMemberValue()) == true)
            .ToArray();

        return new PublicKeyCredentialDescriptor(PublicKeyCredentialType.PublicKey, passkey.CredentialId, transports);
    }

    /// <summary>
    /// Passkeys belong to the site's domain, and the browser reports the page's origin,
    /// which must be one of the site's origins.
    /// </summary>
    private Fido2 CreateFido2(HttpRequest request)
    {
        var settings = _options.CurrentValue.Users;
        var origins = settings.Origins.Count > 0
            ? new HashSet<string>(settings.Origins, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { $"{request.Scheme}://{request.Host.ToUriComponent()}" };

        return new Fido2(new Fido2Configuration
        {
            RPID = string.IsNullOrEmpty(settings.Domain) ? request.Host.Host : settings.Domain,
            RPName = ServerName,
            Origins = origins,
        });
    }
}

/// <summary>
/// The outcome of a passkey ceremony: the user, or why it failed.
/// </summary>
public record PasskeyResult(User? User, string? Error)
{
    public static PasskeyResult Succeeded(User? user) => new(user, user == null ? "The account no longer exists." : null);

    public static PasskeyResult Failed(string error) => new(null, error);
}

public enum PasskeyCeremony
{
    SignUp,
    AddPasskey,
    SignIn,
}
