namespace LnAddress.Net.Models.Lightning;

public record InvoiceStatus(string PaymentRequest, bool Settled, string? Preimage);
