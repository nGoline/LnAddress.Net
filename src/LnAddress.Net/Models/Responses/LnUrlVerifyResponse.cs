using System.Text.Json.Serialization;

namespace LnAddress.Net.Models.Responses;

/// <summary>
/// LUD-21 verify response.
/// </summary>
public class LnUrlVerifyResponse(string pr, bool settled, string? preimage)
{
    [JsonPropertyName("status")]
    public string Status { get; } = "OK";

    /// <summary>
    /// true once the invoice has been paid
    /// </summary>
    [JsonPropertyName("settled")]
    public bool Settled { get; } = settled;

    /// <summary>
    /// hex preimage of the invoice, null until it is settled
    /// </summary>
    [JsonPropertyName("preimage")]
    public string? Preimage { get; } = preimage;

    /// <summary>
    /// bech32-serialized lightning invoice
    /// </summary>
    [JsonPropertyName("pr")]
    public string Pr { get; } = pr;
}
