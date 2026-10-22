using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

namespace LnAddress.Net.Services;

using Helpers;
using Interfaces;
using Models.Lightning;

/// <summary>
/// Core Lightning (CLN) backend over the clnrest plugin, authenticated with a rune. Unlike cln-grpc client
/// certificates, a rune can be restricted to the methods and parameters LnAddress needs.
/// </summary>
public class ClnRestService : ILightningService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ClnRestService> _logger;

    public ClnRestService(IConfiguration configuration, ILogger<ClnRestService> logger)
        : this(configuration, logger, null)
    {
    }

    /// <param name="configuration">The app configuration</param>
    /// <param name="logger">The logger</param>
    /// <param name="httpHandler">Replaces the HTTP handler, so tests can stub the server</param>
    public ClnRestService(IConfiguration configuration, ILogger<ClnRestService> logger, HttpMessageHandler? httpHandler)
    {
        _logger = logger;

        var url = configuration["ClnRest:Url"] ?? throw new Exception("ClnRest url config is missing");
        var rune = configuration["ClnRest:Rune"] ?? throw new Exception("ClnRest rune config is missing");

        if (httpHandler is null)
        {
            var httpClientHandler = new HttpClientHandler();
            // clnrest serves a certificate issued by the node's own CA. Without that CA configured, the
            // certificate must be trusted by the system store (e.g. when a proxy with a public certificate is in front)
            if (configuration["ClnRest:CaCert"] is not null)
            {
                var caCert = X509Certificate2.CreateFromPem(
                    CertificateHelpers.ReadPem(configuration, "ClnRest:CaCert", "ClnRest CA certificate config is missing"));
                httpClientHandler.ServerCertificateCustomValidationCallback =
                    (_, cert, _, _) => CertificateHelpers.IsIssuedByCa(cert, caCert);
            }

            httpHandler = httpClientHandler;
        }

        _httpClient = new HttpClient(httpHandler) { BaseAddress = new Uri($"{url.TrimEnd('/')}/v1/") };
        _httpClient.DefaultRequestHeaders.Add("Rune", rune);
    }

    public async Task<CreatedInvoice> FetchInvoiceAsync(long valueMillisats, string username, string metadata, string? comment)
    {
        try
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(valueMillisats);

            // With deschashonly, CLN puts sha256(description) in the invoice instead of the description
            var response = await CallAsync<InvoiceResponse>("invoice",
                new InvoiceRequest(valueMillisats, ClnInvoiceLabel.Create(username, comment), metadata, true));
            return new CreatedInvoice(response.Bolt11, response.PaymentHash);
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
            var response = await CallAsync<ListInvoicesResponse>("listinvoices", new ListInvoicesRequest(paymentHash));

            // Only answer for invoices LnAddress created, so verify doesn't expose other invoices on the node
            var invoice = response.Invoices.FirstOrDefault();
            if (invoice?.Bolt11 is null || !ClnInvoiceLabel.IsLnAddress(invoice.Label))
            {
                return null;
            }

            var settled = invoice.Status == "paid";
            return new InvoiceStatus(invoice.Bolt11, settled, settled ? invoice.PaymentPreimage : null);
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
            var response = await CallAsync<GetInfoResponse>("getinfo", new { });
            return response.WarningBitcoindSync is null && response.WarningLightningdSync is null;
        }
        catch (Exception e)
        {
            const string errorMessage = "Error fetching server info";
            _logger.LogError(e, errorMessage);
            throw new Exception(errorMessage);
        }
    }

    private async Task<T> CallAsync<T>(string method, object request)
    {
        using var response = await _httpClient.PostAsJsonAsync(method, request);
        if (!response.IsSuccessStatusCode)
        {
            // clnrest answers 401 or 403 when the rune is missing or doesn't allow the call
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception($"clnrest {method} failed with HTTP {(int)response.StatusCode}: {body}");
        }

        return await response.Content.ReadFromJsonAsync<T>()
               ?? throw new Exception($"clnrest {method} returned an empty response");
    }

    private record InvoiceRequest(
        [property: JsonPropertyName("amount_msat")] long AmountMsat,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("deschashonly")] bool DescHashOnly);

    private record InvoiceResponse(
        [property: JsonPropertyName("bolt11")] string Bolt11,
        [property: JsonPropertyName("payment_hash")] string PaymentHash);

    private record ListInvoicesRequest([property: JsonPropertyName("payment_hash")] string PaymentHash);

    private record ListInvoicesResponse([property: JsonPropertyName("invoices")] List<ListedInvoice> Invoices);

    private record ListedInvoice(
        [property: JsonPropertyName("label")] string? Label,
        [property: JsonPropertyName("bolt11")] string? Bolt11,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("payment_preimage")] string? PaymentPreimage);

    private record GetInfoResponse(
        [property: JsonPropertyName("warning_bitcoind_sync")] string? WarningBitcoindSync,
        [property: JsonPropertyName("warning_lightningd_sync")] string? WarningLightningdSync);
}
