using System.Security.Cryptography.X509Certificates;
using System.Text;
using Google.Protobuf;
using Grpc.Net.Client;

namespace LnAddress.Net.Services;

using Cln;
using Interfaces;
using Models.Lightning;

/// <summary>
/// Core Lightning (CLN) backend. Talks to the cln-grpc plugin over mutual TLS.
/// </summary>
public class ClnService : ILightningService
{
    private readonly Node.NodeClient _rpcClient;
    private readonly ILogger<ClnService> _logger;

    public ClnService(IConfiguration configuration, ILogger<ClnService> logger)
    {
        _logger = logger;

        var caCertPem = ReadPem(configuration, "Cln:CaCert", "Cln CA certificate config is missing");
        var clientCertPem = ReadPem(configuration, "Cln:ClientCert", "Cln client certificate config is missing");
        var clientKeyPem = ReadPem(configuration, "Cln:ClientKey", "Cln client key config is missing");
        var rpcAddress = configuration["Cln:RpcAddress"] ?? throw new Exception("Cln rpc address config is missing");

        var caCert = X509Certificate2.CreateFromPem(caCertPem);
        var clientCert = LoadClientCertificate(clientCertPem, clientKeyPem);

        var httpClientHandler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            // The server certificate is issued by the node's own CA and carries "cln" as its
            // subject name, so validate the chain against that CA instead of the system store.
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => IsIssuedByCa(cert, caCert)
        };
        httpClientHandler.ClientCertificates.Add(clientCert);

        var channel = GrpcChannel.ForAddress(rpcAddress, new GrpcChannelOptions
        {
            HttpHandler = httpClientHandler
        });
        _rpcClient = new Node.NodeClient(channel);
    }

    public async Task<CreatedInvoice> FetchInvoiceAsync(long valueMillisats, string username, string? comment)
    {
        try
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(valueMillisats);

            var request = new InvoiceRequest
            {
                AmountMsat = new AmountOrAny { Amount = new Amount { Msat = (ulong)valueMillisats } },
                Description = comment ?? $"payment for {username}",
                // CLN requires a unique label per invoice
                Label = $"lnaddress-{username}-{Guid.NewGuid():N}"
            };

            var response = await _rpcClient.InvoiceAsync(request);
            return new CreatedInvoice(response.Bolt11, Convert.ToHexStringLower(response.PaymentHash.Span));
        }
        catch (Exception e)
        {
            const string errorMessage = "Error fetching invoice from server";
            _logger.LogError(e, errorMessage);
            throw new Exception(errorMessage);
        }
    }

    public async Task<InvoiceStatus?> LookupInvoiceAsync(string paymentHash)
    {
        try
        {
            var request = new ListinvoicesRequest { PaymentHash = ByteString.CopyFrom(Convert.FromHexString(paymentHash)) };
            var response = await _rpcClient.ListInvoicesAsync(request);

            var invoice = response.Invoices.FirstOrDefault();
            if (invoice is null)
            {
                return null;
            }

            var settled = invoice.Status == ListinvoicesInvoices.Types.ListinvoicesInvoicesStatus.Paid;
            var preimage = settled && invoice.HasPaymentPreimage
                ? Convert.ToHexStringLower(invoice.PaymentPreimage.Span)
                : null;
            return new InvoiceStatus(invoice.Bolt11, settled, preimage);
        }
        catch (Exception e)
        {
            const string errorMessage = "Error looking up invoice on server";
            _logger.LogError(e, errorMessage);
            throw new Exception(errorMessage);
        }
    }

    public async Task<bool> CheckConnection()
    {
        try
        {
            var response = await _rpcClient.GetinfoAsync(new GetinfoRequest());
            return !response.HasWarningBitcoindSync && !response.HasWarningLightningdSync;
        }
        catch (Exception e)
        {
            const string errorMessage = "Error fetching server info";
            _logger.LogError(e, errorMessage);
            throw new Exception(errorMessage);
        }
    }

    /// <summary>
    /// Reads a PEM value from configuration. The value may be the raw PEM text or the PEM file
    /// encoded as a single-line base64 string (the latter is friendlier for environment variables).
    /// </summary>
    private static string ReadPem(IConfiguration configuration, string key, string missingMessage)
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

    private static X509Certificate2 LoadClientCertificate(string certPem, string keyPem)
    {
        using var ephemeral = X509Certificate2.CreateFromPem(certPem, keyPem);
        // Certificates created from PEM hold an ephemeral private key, which the TLS stack on
        // Windows and macOS cannot use. Round-tripping through PKCS#12 makes the key persistable.
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null);
    }

    private static bool IsIssuedByCa(X509Certificate2? serverCert, X509Certificate2 caCert)
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
