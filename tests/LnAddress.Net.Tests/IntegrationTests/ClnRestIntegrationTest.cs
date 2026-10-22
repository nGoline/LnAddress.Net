using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace LnAddress.Net.Tests.IntegrationTests;

using Fixtures;
using LnAddress.Net.Helpers;
using LnAddress.Net.Interfaces;
using LnAddress.Net.Services;

[Collection("cln-regtest")]
public class ClnRestIntegrationTest(ClnRegtestFixture fixture) : ClnIntegrationTestBase(fixture)
{
    private readonly ClnRegtestFixture _fixture = fixture;

    protected override ILightningService CreateService(ClnRegtestFixture fixture)
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "ClnRest:Url", fixture.RestAddress },
            { "ClnRest:Rune", fixture.Rune },
            { "ClnRest:CaCert", Convert.ToBase64String(Encoding.UTF8.GetBytes(fixture.CaCertPem)) }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        return new ClnRestService(configuration, new Mock<ILogger<ClnRestService>>().Object);
    }

    [Theory]
    [InlineData("listfunds", "{}")]
    [InlineData("withdraw", """{"destination":"bcrt1qw508d6qejxtdg4y5r3zarvary0c5xw7kygt080","satoshi":1000}""")]
    [InlineData("pay", """{"bolt11":"lnbcrt1"}""")]
    [InlineData("createrune", "{}")]
    [InlineData("listinvoices", "{}")]
    [InlineData("invoice", """{"amount_msat":1000,"label":"other-app","description":"spoof"}""")]
    public async Task Given_RecommendedRune_When_CallingAnythingElse_Then_Expect_Unauthorized(string method, string body)
    {
        var caCert = X509Certificate2.CreateFromPem(_fixture.CaCertPem);
        using var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) => CertificateHelpers.IsIssuedByCa(cert, caCert);
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("Rune", _fixture.Rune);

        using var response = await client.PostAsync($"{_fixture.RestAddress}/v1/{method}",
            new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestContext.Current.CancellationToken);
        Assert.StartsWith("Not permitted", error?["message"].ToString());
    }

    [Fact]
    public void Given_Readme_When_Reading_Then_Expect_RecommendedRuneRestrictions()
    {
        // The rune these tests use must be the one users are told to create
        var readme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../README.md"));

        Assert.Contains($"restrictions='{ClnRegtestFixture.RecommendedRuneRestrictions}'", readme);
    }
}
