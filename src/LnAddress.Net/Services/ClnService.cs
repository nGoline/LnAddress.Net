using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Grpc.Net.Client;

namespace LnAddress.Net.Services;

using Cln;
using Helpers;
using Interfaces;
using Models.Lightning;

/// <summary>
/// Core Lightning (CLN) backend over the cln-grpc plugin and mutual TLS. cln-grpc can't restrict what a client
/// certificate may do, so prefer <see cref="ClnRestService"/> and a restricted rune.
/// </summary>
public class ClnService : ILightningService
{
    private readonly Node.NodeClient _rpcClient;
    private readonly ILogger<ClnService> _logger;

    public ClnService(IConfiguration configuration, ILogger<ClnService> logger)
    {
        _logger = logger;

        var caCertPem = CertificateHelpers.ReadPem(configuration, "Cln:CaCert", "Cln CA certificate config is missing");
        var clientCertPem = CertificateHelpers.ReadPem(configuration, "Cln:ClientCert", "Cln client certificate config is missing");
        var clientKeyPem = CertificateHelpers.ReadPem(configuration, "Cln:ClientKey", "Cln client key config is missing");
        var rpcAddress = configuration["Cln:RpcAddress"] ?? throw new Exception("Cln rpc address config is missing");

        var caCert = X509Certificate2.CreateFromPem(caCertPem);
        var clientCert = LoadClientCertificate(clientCertPem, clientKeyPem);

        var httpClientHandler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => CertificateHelpers.IsIssuedByCa(cert, caCert)
        };
        httpClientHandler.ClientCertificates.Add(clientCert);

        var channel = GrpcChannel.ForAddress(rpcAddress, new GrpcChannelOptions
        {
            HttpHandler = httpClientHandler
        });
        _rpcClient = new Node.NodeClient(channel);
    }

    public async Task<CreatedInvoice> FetchInvoiceAsync(long valueMillisats, string username, string metadata, string? comment)
    {
        try
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(valueMillisats);

            var request = new InvoiceRequest
            {
                AmountMsat = new AmountOrAny { Amount = new Amount { Msat = (ulong)valueMillisats } },
                // With deschashonly, CLN puts sha256(description) in the invoice instead of the description
                Description = metadata,
                Deschashonly = true,
                Label = ClnInvoiceLabel.Create(username, comment)
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

            // Only answer for invoices LnAddress created, so verify doesn't expose other invoices on the node
            var invoice = response.Invoices.FirstOrDefault();
            if (invoice is null || !ClnInvoiceLabel.IsLnAddress(invoice.Label))
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

    private static X509Certificate2 LoadClientCertificate(string certPem, string keyPem)
    {
        using var ephemeral = X509Certificate2.CreateFromPem(certPem, keyPem);
        // Certificates created from PEM hold an ephemeral private key, which the TLS stack on
        // Windows and macOS cannot use. Round-tripping through PKCS#12 makes the key persistable.
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null);
    }
}
