using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TekstilCAD.Licensing;

namespace TekstilCAD.Tests;

public class PerpetualLicensingTests
{
    private static PerpetualLicensePayload Claims=>new(Guid.NewGuid(),"Test Textile",PerpetualLicenseVerifier.ProductId,new DateTimeOffset(2026,11,1,0,0,0,TimeSpan.Zero));
    private static string B64(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static string Sign(RSA rsa,string json,string version="v2")
    {
        var content=version+"."+B64(Encoding.UTF8.GetBytes(json));
        return content+"."+B64(rsa.SignData(Encoding.ASCII.GetBytes(content),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
    }
    [Fact]
    public void SignedNonExpiringRecordNeedsNoClockOrNetwork()
    {
        using var key=RSA.Create(2048);var claims=Claims;var token=Sign(key,JsonSerializer.Serialize(claims));
        var result=PerpetualLicenseVerifier.Verify(token,key.ExportSubjectPublicKeyInfoPem());
        Assert.True(result.IsValid);Assert.Equal(LicenseFailure.None,result.Failure);Assert.Equal(claims,result.License);
    }
    [Fact]
    public void SignedIssueDateIsMetadataNotAnOfflineClockGate()
    {
        using var key=RSA.Create(2048);var claims=Claims with { IssuedUtc=new DateTimeOffset(2200,1,1,0,0,0,TimeSpan.Zero) };
        Assert.True(PerpetualLicenseVerifier.Verify(Sign(key,JsonSerializer.Serialize(claims)),key.ExportSubjectPublicKeyInfoPem()).IsValid);
    }
    [Fact]
    public void EnvelopeVersionIsSignedAndLegacyVerifierRejectsNewFormat()
    {
        using var key=RSA.Create(2048);var pem=key.ExportSubjectPublicKeyInfoPem();
        var v2=Sign(key,JsonSerializer.Serialize(Claims));
        Assert.Equal(LicenseFailure.Malformed,LicenseVerifier.Verify(v2,pem,DateTimeOffset.UtcNow).Failure);
        var v1=Sign(key,JsonSerializer.Serialize(Claims),"v1");
        Assert.Equal(LicenseFailure.Malformed,PerpetualLicenseVerifier.Verify(v1,pem).Failure);
        Assert.Equal(LicenseFailure.InvalidSignature,PerpetualLicenseVerifier.Verify("v2"+v1[2..],pem).Failure);
    }
    [Fact]
    public void WrongKeyModifiedClaimsAndPrivateKeyAreRejected()
    {
        using var key=RSA.Create(2048);using var other=RSA.Create(2048);var token=Sign(key,JsonSerializer.Serialize(Claims));
        Assert.Equal(LicenseFailure.InvalidSignature,PerpetualLicenseVerifier.Verify(token,other.ExportSubjectPublicKeyInfoPem()).Failure);
        var parts=token.Split('.');parts[1]=B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Claims)));
        Assert.Equal(LicenseFailure.InvalidSignature,PerpetualLicenseVerifier.Verify(string.Join('.',parts),key.ExportSubjectPublicKeyInfoPem()).Failure);
        Assert.Equal(LicenseFailure.InvalidKey,PerpetualLicenseVerifier.Verify(token,key.ExportPkcs8PrivateKeyPem()).Failure);
    }
    [Fact]
    public void ExtraExpiryAndDuplicateSignedPropertiesAreRejected()
    {
        using var key=RSA.Create(2048);var json=JsonSerializer.Serialize(Claims);var pem=key.ExportSubjectPublicKeyInfoPem();
        Assert.Equal(LicenseFailure.InvalidClaims,PerpetualLicenseVerifier.Verify(Sign(key,json[..^1]+",\"ExpiresUtc\":\"2027-01-01T00:00:00Z\"}"),pem).Failure);
        Assert.Equal(LicenseFailure.InvalidClaims,PerpetualLicenseVerifier.Verify(Sign(key,json[..^1]+",\"Customer\":\"Other\"}"),pem).Failure);
        Assert.Equal(LicenseFailure.InvalidClaims,PerpetualLicenseVerifier.Verify(Sign(key,"[]"),pem).Failure);
    }
    [Fact]
    public void WrongProductMissingClaimsAndInvalidMetadataAreRejected()
    {
        using var key=RSA.Create(2048);var pem=key.ExportSubjectPublicKeyInfoPem();var valid=Claims;
        foreach(var bad in new[] { valid with { LicenseId=Guid.Empty },valid with { Customer=" " },valid with { Customer="A\nB" },
            valid with { Product="Other" },valid with { IssuedUtc=new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.FromHours(3)) },valid with { IssuedUtc=default } })
            Assert.Equal(LicenseFailure.InvalidClaims,PerpetualLicenseVerifier.Verify(Sign(key,JsonSerializer.Serialize(bad)),pem).Failure);
        Assert.Equal(LicenseFailure.InvalidClaims,PerpetualLicenseVerifier.Verify(Sign(key,"{}"),pem).Failure);
    }
    [Theory]
    [InlineData("")] [InlineData("v2.!!!.@@@")] [InlineData("v2.a.a")] [InlineData("v3.a.b")]
    public void MalformedRecordsAreRejected(string token)
        =>Assert.Equal(LicenseFailure.Malformed,PerpetualLicenseVerifier.Verify(token,"invalid").Failure);
}
