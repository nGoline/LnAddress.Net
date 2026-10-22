using System.Security.Cryptography;
using NLightning.Domain.Protocol.ValueObjects;
using Invoice = NLightning.Bolt11.Models.Invoice;

namespace LnAddress.Net.Tests.IntegrationTests;

using Fixtures;
using LnAddress.Net.Helpers;
using LnAddress.Net.Interfaces;

/// <summary>
/// Tests a Core Lightning backend against the regtest nodes. Each transport (cln-grpc, clnrest) runs the same tests.
/// </summary>
public abstract class ClnIntegrationTestBase
{
    private const string Username = "ngoline";
    private static readonly string Metadata = LnUrlMetadata.Build(Username, "ln.example.com");

    private readonly ClnRegtestFixture _fixture;
    private readonly ILightningService _clnService;

    protected ClnIntegrationTestBase(ClnRegtestFixture fixture)
    {
        _fixture = fixture;
        // ReSharper disable once VirtualMemberCallInConstructor
        _clnService = CreateService(fixture);
    }

    protected abstract ILightningService CreateService(ClnRegtestFixture fixture);

    [Fact]
    public async Task Given_SyncedNode_When_CheckConnection_Then_Expect_True()
    {
        var isConnected = await _clnService.CheckConnection();

        Assert.True(isConnected);
    }

    [Fact]
    public async Task Given_ValidAmount_When_FetchInvoice_Then_Expect_DescriptionHashOfMetadata()
    {
        // Arrange
        const long expectedAmount = 10_000L;

        // Act
        var invoice = (await _clnService.FetchInvoiceAsync(expectedAmount, Username, Metadata, null)).PaymentRequest;

        // Assert
        var decodedInvoice = Invoice.Decode(invoice, BitcoinNetwork.Regtest);
        Assert.NotNull(decodedInvoice);
        Assert.Equal(_fixture.NodePubKeyHex, Convert.ToHexStringLower(decodedInvoice.PayeePubKey?.ToBytes() ?? []));
        Assert.Equal(expectedAmount, (long)decodedInvoice.Amount.MilliSatoshi);
        Assert.True(string.IsNullOrEmpty(decodedInvoice.Description));
        // LUD-06: wallets check the h tag against sha256(metadata)
        Assert.Equal(Convert.ToHexStringLower(LnUrlMetadata.Hash(Metadata)), await _fixture.DecodeDescriptionHashAsync(invoice));
    }

    [Fact]
    public async Task Given_Comment_When_FetchInvoice_Then_Expect_CommentInLabelAndDescriptionHashUnchanged()
    {
        // Act
        var invoice = await _clnService.FetchInvoiceAsync(10_000, Username, Metadata, "thanks for the coffee");

        // Assert
        Assert.Equal(Convert.ToHexStringLower(LnUrlMetadata.Hash(Metadata)),
            await _fixture.DecodeDescriptionHashAsync(invoice.PaymentRequest));
        var label = await _fixture.GetInvoiceLabelAsync(invoice.PaymentHash);
        Assert.StartsWith($"lnaddress-{Username}-", label);
        Assert.EndsWith(": thanks for the coffee", label);
    }

    [Fact]
    public async Task Given_SameUser_When_FetchInvoiceTwice_Then_Expect_DistinctInvoices()
    {
        // CLN rejects duplicate labels, so two requests for the same user must not collide
        var first = await _clnService.FetchInvoiceAsync(10_000, Username, Metadata, null);
        var second = await _clnService.FetchInvoiceAsync(10_000, Username, Metadata, null);

        Assert.NotEqual(first.PaymentRequest, second.PaymentRequest);
        Assert.NotEqual(first.PaymentHash, second.PaymentHash);
    }

    [Fact]
    public async Task Given_UnpaidInvoice_When_LookupInvoice_Then_Expect_NotSettled()
    {
        // Arrange
        var invoice = await _clnService.FetchInvoiceAsync(10_000, Username, Metadata, null);

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
        var invoice = await _clnService.FetchInvoiceAsync(10_000, Username, Metadata, null);
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
    public async Task Given_InvoiceNotCreatedByLnAddress_When_LookupInvoice_Then_Expect_Null()
    {
        // Another app on the same node created this invoice, so verify must not reveal it
        var paymentHash = await _fixture.CreateForeignInvoiceAsync();

        var status = await _clnService.LookupInvoiceAsync(paymentHash);

        Assert.Null(status);
    }

    [Fact]
    public async Task Given_UnknownPaymentHash_When_LookupInvoice_Then_Expect_Null()
    {
        var unknownHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        var status = await _clnService.LookupInvoiceAsync(unknownHash);

        Assert.Null(status);
    }
}
