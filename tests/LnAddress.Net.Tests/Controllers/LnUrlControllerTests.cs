using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace LnAddress.Net.Tests.Controllers;

using LnAddress.Net.Controllers;
using LnAddress.Net.Interfaces;
using LnAddress.Net.Models.Lightning;
using LnAddress.Net.Models.Responses;

public class LnUrlControllerTests
{
    private const string PaymentHash = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";
    private const string Preimage = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
    private const string Bolt11 = "lnbcrt100n1example";

    private readonly Mock<ILightningService> _lightningService = new();

    private LnUrlController BuildController()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("ln.example.com");

        return new LnUrlController(_lightningService.Object, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public async Task Given_ValidAmount_When_Callback_Then_Expect_VerifyUrlWithPaymentHash()
    {
        _lightningService
            .Setup(s => s.FetchInvoiceAsync(10_000, "alice", null))
            .ReturnsAsync(new CreatedInvoice(Bolt11, PaymentHash));

        var result = await BuildController().Get("alice", 10_000, null);

        var response = Assert.IsType<LnUrlCallbackResponse>(result.Value);
        Assert.Equal(Bolt11, response.Pr);
        Assert.Equal($"https://ln.example.com/lnurl/verify/{PaymentHash}", response.Verify);
    }

    [Fact]
    public async Task Given_UnpaidInvoice_When_Verify_Then_Expect_NotSettledWithNullPreimage()
    {
        _lightningService
            .Setup(s => s.LookupInvoiceAsync(PaymentHash))
            .ReturnsAsync(new InvoiceStatus(Bolt11, false, null));

        var result = await BuildController().Verify(PaymentHash);

        var response = Assert.IsType<LnUrlVerifyResponse>(result.Value);
        Assert.Equal("OK", response.Status);
        Assert.False(response.Settled);
        Assert.Null(response.Preimage);
        Assert.Equal(Bolt11, response.Pr);

        // LUD-21 requires the preimage key to be present with a null value while unsettled
        var json = JsonSerializer.Serialize(response);
        Assert.Contains("\"preimage\":null", json);
    }

    [Fact]
    public async Task Given_PaidInvoice_When_Verify_Then_Expect_SettledWithPreimage()
    {
        _lightningService
            .Setup(s => s.LookupInvoiceAsync(PaymentHash))
            .ReturnsAsync(new InvoiceStatus(Bolt11, true, Preimage));

        var result = await BuildController().Verify(PaymentHash);

        var response = Assert.IsType<LnUrlVerifyResponse>(result.Value);
        Assert.True(response.Settled);
        Assert.Equal(Preimage, response.Preimage);
    }

    [Fact]
    public async Task Given_UppercasePaymentHash_When_Verify_Then_Expect_LowercaseLookup()
    {
        _lightningService
            .Setup(s => s.LookupInvoiceAsync(PaymentHash))
            .ReturnsAsync(new InvoiceStatus(Bolt11, false, null));

        var result = await BuildController().Verify(PaymentHash.ToUpperInvariant());

        Assert.IsType<LnUrlVerifyResponse>(result.Value);
        _lightningService.Verify(s => s.LookupInvoiceAsync(PaymentHash), Times.Once);
    }

    [Fact]
    public async Task Given_UnknownPaymentHash_When_Verify_Then_Expect_NotFoundError()
    {
        _lightningService
            .Setup(s => s.LookupInvoiceAsync(PaymentHash))
            .ReturnsAsync((InvoiceStatus?)null);

        var result = await BuildController().Verify(PaymentHash);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var error = Assert.IsType<ErrorResponse>(notFound.Value);
        Assert.Equal("ERROR", error.Status);
        Assert.Equal("Not found", error.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zz1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0")]
    [InlineData("0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0ff")]
    [InlineData("0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0\n")]
    public async Task Given_MalformedPaymentHash_When_Verify_Then_Expect_NotFoundWithoutBackendCall(string paymentHash)
    {
        var result = await BuildController().Verify(paymentHash);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        _lightningService.Verify(s => s.LookupInvoiceAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Given_BackendFailure_When_Verify_Then_Expect_InternalServerError()
    {
        _lightningService
            .Setup(s => s.LookupInvoiceAsync(PaymentHash))
            .ThrowsAsync(new Exception("Error looking up invoice on server"));

        var result = await BuildController().Verify(PaymentHash);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
        var error = Assert.IsType<ErrorResponse>(status.Value);
        Assert.Equal("Error looking up invoice on server", error.Reason);
    }
}
