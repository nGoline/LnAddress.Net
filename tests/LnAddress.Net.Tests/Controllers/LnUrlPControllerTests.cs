using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace LnAddress.Net.Tests.Controllers;

using LnAddress.Net.Controllers;
using LnAddress.Net.Models.Responses;

public class LnUrlPControllerTests
{
    private static LnUrlPController BuildController(string host = "ln.example.com")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString(host);

        return new LnUrlPController(configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public void Given_Username_When_Get_Then_Expect_IdentifierInMetadata()
    {
        var result = BuildController("ln.example.com:8443").Get("Alice");

        var response = Assert.IsType<LnAddressWellKnownResponse>(result.Value);
        Assert.Equal("https://ln.example.com:8443/lnurl/callback/alice", response.Callback);
        // LUD-16: the identifier carries the address without the port
        var entries = JsonSerializer.Deserialize<string[][]>(response.Metadata!);
        Assert.Contains(entries!, entry => entry is ["text/identifier", "alice@ln.example.com"]);
    }

    [Theory]
    [InlineData("al\"ice")]
    [InlineData("al\\ice")]
    [InlineData("al ice")]
    [InlineData("alice@example.com")]
    public void Given_InvalidUsername_When_Get_Then_Expect_NotFound(string username)
    {
        var result = BuildController().Get(username);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Not found", Assert.IsType<ErrorResponse>(notFound.Value).Reason);
    }
}
