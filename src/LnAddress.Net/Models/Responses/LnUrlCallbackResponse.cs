using System.Text.Json.Serialization;

namespace LnAddress.Net.Models.Responses;

public class LnUrlCallbackResponse(string pr, string verify)
{
    /// <summary>
    /// bech32-serialized lightning invoice
    /// </summary>
    [JsonPropertyName("pr")]
    public string Pr { get; } = pr;

    /// <summary>
    /// an empty array
    /// </summary>
    [JsonPropertyName("routes")]
    public string[] Routes { get; } = [];

    /// <summary>
    /// LUD-21 URL the payer can poll to learn whether the invoice was settled
    /// </summary>
    [JsonPropertyName("verify")]
    public string Verify { get; } = verify;
}
