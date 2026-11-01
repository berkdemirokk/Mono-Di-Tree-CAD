using System.Text.Json;
using System.Text.Json.Serialization;

namespace TekstilCAD.Licensing;

/// <summary>Signed offline entitlement metadata; update and support rights are not represented.</summary>
public sealed record PerpetualLicensePayload(Guid LicenseId,string Customer,string Product,DateTimeOffset IssuedUtc);
public sealed record PerpetualLicenseVerification(bool IsValid,LicenseFailure Failure,string Message,PerpetualLicensePayload? License=null);

/// <summary>
/// Verifies a v2 signed, non-expiring record using a caller-provisioned public key.
/// No clock, device identity, seat enforcement, revocation, issuance or payment is provided.
/// This library is not integrated into the desktop application.
/// </summary>
public static class PerpetualLicenseVerifier
{
    public const string ProductId="MonoDiTreeCAD";
    private static readonly JsonSerializerOptions Options=new() { UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow };
    public static PerpetualLicenseVerification Verify(string token,string publicKeyPem)
    {
        static PerpetualLicenseVerification Fail(LicenseFailure failure,string message)=>new(false,failure,message);
        var error=LicenseVerifier.VerifyEnvelope(token,publicKeyPem,"v2",out var bytes);
        if(error is not null)return Fail(error.Failure,error.Message);
        PerpetualLicensePayload? payload;
        try
        {
            using var document=JsonDocument.Parse(bytes);
            if(document.RootElement.ValueKind!=JsonValueKind.Object)return Fail(LicenseFailure.InvalidClaims,"Claims must be an object.");
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in document.RootElement.EnumerateObject())
                if(!names.Add(property.Name))return Fail(LicenseFailure.InvalidClaims,"Duplicate claims are not allowed.");
            payload=JsonSerializer.Deserialize<PerpetualLicensePayload>(bytes,Options);
        }
        catch(JsonException) { return Fail(LicenseFailure.InvalidClaims,"License claims are invalid."); }
        if(payload is null || payload.LicenseId==Guid.Empty || string.IsNullOrWhiteSpace(payload.Customer) || payload.Customer.Length>200 ||
           payload.Customer.Any(char.IsControl) || payload.Product!=ProductId || payload.IssuedUtc.Offset!=TimeSpan.Zero || payload.IssuedUtc.Year<2020)
            return Fail(LicenseFailure.InvalidClaims,"License claims exceed supported bounds or identify another product.");
        return new(true,LicenseFailure.None,"Offline signature and non-expiring record are valid.",payload);
    }
}
