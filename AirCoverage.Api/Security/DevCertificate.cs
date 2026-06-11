using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AirCoverage.Api.Security;

/// <summary>
/// Loads a persisted self-signed certificate, or creates one for localhost the
/// first time. Persisting the .pfx (on the mounted data volume in Docker) keeps
/// the cert stable across restarts so the browser only warns once.
///
/// This is for local/internal HTTPS only. For a real deployment, supply a proper
/// certificate via Kestrel config (Kestrel:Certificates:Default:Path/Password) and
/// this generator is bypassed.
/// </summary>
public static class DevCertificate
{
    public static X509Certificate2 LoadOrCreate(string path, string password)
    {
        if (File.Exists(path))
        {
            return new X509Certificate2(path, password, X509KeyStorageFlags.Exportable);
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // serverAuth

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(san.Build());

        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));

        var pfxBytes = cert.Export(X509ContentType.Pfx, password);

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, pfxBytes);

        return new X509Certificate2(pfxBytes, password, X509KeyStorageFlags.Exportable);
    }
}
