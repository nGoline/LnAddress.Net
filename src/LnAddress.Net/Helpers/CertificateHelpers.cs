using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LnAddress.Net.Helpers;

public static class CertificateHelpers
{
    /// <summary>
    /// Reads a PEM value from configuration. The value may be the raw PEM text or the PEM file
    /// encoded as a single-line base64 string (the latter is friendlier for environment variables).
    /// </summary>
    public static string ReadPem(IConfiguration configuration, string key, string missingMessage)
    {
        var value = configuration[key] ?? throw new Exception(missingMessage);
        value = value.Trim();

        if (value.StartsWith("-----BEGIN", StringComparison.Ordinal))
        {
            return value;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            throw new Exception($"{key} must be PEM text or a base64 encoded PEM file");
        }
    }

    /// <summary>
    /// Core Lightning issues its server certificates from its own CA with "cln" as the subject name, so validate
    /// the chain against that CA instead of the system store, and don't match the hostname.
    /// </summary>
    public static bool IsIssuedByCa(X509Certificate2? serverCert, X509Certificate2 caCert)
    {
        if (serverCert is null)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(caCert);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(serverCert);
    }
}
