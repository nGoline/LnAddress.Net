using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LnAddress.Net.Tests.Helpers;

using LnAddress.Net.Helpers;

public class LnUrlMetadataTests
{
    [Theory]
    [InlineData("alice", "alice")]
    [InlineData("Alice", "alice")]
    [InlineData("a.l-i_c3", "a.l-i_c3")]
    public void Given_ValidUsername_When_Normalizing_Then_Expect_Lowercase(string username, string expected)
    {
        Assert.True(LnUrlMetadata.TryNormalizeUsername(username, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("al ice")]
    [InlineData("al\"ice")]
    [InlineData("al\\ice")]
    [InlineData("alice@example.com")]
    [InlineData("alice\n")]
    [InlineData("ålice")]
    [InlineData("Kelvin")] // Kelvin sign, lowercases to an ASCII 'k'
    public void Given_InvalidUsername_When_Normalizing_Then_Expect_False(string? username)
    {
        Assert.False(LnUrlMetadata.TryNormalizeUsername(username, out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void Given_UsernameAndDomain_When_Building_Then_Expect_PlainTextAndIdentifier()
    {
        var metadata = LnUrlMetadata.Build("alice", "LN.Example.com");

        Assert.Equal("""[["text/plain","Paying alice@ln.example.com"],["text/identifier","alice@ln.example.com"]]""", metadata);
        // Must be a JSON array of [mime, content] pairs
        var entries = JsonSerializer.Deserialize<string[][]>(metadata);
        Assert.NotNull(entries);
        Assert.All(entries, entry => Assert.Equal(2, entry.Length));
    }

    [Fact]
    public void Given_Metadata_When_Hashing_Then_Expect_Sha256OfUtf8()
    {
        const string metadata = """[["text/plain","Paying alice@ln.example.com"]]""";

        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(metadata)), LnUrlMetadata.Hash(metadata));
    }
}
