using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NLightning.Domain.Protocol.ValueObjects;
using Invoice = NLightning.Bolt11.Models.Invoice;

namespace LnAddress.Net.Tests.IntegrationTests;

using Fixtures;
using LnAddress.Net.Services;

[Collection("cln-regtest")]
public class ClnGrpcIntegrationTest
{
    private readonly ClnRegtestFixture _fixture;
    private readonly ClnService _clnService;

    public ClnGrpcIntegrationTest(ClnRegtestFixture fixture)
    {
        _fixture = fixture;

        var loggerMock = new Mock<ILogger<ClnService>>();

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Cln:RpcAddress", fixture.RpcAddress },
            // Base64 encoded PEM files, as documented for the environment variables
            { "Cln:CaCert", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(fixture.CaCertPem)) },
            { "Cln:ClientCert", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(fixture.ClientCertPem)) },
            // Raw PEM text is accepted too
            { "Cln:ClientKey", fixture.ClientKeyPem }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _clnService = new ClnService(configuration, loggerMock.Object);
    }

    [Fact]
    public async Task Given_SyncedNode_When_CheckConnection_Then_Expect_True()
    {
        var isConnected = await _clnService.CheckConnection();

        Assert.True(isConnected);
    }

    [Fact]
    public async Task Given_ValidAmount_When_FetchInvoice_Then_Expect_ValidInvoice()
    {
        // Arrange
        const long expectedAmount = 10_000L;
        const string expectedDescription = "payment for nGoline";

        // Act
        var invoice = (await _clnService.FetchInvoiceAsync(expectedAmount, "nGoline", null)).PaymentRequest;

        // Assert
        var decodedInvoice = Invoice.Decode(invoice, BitcoinNetwork.Regtest);
        Assert.NotNull(decodedInvoice);
        Assert.Equal(_fixture.NodePubKeyHex, Convert.ToHexStringLower(decodedInvoice.PayeePubKey?.ToBytes() ?? []));
        Assert.Equal(expectedAmount, (long)decodedInvoice.Amount.MilliSatoshi);
        Assert.Equal(expectedDescription, decodedInvoice.Description);
    }

    [Fact]
    public async Task Given_ValidAmountAndDescription_When_FetchInvoice_Then_Expect_ValidInvoice()
    {
        // Arrange
        const long expectedAmount = 10_000L;
        const string expectedDescription = "LnAddress Payment";

        // Act
        var invoice = (await _clnService.FetchInvoiceAsync(expectedAmount, "nGoline", expectedDescription)).PaymentRequest;

        // Assert
        var decodedInvoice = Invoice.Decode(invoice, BitcoinNetwork.Regtest);
        Assert.NotNull(decodedInvoice);
        Assert.Equal(_fixture.NodePubKeyHex, Convert.ToHexStringLower(decodedInvoice.PayeePubKey?.ToBytes() ?? []));
        Assert.Equal(expectedAmount, (long)decodedInvoice.Amount.MilliSatoshi);
        Assert.Equal(expectedDescription, decodedInvoice.Description);
    }

    [Fact]
    public async Task Given_SameUser_When_FetchInvoiceTwice_Then_Expect_DistinctInvoices()
    {
        // CLN rejects duplicate labels, so two requests for the same user must not collide
        var first = await _clnService.FetchInvoiceAsync(10_000, "nGoline", null);
        var second = await _clnService.FetchInvoiceAsync(10_000, "nGoline", null);

        Assert.NotEqual(first.PaymentRequest, second.PaymentRequest);
        Assert.NotEqual(first.PaymentHash, second.PaymentHash);
    }

    [Fact]
    public async Task Given_UnpaidInvoice_When_LookupInvoice_Then_Expect_NotSettled()
    {
        // Arrange
        var invoice = await _clnService.FetchInvoiceAsync(10_000, "nGoline", null);

        // Act
        var status = await _clnService.LookupInvoiceAsync(invoice.PaymentHash);

        // Assert
        Assert.NotNull(status);
        Assert.False(status.Settled);
        Assert.Null(status.Preimage);
        Assert.Equal(invoice.PaymentRequest, status.PaymentRequest);
    }

    [Fact]
    public async Task Given_PaidInvoice_When_LookupInvoice_Then_Expect_SettledWithPreimage()
    {
        // Arrange
        var invoice = await _clnService.FetchInvoiceAsync(10_000, "nGoline", null);
        var paidPreimage = await _fixture.PayAsync(invoice.PaymentRequest);

        // Act
        var status = await _clnService.LookupInvoiceAsync(invoice.PaymentHash);

        // Assert
        Assert.NotNull(status);
        Assert.True(status.Settled);
        Assert.Equal(paidPreimage, status.Preimage);
        Assert.Equal(invoice.PaymentHash, Convert.ToHexStringLower(SHA256.HashData(Convert.FromHexString(status.Preimage!))));
        Assert.Equal(invoice.PaymentRequest, status.PaymentRequest);
    }

    [Fact]
    public async Task Given_UnknownPaymentHash_When_LookupInvoice_Then_Expect_Null()
    {
        var unknownHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        var status = await _clnService.LookupInvoiceAsync(unknownHash);

        Assert.Null(status);
    }
}
