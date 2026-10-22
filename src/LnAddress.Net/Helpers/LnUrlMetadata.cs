using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LnAddress.Net.Helpers;

/// <summary>
/// Builds the LNURL-pay metadata for a Lightning Address. The well-known response serves this exact string and
/// invoices commit to it through their description hash (LUD-06), so both must build it here.
/// </summary>
public static partial class LnUrlMetadata
{
    /// <summary>
    /// Checks a username against the LUD-16 charset (a-z, 0-9, '-', '_' and '.') and lowercases it.
    /// </summary>
    public static bool TryNormalizeUsername(string? username, [NotNullWhen(true)] out string? normalized)
    {
        if (username is null || !UsernameRegex().IsMatch(username))
        {
            normalized = null;
            return false;
        }

        normalized = username.ToLowerInvariant();
        return true;
    }

    /// <summary>
    /// Builds the metadata JSON for <c>username@domain</c>. The username must be normalized and the domain must not
    /// carry a port.
    /// </summary>
    public static string Build(string username, string domain)
    {
        var identifier = $"{username}@{domain.ToLowerInvariant()}";

        // LUD-16 requires a text/identifier entry matching the address being paid
        return JsonSerializer.Serialize(new[]
        {
            new[] { "text/plain", $"Paying {identifier}" },
            new[] { "text/identifier", identifier }
        });
    }

    /// <summary>
    /// The description hash an invoice must carry for the given metadata: <c>sha256(utf8(metadata))</c>.
    /// </summary>
    public static byte[] Hash(string metadata) => SHA256.HashData(Encoding.UTF8.GetBytes(metadata));

    [GeneratedRegex(@"^[A-Za-z0-9\-_.]+\z")]
    private static partial Regex UsernameRegex();
}
