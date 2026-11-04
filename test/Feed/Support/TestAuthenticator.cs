using Microsoft.AspNetCore.WebUtilities;
using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NuDepot.Feed.Tests.Support;

/// <summary>
/// A software passkey authenticator that answers the browser's WebAuthn calls, so tests can sign up
/// and sign in without a browser. It holds one ES256 (P-256) credential, and uses "none" attestation.
/// </summary>
internal sealed class TestAuthenticator(string origin = "http://localhost") : IDisposable
{
    private const byte UserPresent = 0x01;
    private const byte UserVerified = 0x04;
    private const byte AttestedCredentialData = 0x40;

    private ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private string Origin { get; } = origin;
    private uint SignCount { get; set; }

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(16);

    /// <summary>
    /// The user handle the credential was created for.
    /// </summary>
    public byte[]? UserHandle { get; private set; }

    public void Dispose() => Key.Dispose();

    /// <summary>
    /// Answer navigator.credentials.create() with a new credential, as the browser sends it to the server.
    /// </summary>
    /// <param name="optionsJson">The options from the server.</param>
    public string CreateCredential(string optionsJson)
    {
        JsonNode options = JsonNode.Parse(optionsJson)!;
        string rpId = options["rp"]!["id"]!.GetValue<string>();
        UserHandle = WebEncoders.Base64UrlDecode(options["user"]!["id"]!.GetValue<string>());

        byte[] clientData = ClientData("webauthn.create", options["challenge"]!.GetValue<string>());

        using MemoryStream authenticatorData = new();
        WriteAuthenticatorDataHeader(authenticatorData, rpId, UserPresent | UserVerified | AttestedCredentialData);

        // The attested credential data: the authenticator's model (none), the credential ID and its public key.
        authenticatorData.Write(new byte[16]);
        byte[] idLength = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(idLength, (ushort)CredentialId.Length);
        authenticatorData.Write(idLength);
        authenticatorData.Write(CredentialId);
        authenticatorData.Write(CosePublicKey());

        CborWriter attestation = new(CborConformanceMode.Ctap2Canonical);
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt");
        attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt");
        attestation.WriteStartMap(0);
        attestation.WriteEndMap();
        attestation.WriteTextString("authData");
        attestation.WriteByteString(authenticatorData.ToArray());
        attestation.WriteEndMap();

        return Credential(new JsonObject
        {
            ["clientDataJSON"] = Base64Url(clientData),
            ["attestationObject"] = Base64Url(attestation.Encode()),
            ["transports"] = new JsonArray("internal"),
        });
    }

    /// <summary>
    /// Answer navigator.credentials.get() by signing the server's challenge, as the browser sends it to the server.
    /// </summary>
    /// <param name="optionsJson">The options from the server.</param>
    public string GetAssertion(string optionsJson)
    {
        JsonNode options = JsonNode.Parse(optionsJson)!;
        string rpId = options["rpId"]!.GetValue<string>();
        byte[] clientData = ClientData("webauthn.get", options["challenge"]!.GetValue<string>());

        using MemoryStream authenticatorData = new();
        WriteAuthenticatorDataHeader(authenticatorData, rpId, UserPresent | UserVerified);

        byte[] signed = new byte[authenticatorData.Length + 32];
        authenticatorData
            .ToArray()
            .CopyTo(signed, 0);
        SHA256
            .HashData(clientData)
            .CopyTo(signed, authenticatorData.Length);

        return Credential(new JsonObject
        {
            ["clientDataJSON"] = Base64Url(clientData),
            ["authenticatorData"] = Base64Url(authenticatorData.ToArray()),
            ["signature"] = Base64Url(Key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)),
            ["userHandle"] = Base64Url(UserHandle ?? throw new InvalidOperationException("Create the credential first.")),
        });
    }

    private byte[] ClientData(string type, string challenge)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new JsonObject
        {
            ["type"] = type,
            ["challenge"] = challenge,
            ["origin"] = Origin,
            ["crossOrigin"] = false,
        });
    }

    private void WriteAuthenticatorDataHeader(Stream stream, string rpId, int flags)
    {
        stream.Write(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        stream.WriteByte((byte)flags);

        byte[] signCount = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(signCount, ++SignCount);
        stream.Write(signCount);
    }

    private byte[] CosePublicKey()
    {
        ECParameters parameters = Key.ExportParameters(includePrivateParameters: false);
        CborWriter writer = new(CborConformanceMode.Ctap2Canonical);

        writer.WriteStartMap(5);
        writer.WriteInt32(1);   // Key type: EC2
        writer.WriteInt32(2);
        writer.WriteInt32(3);   // Algorithm: ES256
        writer.WriteInt32(-7);
        writer.WriteInt32(-1);  // Curve: P-256
        writer.WriteInt32(1);
        writer.WriteInt32(-2);  // X
        writer.WriteByteString(parameters.Q.X!);
        writer.WriteInt32(-3);  // Y
        writer.WriteByteString(parameters.Q.Y!);
        writer.WriteEndMap();

        return writer.Encode();
    }

    private string Credential(JsonObject response)
    {
        string id = Base64Url(CredentialId);

        return new JsonObject
        {
            ["id"] = id,
            ["rawId"] = id,
            ["type"] = "public-key",
            ["clientExtensionResults"] = new JsonObject(),
            ["response"] = response,
        }.ToJsonString();
    }

    private static string Base64Url(byte[] value) => WebEncoders.Base64UrlEncode(value);
}
