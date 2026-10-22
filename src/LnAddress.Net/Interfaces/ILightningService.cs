namespace LnAddress.Net.Interfaces;

using Models.Lightning;

public interface ILightningService
{
    /// <summary>
    /// Creates an invoice that commits to <paramref name="metadata"/> through its description hash (LUD-06).
    /// The LUD-12 <paramref name="comment"/> can't go in the description without breaking that hash; backends
    /// that can store it next to the invoice do so, the others drop it.
    /// </summary>
    Task<CreatedInvoice> FetchInvoiceAsync(long valueMillisats, string username, string metadata, string? comment);

    /// <summary>
    /// Looks up an invoice by payment hash. Returns null when the node doesn't know it, or when the backend can
    /// tell the invoice wasn't created by LnAddress.
    /// </summary>
    Task<InvoiceStatus?> LookupInvoiceAsync(string paymentHash);
    Task<bool> CheckConnection();
}
