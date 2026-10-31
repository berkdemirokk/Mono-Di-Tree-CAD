using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TekstilCAD.Licensing;

[JsonConverter(typeof(JsonStringEnumConverter<LicensePlan>))]
public enum LicensePlan { Starter, Professional, Factory, Enterprise }
public sealed record LicensePayload(Guid LicenseId, string Company, LicensePlan Plan,
    DateTimeOffset IssuedUtc, DateTimeOffset ExpiresUtc, string? DeviceId = null);
public enum LicenseFailure { None, Malformed, InvalidKey, InvalidSignature, InvalidClaims, NotYetValid, Expired, WrongDevice }
public sealed record LicenseVerification(bool IsValid, LicenseFailure Failure, string Message, LicensePayload? License = null);

/// <summary>
/// Offline RSA-PKCS1-SHA256 verifier; never issues licenses or embeds private keys.
/// Token: v1.base64url(UTF8 JSON).base64url(signature of ASCII v1.payload).
/// Caller supplies a trusted public key, trusted UTC time and device identity.
/// Offline verification cannot establish trusted time, prevent clock rollback, or observe revocation.
/// </summary>
public static class LicenseVerifier
{
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static LicenseVerification Verify(string token, string publicKeyPem, DateTimeOffset utcNow, string? deviceId = null)
    {
        static LicenseVerification Fail(LicenseFailure reason, string text) => new(false, reason, text);
        if (utcNow.Offset != TimeSpan.Zero) return Fail(LicenseFailure.InvalidClaims, "Verification time must use UTC.");
        if (string.IsNullOrEmpty(token) || token.Length > 16_384) return Fail(LicenseFailure.Malformed, "Invalid token length.");
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != "v1") return Fail(LicenseFailure.Malformed, "Unsupported token envelope.");
        byte[] payloadBytes, signature;
        try { payloadBytes = Decode(parts[1]); signature = Decode(parts[2]); }
        catch (FormatException) { return Fail(LicenseFailure.Malformed, "Invalid base64url encoding."); }
        if (payloadBytes.Length > 8192 || signature.Length is < 256 or > 1024)
            return Fail(LicenseFailure.Malformed, "Payload or signature length is invalid.");
        if (string.IsNullOrWhiteSpace(publicKeyPem) || publicKeyPem.Length > 16_384 || publicKeyPem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            return Fail(LicenseFailure.InvalidKey, "A trusted RSA public key is required.");
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(publicKeyPem);
            if (rsa.KeySize is < 2048 or > 8192) return Fail(LicenseFailure.InvalidKey, "RSA key size must be between 2048 and 8192 bits.");
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        { return Fail(LicenseFailure.InvalidKey, "Public key could not be read."); }
        try
        {
            if (!rsa.VerifyData(Encoding.ASCII.GetBytes("v1." + parts[1]), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return Fail(LicenseFailure.InvalidSignature, "License signature is invalid.");
        }
        catch (CryptographicException) { return Fail(LicenseFailure.InvalidSignature, "License signature is invalid."); }
        LicensePayload? payload;
        try
        {
            using var json = JsonDocument.Parse(payloadBytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return Fail(LicenseFailure.InvalidClaims, "Claims must be an object.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in json.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) return Fail(LicenseFailure.InvalidClaims, "Duplicate claims are not allowed.");
            // Numeric enum values are rejected even when they map to a known plan.
            if (!json.RootElement.TryGetProperty("Plan", out var plan) || plan.ValueKind != JsonValueKind.String)
                return Fail(LicenseFailure.InvalidClaims, "Plan must be a named subscription tier.");
            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, Options);
        }
        catch (JsonException) { return Fail(LicenseFailure.InvalidClaims, "License claims are invalid."); }
        if (payload is null || payload.LicenseId == Guid.Empty || string.IsNullOrWhiteSpace(payload.Company)
            || payload.Company.Length > 200 || payload.Company.Any(char.IsControl) || !Enum.IsDefined(payload.Plan)
            || payload.IssuedUtc.Offset != TimeSpan.Zero || payload.ExpiresUtc.Offset != TimeSpan.Zero
            || payload.IssuedUtc.Year < 2020 || payload.ExpiresUtc <= payload.IssuedUtc
            || payload.ExpiresUtc - payload.IssuedUtc > TimeSpan.FromDays(366 * 10)
            || payload.DeviceId is not null && (string.IsNullOrWhiteSpace(payload.DeviceId) || payload.DeviceId.Length > 256 || payload.DeviceId.Any(char.IsControl)))
            return Fail(LicenseFailure.InvalidClaims, "License claims exceed supported bounds.");
        if (utcNow < payload.IssuedUtc) return Fail(LicenseFailure.NotYetValid, "License is not yet valid.");
        if (utcNow >= payload.ExpiresUtc) return Fail(LicenseFailure.Expired, "License has expired.");
        if (payload.DeviceId is not null && !string.Equals(payload.DeviceId, deviceId, StringComparison.Ordinal))
            return Fail(LicenseFailure.WrongDevice, "License is assigned to another device.");
        return new(true, LicenseFailure.None, "Offline signature and claims are valid.", payload);
    }
    private static byte[] Decode(string value)
    {
        if (value.Length == 0 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) || value.Length % 4 == 1)
            throw new FormatException();
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        var bytes = Convert.FromBase64String(padded);
        // Reject alternate encodings of identical bytes.
        if (Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != value) throw new FormatException();
        return bytes;
    }
}
