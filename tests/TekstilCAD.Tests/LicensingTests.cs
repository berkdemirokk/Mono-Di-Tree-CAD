using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TekstilCAD.Licensing;
namespace TekstilCAD.Tests;
public class LicensingTests
{
    private static readonly DateTimeOffset Now = new(2026,10,31,0,0,0,TimeSpan.Zero);
    private static LicensePayload Valid => new(Guid.NewGuid(),"Test Textile",LicensePlan.Professional,Now.AddDays(-1),Now.AddDays(30),"test-device");
    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static string Sign(RSA rsa, LicensePayload claims) => SignJson(rsa,JsonSerializer.Serialize(claims));
    private static string SignJson(RSA rsa,string json)
    {
        var envelope="v1."+B64(Encoding.UTF8.GetBytes(json));
        return envelope+"."+B64(rsa.SignData(Encoding.ASCII.GetBytes(envelope),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
    }
    [Fact] public void ValidOfflineLicenseReturnsClaims()
    {
        using var rsa=RSA.Create(2048);var claims=Valid;
        var result=LicenseVerifier.Verify(Sign(rsa,claims),rsa.ExportSubjectPublicKeyInfoPem(),Now,"test-device");
        Assert.True(result.IsValid);Assert.Equal(claims,result.License);
    }
    [Fact] public void TamperedClaimsFailSignature()
    {
        using var rsa=RSA.Create(2048);var token=Sign(rsa,Valid);var parts=token.Split('.');
        parts[1]=B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Valid with { Plan=LicensePlan.Enterprise })));
        Assert.Equal(LicenseFailure.InvalidSignature,LicenseVerifier.Verify(string.Join('.',parts),rsa.ExportSubjectPublicKeyInfoPem(),Now,"test-device").Failure);
    }
    [Fact] public void WrongSigningKeyRejected()
    {
        using var issuer=RSA.Create(2048);using var other=RSA.Create(2048);
        Assert.Equal(LicenseFailure.InvalidSignature,LicenseVerifier.Verify(Sign(issuer,Valid),other.ExportSubjectPublicKeyInfoPem(),Now,"test-device").Failure);
    }
    [Theory] [InlineData(-2,LicenseFailure.Expired)] [InlineData(31,LicenseFailure.NotYetValid)]
    public void ValidityWindowEnforced(int days,LicenseFailure expected)
    {
        using var rsa=RSA.Create(2048); var claims=days<0?Valid with {IssuedUtc=Now.AddDays(-10),ExpiresUtc=Now.AddDays(days)}:Valid with {IssuedUtc=Now.AddDays(days),ExpiresUtc=Now.AddDays(50)};
        Assert.Equal(expected,LicenseVerifier.Verify(Sign(rsa,claims),rsa.ExportSubjectPublicKeyInfoPem(),Now,"test-device").Failure);
    }
    [Fact] public void ExpiryInstantIsExclusive()
    {
        using var rsa=RSA.Create(2048);var claims=Valid with {ExpiresUtc=Now};
        Assert.Equal(LicenseFailure.Expired,LicenseVerifier.Verify(Sign(rsa,claims),rsa.ExportSubjectPublicKeyInfoPem(),Now,"test-device").Failure);
    }
    [Fact] public void DeviceBindingIsExact()
    {
        using var rsa=RSA.Create(2048);
        Assert.Equal(LicenseFailure.WrongDevice,LicenseVerifier.Verify(Sign(rsa,Valid),rsa.ExportSubjectPublicKeyInfoPem(),Now,"TEST-DEVICE").Failure);
    }
    [Fact] public void UnboundLicenseAllowsAnyDevice()
    {
        using var rsa=RSA.Create(2048);
        Assert.True(LicenseVerifier.Verify(Sign(rsa,Valid with {DeviceId=null}),rsa.ExportSubjectPublicKeyInfoPem(),Now).IsValid);
    }
    [Fact] public void PrivateKeyInputRejected()
    {
        using var rsa=RSA.Create(2048);
        Assert.Equal(LicenseFailure.InvalidKey,LicenseVerifier.Verify(Sign(rsa,Valid),rsa.ExportPkcs8PrivateKeyPem(),Now,"test-device").Failure);
    }
    [Theory] [InlineData("")] [InlineData("v2.a.b")] [InlineData("v1.!!!.@@@")] [InlineData("v1.a.a")]
    public void MalformedTokensReturnFailure(string token) => Assert.Equal(LicenseFailure.Malformed,LicenseVerifier.Verify(token,"invalid",Now).Failure);
    [Fact] public void SignedInvalidCompanyRejected()
    {
        using var rsa=RSA.Create(2048);
        Assert.Equal(LicenseFailure.InvalidClaims,LicenseVerifier.Verify(Sign(rsa,Valid with {Company=""}),rsa.ExportSubjectPublicKeyInfoPem(),Now,"test-device").Failure);
    }
    [Fact] public void DuplicateSignedClaimsRejected()
    {
        using var rsa=RSA.Create(2048);var json=JsonSerializer.Serialize(Valid);json=json[..^1]+",\"Company\":\"Other\"}";
        Assert.Equal(LicenseFailure.InvalidClaims,LicenseVerifier.Verify(SignJson(rsa,json),rsa.ExportSubjectPublicKeyInfoPem(),Now,"test-device").Failure);
    }
    [Fact] public void NumericPlanRejected()
    {
        using var rsa=RSA.Create(2048);var json=JsonSerializer.Serialize(Valid).Replace("\"Professional\"","1");
        Assert.Equal(LicenseFailure.InvalidClaims,LicenseVerifier.Verify(SignJson(rsa,json),rsa.ExportSubjectPublicKeyInfoPem(),Now,"test-device").Failure);
    }
}
