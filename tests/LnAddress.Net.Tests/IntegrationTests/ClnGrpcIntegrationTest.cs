using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace LnAddress.Net.Tests.IntegrationTests;

using Fixtures;
using LnAddress.Net.Interfaces;
using LnAddress.Net.Services;

[Collection("cln-regtest")]
public class ClnGrpcIntegrationTest(ClnRegtestFixture fixture) : ClnIntegrationTestBase(fixture)
{
    protected override ILightningService CreateService(ClnRegtestFixture fixture)
    {
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

        return new ClnService(configuration, new Mock<ILogger<ClnService>>().Object);
    }
}
