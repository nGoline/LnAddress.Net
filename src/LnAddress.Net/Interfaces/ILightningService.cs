namespace LnAddress.Net.Interfaces;

using Models.Lightning;

public interface ILightningService
{
    Task<CreatedInvoice> FetchInvoiceAsync(long valueMillisats, string username, string? comment);
    Task<InvoiceStatus?> LookupInvoiceAsync(string paymentHash);
    Task<bool> CheckConnection();
}
