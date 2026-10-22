using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace LnAddress.Net.Tests.Services;

using LnAddress.Net.Services;

public class ClnRestServiceTests
{
    private const string Rune = "test-rune";
    private const string PaymentHash = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";
    private const string Preimage = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
    private const string Metadata = """[["text/plain","Paying alice@ln.example.com"]]""";

    private readonly StubHandler _handler = new();

    private ClnRestService BuildService(string url = "https://cln.internal:3010") =>
        new(BuildConfiguration(new Dictionary<string, string?> { { "ClnRest:Url", url }, { "ClnRest:Rune", Rune } }),
            new Mock<ILogger<ClnRestService>>().Object, _handler);

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    [Theory]
    [InlineData("https://cln.internal:3010")]
    [InlineData("https://cln.internal:3010/")]
    public async Task Given_Amount_When_FetchInvoice_Then_Expect_RuneAndDescriptionHashRequest(string url)
    {
        _handler.Respond(HttpStatusCode.Created, $$"""{"bolt11":"lnbcrt1","payment_hash":"{{PaymentHash}}"}""");

        var invoice = await BuildService(url).FetchInvoiceAsync(10_000, "alice", Metadata, null);

        Assert.Equal("lnbcrt1", invoice.PaymentRequest);
        Assert.Equal(PaymentHash, invoice.PaymentHash);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://cln.internal:3010/v1/invoice", request.Uri);
        Assert.Equal(Rune, request.Rune);
        var body = JsonDocument.Parse(request.Body).RootElement;
        Assert.Equal(10_000, body.GetProperty("amount_msat").GetInt64());
        Assert.Equal(Metadata, body.GetProperty("description").GetString());
        Assert.True(body.GetProperty("deschashonly").GetBoolean());
        Assert.Matches("^lnaddress-alice-[0-9a-f]{32}$", body.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Given_Comment_When_FetchInvoice_Then_Expect_CommentInLabel()
    {
        _handler.Respond(HttpStatusCode.Created, $$"""{"bolt11":"lnbcrt1","payment_hash":"{{PaymentHash}}"}""");

        await BuildService().FetchInvoiceAsync(10_000, "alice", Metadata, "gm");

        var body = JsonDocument.Parse(Assert.Single(_handler.Requests).Body).RootElement;
        Assert.Matches("^lnaddress-alice-[0-9a-f]{32}: gm$", body.GetProperty("label").GetString());
        Assert.Equal(Metadata, body.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Given_RuneRejected_When_FetchInvoice_Then_Expect_Exception()
    {
        _handler.Respond(HttpStatusCode.Unauthorized, """{"code":1502,"message":"Not permitted"}""");

        var exception = await Assert.ThrowsAsync<Exception>(() => BuildService().FetchInvoiceAsync(10_000, "alice", Metadata, null));

        Assert.Equal("Error fetching invoice from server", exception.Message);
    }

    [Fact]
    public async Task Given_PaidLnAddressInvoice_When_LookupInvoice_Then_Expect_SettledWithPreimage()
    {
        _handler.Respond(HttpStatusCode.Created, $$"""
            {"invoices":[{"label":"lnaddress-alice-1","bolt11":"lnbcrt1","status":"paid","payment_preimage":"{{Preimage}}"}]}
            """);

        var status = await BuildService().LookupInvoiceAsync(PaymentHash);

        Assert.NotNull(status);
        Assert.True(status.Settled);
        Assert.Equal(Preimage, status.Preimage);
        Assert.Equal("lnbcrt1", status.PaymentRequest);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal("https://cln.internal:3010/v1/listinvoices", request.Uri);
        Assert.Equal(PaymentHash, JsonDocument.Parse(request.Body).RootElement.GetProperty("payment_hash").GetString());
    }

    [Theory]
    [InlineData("unpaid")]
    [InlineData("expired")]
    public async Task Given_UnpaidLnAddressInvoice_When_LookupInvoice_Then_Expect_NotSettled(string invoiceStatus)
    {
        _handler.Respond(HttpStatusCode.Created, $$"""
            {"invoices":[{"label":"lnaddress-alice-1","bolt11":"lnbcrt1","status":"{{invoiceStatus}}"}]}
            """);

        var status = await BuildService().LookupInvoiceAsync(PaymentHash);

        Assert.NotNull(status);
        Assert.False(status.Settled);
        Assert.Null(status.Preimage);
    }

    [Theory]
    [InlineData("""{"invoices":[]}""")]
    [InlineData("""{"invoices":[{"label":"rtl-1","bolt11":"lnbcrt1","status":"paid","payment_preimage":"00"}]}""")]
    public async Task Given_UnknownOrForeignInvoice_When_LookupInvoice_Then_Expect_Null(string response)
    {
        _handler.Respond(HttpStatusCode.Created, response);

        Assert.Null(await BuildService().LookupInvoiceAsync(PaymentHash));
    }

    [Theory]
    [InlineData("""{"id":"02aa"}""", true)]
    [InlineData("""{"id":"02aa","warning_bitcoind_sync":"Bitcoind is not up-to-date with network."}""", false)]
    [InlineData("""{"id":"02aa","warning_lightningd_sync":"Still loading latest blocks from bitcoind."}""", false)]
    public async Task Given_GetInfo_When_CheckConnection_Then_Expect_SyncState(string response, bool expected)
    {
        _handler.Respond(HttpStatusCode.Created, response);

        Assert.Equal(expected, await BuildService().CheckConnection());
        Assert.Equal("https://cln.internal:3010/v1/getinfo", Assert.Single(_handler.Requests).Uri);
    }

    [Theory]
    [InlineData("ClnRest:Url", "ClnRest url config is missing")]
    [InlineData("ClnRest:Rune", "ClnRest rune config is missing")]
    public void Given_MissingSetting_When_Constructing_Then_Expect_DescriptiveException(string missingKey, string expectedMessage)
    {
        var settings = new Dictionary<string, string?> { { "ClnRest:Url", "https://localhost:3010" }, { "ClnRest:Rune", Rune } };
        settings.Remove(missingKey);

        var exception = Assert.Throws<Exception>(() =>
            new ClnRestService(BuildConfiguration(settings), new Mock<ILogger<ClnRestService>>().Object));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public void Given_InvalidCaCert_When_Constructing_Then_Expect_DescriptiveException()
    {
        var settings = new Dictionary<string, string?>
        {
            { "ClnRest:Url", "https://localhost:3010" },
            { "ClnRest:Rune", Rune },
            { "ClnRest:CaCert", "not base64 and not pem!" }
        };

        var exception = Assert.Throws<Exception>(() =>
            new ClnRestService(BuildConfiguration(settings), new Mock<ILogger<ClnRestService>>().Object));

        Assert.Equal("ClnRest:CaCert must be PEM text or a base64 encoded PEM file", exception.Message);
    }

    private class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private string _body = "{}";

        public List<(HttpMethod Method, string Uri, string? Rune, string Body)> Requests { get; } = [];

        public void Respond(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var rune = request.Headers.TryGetValues("Rune", out var values) ? values.Single() : null;
            Requests.Add((request.Method, request.RequestUri!.ToString(), rune, body));

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}
